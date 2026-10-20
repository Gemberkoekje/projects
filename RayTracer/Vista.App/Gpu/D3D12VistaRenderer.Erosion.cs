using System.Diagnostics;
using Vista.World;
using Vortice.Direct3D12;

namespace Vista.App.Gpu;

/// <summary>One of the five passes of an erosion step (<see cref="ErosionReference"/>), for the self-test's parity checks.</summary>
internal enum ErosionPass
{
    None,
    Flux,
    Water,
    Transport,
    ThermalOut,
    ThermalApply,
}

/// <summary>
/// The GPU erosion (vista-plan §10 M1.7): <c>Shaders/VistaErosion.hlsl</c> on the renderer's device, so the world stage runs on
/// the device before the mesh. The state lives in UAV buffers laid out as <see cref="ErosionState"/>'s; a step is five
/// dispatches separated by UAV barriers, and steps are batched into command lists of about <see cref="ErosionBudgetSeconds"/>
/// of GPU time each (measured as it goes, starting from one step), far below the Windows GPU timeout. The buffers live only
/// for one erosion.
/// </summary>
internal sealed partial class D3D12VistaRenderer : ITerrainEroder
{
    private const string ErosionShaderFile = "VistaErosion.hlsl";
    private const int ErosionGroup = 8;
    private const int ClearGroup = 64;

    /// <summary>GPU seconds a submission of erosion steps aims for.</summary>
    private const double ErosionBudgetSeconds = 0.1;

    /// <summary>The most steps one submission records.</summary>
    private const int MaxStepsPerSubmission = 256;

    /// <summary>The erosion bindings, by root parameter slot (registers in the comments).</summary>
    private enum ErosionSlot
    {
        Constants,   // b0
        TerrainIn,   // u0
        WaterIn,     // u1
        SedimentIn,  // u2
        Flux,        // u3
        TerrainOut,  // u4
        WaterOut,    // u5
        SedimentOut, // u6
        Flow,        // u7
        Thermal,     // u8
        Clear,       // u9
    }

    private ID3D12RootSignature? _erosionRootSignature;
    private ID3D12PipelineState? _fluxPipeline;
    private ID3D12PipelineState? _waterPipeline;
    private ID3D12PipelineState? _transportPipeline;
    private ID3D12PipelineState? _thermalOutPipeline;
    private ID3D12PipelineState? _thermalApplyPipeline;
    private ID3D12PipelineState? _clearPipeline;

    /// <summary>The erosion state's buffers on the device: A and B sides of the double-buffered layers, and the rest.</summary>
    private sealed class ErosionBuffers(Func<ulong, ID3D12Resource> create, int size) : IDisposable
    {
        public readonly int Size = size;
        public readonly ID3D12Resource[] Terrain = [create(Bytes(size, 2)), create(Bytes(size, 2))];
        public readonly ID3D12Resource[] Water = [create(Bytes(size, 1)), create(Bytes(size, 1))];
        public readonly ID3D12Resource[] Sediment = [create(Bytes(size, 1)), create(Bytes(size, 1))];
        public readonly ID3D12Resource Flux = create(Bytes(size, 4));
        public readonly ID3D12Resource Flow = create(Bytes(size, 2));
        public readonly ID3D12Resource Thermal = create(Bytes(size, 4));
        // Each step's constants (accumulating or not) and the clear's, in their own upload buffers.
        public readonly ID3D12Resource[] Constants = new ID3D12Resource[3];

        /// <summary>Bytes of a buffer of <paramref name="floats"/> floats per cell, rounded up to 16 for the clear kernel.</summary>
        public static ulong Bytes(int size, int floats) => ((ulong)size * (ulong)size * (ulong)floats * sizeof(float) + 15ul) & ~15ul;

        public long StateBytes => (long)(Bytes(Size, 2) * 2 + Bytes(Size, 1) * 4 + Bytes(Size, 4) * 2 + Bytes(Size, 2));

        public IEnumerable<ID3D12Resource> All => Terrain.Concat(Water).Concat(Sediment).Concat([Flux, Flow, Thermal]);

        public void Dispose()
        {
            foreach (ID3D12Resource buffer in All.Concat(Constants.Where(c => c is not null)))
                buffer.Dispose();
        }
    }

    private void EnsureErosionPipelines()
    {
        if (_erosionRootSignature is not null)
            return;
        var rootParameters = new[]
        {
            Cbv(0),
            Uav(0), Uav(1), Uav(2), Uav(3), Uav(4), Uav(5), Uav(6), Uav(7), Uav(8), Uav(9),
        };
        _erosionRootSignature = _device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.None, rootParameters));
        string source = ShaderSource.Load(ErosionShaderFile);
        _fluxPipeline = CompilePipeline(_erosionRootSignature, source, "CSFlux", ErosionShaderFile);
        _waterPipeline = CompilePipeline(_erosionRootSignature, source, "CSWater", ErosionShaderFile);
        _transportPipeline = CompilePipeline(_erosionRootSignature, source, "CSTransport", ErosionShaderFile);
        _thermalOutPipeline = CompilePipeline(_erosionRootSignature, source, "CSThermalOut", ErosionShaderFile);
        _thermalApplyPipeline = CompilePipeline(_erosionRootSignature, source, "CSThermalApply", ErosionShaderFile);
        _clearPipeline = CompilePipeline(_erosionRootSignature, source, "CSClear", ErosionShaderFile);
    }

    /// <inheritdoc />
    public string DriverVersion { get; private set; } = string.Empty;

    /// <summary>Video memory this process uses on the adapter, in bytes (DXGI's local segment), or 0 when it cannot be asked.</summary>
    public long VideoMemoryUsed => _adapter is null ? 0 : (long)_adapter.QueryVideoMemoryInfo(0, Vortice.DXGI.MemorySegmentGroup.Local).CurrentUsage;

    /// <inheritdoc />
    public ErosionRun Erode(HeightField ground, ErosionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(settings);
        IReadOnlyList<string> errors = settings.Validate();
        if (errors.Count > 0)
            throw new ArgumentException("Invalid erosion settings: " + string.Join(" ", errors), nameof(settings));
        var wall = Stopwatch.StartNew();
        EnsureErosionPipelines();
        int n = ground.Size;
        int cells = n * n;
        using var buffers = new ErosionBuffers(bytes => CreateUavBuffer(bytes), n);

        var terrain = new float[cells * 2];
        ReadOnlySpan<float> heights = ground.Heights;
        for (int c = 0; c < cells; c++)
            terrain[c * 2] = heights[c];
        Upload(buffers.Terrain[0], terrain);
        terrain = [];
        foreach (ID3D12Resource buffer in buffers.All.Where(b => b != buffers.Terrain[0]))
            Clear(buffers, buffer);

        long memory = VideoMemoryUsed;
        (double seconds, int submissions, double longest, int water) = RunSteps(buffers, settings, ground.CellSize, settings.Iterations, firstWater: 0, firstStep: 0);
        int flowSteps = Math.Max(0, settings.Iterations - settings.AverageFrom);
        ErosionResult result = ErosionResult.From(n, ground.CellSize,
            ReadFloats(buffers.Terrain[0], cells * 2), ReadFloats(buffers.Water[water], cells),
            ReadFloats(buffers.Sediment[0], cells), ReadFloats(buffers.Flow, cells * 2), flowSteps, settings.Iterations);
        return new ErosionRun(result, new ErosionStats(seconds, wall.Elapsed.TotalSeconds, submissions, longest, buffers.StateBytes) { ProcessVideoMemory = memory });
    }

    /// <summary>
    /// Runs <paramref name="steps"/> steps of <paramref name="settings"/> from <paramref name="state"/> on the GPU and returns the
    /// state after them (for the self-test's parity with <see cref="ErosionReference.Run"/>).
    /// </summary>
    public ErosionState RunErosion(ErosionState state, ErosionSettings settings, int steps)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);
        EnsureErosionPipelines();
        using var buffers = UploadState(state);
        (_, _, _, int water) = RunSteps(buffers, settings, state.CellSize, steps, firstWater: 0, firstStep: state.Steps);
        ErosionState after = state.Clone();
        int flowSteps = 0;
        for (int s = state.Steps; s < state.Steps + steps; s++)
            flowSteps += s >= settings.AverageFrom ? 1 : 0;
        ReadInto(buffers.Terrain[0], after.Terrain);
        ReadInto(buffers.Water[water], after.Water);
        ReadInto(buffers.Sediment[0], after.Sediment);
        ReadInto(buffers.Flux, after.Flux);
        ReadInto(buffers.Flow, after.Flow);
        ReadInto(buffers.Thermal, after.Thermal);
        after.FlowSteps += flowSteps;
        after.Steps += steps;
        return after;
    }

    /// <summary>
    /// Runs one <paramref name="pass"/> on <paramref name="state"/> with the bindings it has within a step, the state uploaded to
    /// the buffers that pass reads (for <see cref="ErosionPass.Transport"/> the state's sediment is the one pass 2 left; for the
    /// thermal passes its ground is the one pass 2 left), and returns what the pass wrote, in the order of the reference's output
    /// arguments.
    /// </summary>
    public float[][] RunErosionPass(ErosionState state, ErosionSettings settings, ErosionPass pass, bool accumulate)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);
        EnsureErosionPipelines();
        using var buffers = UploadState(state);
        if (pass == ErosionPass.Transport)
            Upload(buffers.Sediment[1], state.Sediment);
        if (pass is ErosionPass.ThermalOut or ErosionPass.ThermalApply)
            Upload(buffers.Terrain[1], state.Terrain);
        WriteErosionConstants(buffers, settings, state.CellSize);
        int cells = state.Size * state.Size;
        Timed(PipelineOf(pass), _erosionRootSignature!, list => RecordPass(list, buffers, pass, accumulate, water: 0));
        return pass switch
        {
            ErosionPass.Flux => [ReadFloats(buffers.Flux, cells * 4)],
            ErosionPass.Water => [ReadFloats(buffers.Terrain[1], cells * 2), ReadFloats(buffers.Water[1], cells), ReadFloats(buffers.Sediment[1], cells), ReadFloats(buffers.Flow, cells * 2)],
            ErosionPass.Transport => [ReadFloats(buffers.Sediment[0], cells)],
            ErosionPass.ThermalOut => [ReadFloats(buffers.Thermal, cells * 4)],
            ErosionPass.ThermalApply => [ReadFloats(buffers.Terrain[0], cells * 2)],
            _ => throw new ArgumentOutOfRangeException(nameof(pass), pass, "Unknown erosion pass."),
        };
    }

    private ErosionBuffers UploadState(ErosionState state)
    {
        var buffers = new ErosionBuffers(bytes => CreateUavBuffer(bytes), state.Size);
        Upload(buffers.Terrain[0], state.Terrain);
        Upload(buffers.Water[0], state.Water);
        Upload(buffers.Sediment[0], state.Sediment);
        Upload(buffers.Flux, state.Flux);
        Upload(buffers.Flow, state.Flow);
        Upload(buffers.Thermal, state.Thermal);
        foreach (ID3D12Resource buffer in new[] { buffers.Terrain[1], buffers.Water[1], buffers.Sediment[1] })
            Clear(buffers, buffer);
        return buffers;
    }

    /// <summary>
    /// Runs the steps in submissions of about <see cref="ErosionBudgetSeconds"/>; returns the GPU seconds, the submissions, the
    /// longest submission's seconds, and which water buffer holds the result.
    /// </summary>
    private (double Seconds, int Submissions, double Longest, int Water) RunSteps(ErosionBuffers buffers, ErosionSettings settings, double cell,
        int steps, int firstWater, int firstStep)
    {
        WriteErosionConstants(buffers, settings, cell);
        double seconds = 0.0, longest = 0.0;
        int submissions = 0, water = firstWater, done = 0, batch = 1;
        while (done < steps)
        {
            int count = Math.Min(batch, steps - done);
            int start = done;
            int waterAtStart = water;
            double taken = Timed(_fluxPipeline!, _erosionRootSignature!, list =>
            {
                int w = waterAtStart;
                for (int s = 0; s < count; s++)
                {
                    bool accumulate = firstStep + start + s >= settings.AverageFrom;
                    foreach (ErosionPass pass in Passes)
                        RecordPass(list, buffers, pass, accumulate, w);
                    w = 1 - w;
                }
            });
            water = (count % 2 == 0) ? water : 1 - water;
            done += count;
            seconds += taken;
            longest = Math.Max(longest, taken);
            submissions++;
            double perStep = taken / count;
            batch = Math.Clamp((int)(ErosionBudgetSeconds / Math.Max(perStep, 1e-6)), 1, MaxStepsPerSubmission);
        }
        return (seconds, submissions, longest, water);
    }

    private static readonly ErosionPass[] Passes = [ErosionPass.Flux, ErosionPass.Water, ErosionPass.Transport, ErosionPass.ThermalOut, ErosionPass.ThermalApply];

    private ID3D12PipelineState PipelineOf(ErosionPass pass) => pass switch
    {
        ErosionPass.Flux => _fluxPipeline!,
        ErosionPass.Water => _waterPipeline!,
        ErosionPass.Transport => _transportPipeline!,
        ErosionPass.ThermalOut => _thermalOutPipeline!,
        ErosionPass.ThermalApply => _thermalApplyPipeline!,
        _ => throw new ArgumentOutOfRangeException(nameof(pass), pass, "Unknown erosion pass."),
    };

    /// <summary>
    /// Records one pass of a step whose water is in <c>Water[water]</c>, followed by a UAV barrier. The A side is Terrain[0],
    /// Sediment[0] and Water[water]; pass 2 writes the B side, pass 3 moves the sediment back to A, passes 4–5 bring the ground
    /// back to A.
    /// </summary>
    private void RecordPass(ID3D12GraphicsCommandList4 list, ErosionBuffers b, ErosionPass pass, bool accumulate, int water)
    {
        var bind = new Dictionary<ErosionSlot, ID3D12Resource>();
        switch (pass)
        {
            case ErosionPass.Flux:
                (bind[ErosionSlot.TerrainIn], bind[ErosionSlot.WaterIn], bind[ErosionSlot.Flux]) = (b.Terrain[0], b.Water[water], b.Flux);
                break;
            case ErosionPass.Water:
                (bind[ErosionSlot.TerrainIn], bind[ErosionSlot.WaterIn], bind[ErosionSlot.SedimentIn], bind[ErosionSlot.Flux]) = (b.Terrain[0], b.Water[water], b.Sediment[0], b.Flux);
                (bind[ErosionSlot.TerrainOut], bind[ErosionSlot.WaterOut], bind[ErosionSlot.SedimentOut], bind[ErosionSlot.Flow]) = (b.Terrain[1], b.Water[1 - water], b.Sediment[1], b.Flow);
                break;
            case ErosionPass.Transport:
                (bind[ErosionSlot.WaterIn], bind[ErosionSlot.Flux], bind[ErosionSlot.SedimentIn], bind[ErosionSlot.SedimentOut]) = (b.Water[water], b.Flux, b.Sediment[1], b.Sediment[0]);
                break;
            case ErosionPass.ThermalOut:
                (bind[ErosionSlot.TerrainIn], bind[ErosionSlot.Thermal]) = (b.Terrain[1], b.Thermal);
                break;
            case ErosionPass.ThermalApply:
                (bind[ErosionSlot.TerrainIn], bind[ErosionSlot.Thermal], bind[ErosionSlot.TerrainOut]) = (b.Terrain[1], b.Thermal, b.Terrain[0]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(pass), pass, "Unknown erosion pass.");
        }
        list.SetPipelineState(PipelineOf(pass));
        list.SetComputeRootConstantBufferView((uint)ErosionSlot.Constants, b.Constants[accumulate ? 1 : 0].GPUVirtualAddress);
        BindErosion(list, bind);
        uint groups = (uint)((b.Size + ErosionGroup - 1) / ErosionGroup);
        list.Dispatch(groups, groups, 1);
        list.ResourceBarrier(new ResourceBarrier(new ResourceUnorderedAccessViewBarrier(null)));
    }

    private void BindErosion(ID3D12GraphicsCommandList4 list, Dictionary<ErosionSlot, ID3D12Resource> bind)
    {
        for (ErosionSlot slot = ErosionSlot.TerrainIn; slot <= ErosionSlot.Clear; slot++)
            list.SetComputeRootUnorderedAccessView((uint)slot, (bind.TryGetValue(slot, out ID3D12Resource? r) ? r : _dummyUav).GPUVirtualAddress);
    }

    private void WriteErosionConstants(ErosionBuffers buffers, ErosionSettings settings, double cell)
    {
        ErosionConstants k = ErosionConstants.For(settings, buffers.Size, cell);
        for (int accumulate = 0; accumulate < 2; accumulate++)
        {
            buffers.Constants[accumulate] ??= CreateBuffer(HeapType.Upload, ConstantBufferBytes, ResourceStates.GenericRead);
            Write(buffers.Constants[accumulate], k with { Accumulate = (uint)accumulate });
        }
    }

    /// <summary>Zeroes <paramref name="buffer"/> (its whole, 16-byte-rounded size).</summary>
    private void Clear(ErosionBuffers buffers, ID3D12Resource buffer)
    {
        buffers.Constants[2] ??= CreateBuffer(HeapType.Upload, ConstantBufferBytes, ResourceStates.GenericRead);
        ulong words = buffer.Description.Width / 16ul;
        Write(buffers.Constants[2], new ErosionConstants { ClearCount = (uint)words });
        Timed(_clearPipeline!, _erosionRootSignature!, list =>
        {
            list.SetComputeRootConstantBufferView((uint)ErosionSlot.Constants, buffers.Constants[2].GPUVirtualAddress);
            BindErosion(list, new Dictionary<ErosionSlot, ID3D12Resource> { [ErosionSlot.Clear] = buffer });
            list.Dispatch((uint)((words + ClearGroup - 1) / ClearGroup), 1, 1);
        });
    }

    /// <summary>Copies <paramref name="data"/> into the start of the UAV buffer <paramref name="buffer"/> through a staging buffer.</summary>
    private void Upload(ID3D12Resource buffer, float[] data)
    {
        if (data.Length == 0)
            return;
        ulong bytes = (ulong)data.Length * sizeof(float);
        using ID3D12Resource staging = CreateUploadBuffer<float>(data);
        _allocator.Reset();
        _commandList.Reset(_allocator, null);
        _commandList.CopyBufferRegion(buffer, 0, staging, 0, bytes);
        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        WaitForGpu();
    }

    private void ReadInto(ID3D12Resource buffer, float[] target) => ReadFloats(buffer, target.Length).CopyTo(target, 0);

    private void DisposeErosion()
    {
        _clearPipeline?.Dispose();
        _thermalApplyPipeline?.Dispose();
        _thermalOutPipeline?.Dispose();
        _transportPipeline?.Dispose();
        _waterPipeline?.Dispose();
        _fluxPipeline?.Dispose();
        _erosionRootSignature?.Dispose();
    }
}
