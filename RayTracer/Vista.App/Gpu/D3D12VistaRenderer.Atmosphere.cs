using System.Numerics;
using System.Runtime.InteropServices;
using Vista.Atmosphere;
using Vortice.Direct3D12;

namespace Vista.App.Gpu;

/// <summary>The atmosphere LUTs of one build, read back from the GPU (for <c>--gpu-selftest</c>'s parity checks).</summary>
/// <param name="Columns">The columns LUT (<see cref="AtmosphereLuts.ColumnsFloats"/> floats).</param>
/// <param name="Orders">The phase-weighted radiance of orders 2 … 5 summed (2 floats per 4D value, 11 wavelengths).</param>
/// <param name="GroundIrradiance">The sky's irradiance on the ground (<see cref="AtmosphereLuts.GroundIrradianceFloats"/> floats).</param>
/// <param name="SkyView">The sky-view LUT (<see cref="AtmosphereLuts.SkyViewFloats"/> floats).</param>
/// <param name="Order2">The tables of the first iteration, when asked for.</param>
internal sealed record AtmosphereLutData(float[] Columns, float[] Orders, float[] GroundIrradiance, float[] SkyView, ScatteringOrder2? Order2);

/// <summary>The tables around the first scattering iteration, for the parity checks of each kernel.</summary>
/// <param name="Direct">The order-0 (direct) ground irradiance.</param>
/// <param name="Single">The single scattering without phases (2 floats per 4D value).</param>
/// <param name="Density">The order-2 phase-weighted radiance (2 floats per 4D value).</param>
/// <param name="Irradiance">The order-1 ground irradiance.</param>
/// <param name="Radiance">The order-2 radiance (1 float per 4D value).</param>
internal sealed record ScatteringOrder2(float[] Direct, float[] Single, float[] Density, float[] Irradiance, float[] Radiance);

/// <summary>The raw sums of one <c>--atmosphere-check</c> run (see <see cref="SkyCheck"/>).</summary>
/// <param name="Sums">Per (direction, wavelength, chunk): Σ L, Σ L², the LUT's L, 0.</param>
/// <param name="GpuSeconds">GPU time of the reference paths.</param>
/// <param name="Dispatches">Dispatches issued.</param>
internal sealed record SkyCheckRaw(float[] Sums, double GpuSeconds, int Dispatches);

internal sealed partial class D3D12VistaRenderer
{
    private const string LutShaderFile = "VistaAtmosphereLuts.hlsl";
    private const int LutGroupSize = 64;

    /// <summary>Work items per LUT dispatch: the 4D density at 32 × 32 directions takes about 0.1 s per million, far below the TDR.</summary>
    private const int LutItemsPerDispatch = 1 << 20;

    /// <summary>The LUT builders' bindings, by root parameter slot (registers in the comments).</summary>
    private enum LutSlot
    {
        Coefficients,   // t8
        Sun,            // t9
        Columns,        // t10
        Single,         // t11
        Order,          // t12
        Weighted,       // t13
        Irradiance,     // t15
        Ground,         // t16
        Output,         // u0
        Sum,            // u1
        Build,          // b0
        Atmosphere,     // b1
    }

    private ID3D12RootSignature _lutRootSignature = null!;
    private ID3D12PipelineState _columnsPipeline = null!;
    private ID3D12PipelineState _directPipeline = null!;
    private ID3D12PipelineState _singlePipeline = null!;
    private ID3D12PipelineState _densityPipeline = null!;
    private ID3D12PipelineState _indirectPipeline = null!;
    private ID3D12PipelineState _multiplePipeline = null!;
    private ID3D12PipelineState _groundPipeline = null!;
    private ID3D12PipelineState _skyViewPipeline = null!;
    private ID3D12PipelineState? _checkPipeline;
    private ID3D12Resource _buildConstants = null!;
    private ID3D12Resource _checkConstants = null!;
    private ID3D12Resource _dummySrv = null!;
    private ID3D12Resource _columnsLut = null!;
    private ID3D12Resource _scatterSum = null!;
    private ID3D12Resource _groundLut = null!;
    private ID3D12Resource _skyViewLut = null!;

    // Per model: the wavelength tables. Rebuilt when the model changes, with everything after them.
    private ID3D12Resource? _atmCoefficients;
    private ID3D12Resource? _atmSun;
    private AtmosphereModel? _tablesModel;
    private (AtmosphereModel Model, double Ground)? _ordersKey;
    private AtmosphereFrame? _skyFrame;

    // A sky check's inputs and sums, alive only during the check.
    private ID3D12Resource? _checkDirections;
    private ID3D12Resource? _checkSums;

    [StructLayout(LayoutKind.Sequential)]
    private struct BuildConstants
    {
        public uint ItemCount, FirstItem, Order, Pad0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CheckConstants
    {
        public uint Directions, Chunks, Paths, FirstPath;
    }

    private void CreateAtmospherePipelines()
    {
        var rootParameters = new[]
        {
            Srv(8), Srv(9), Srv(10), Srv(11), Srv(12), Srv(13), Srv(15), Srv(16), // inputs (see LutSlot)
            Uav(0), Uav(1),                                                       // the table being built, the running sum
            Cbv(0), Cbv(1),                                                       // build and atmosphere constants
        };
        _lutRootSignature = _device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.None, rootParameters));
        string source = ShaderSource.Load(LutShaderFile);
        _columnsPipeline = CompilePipeline(_lutRootSignature, source, "CSColumns", LutShaderFile);
        _directPipeline = CompilePipeline(_lutRootSignature, source, "CSDirectIrradiance", LutShaderFile);
        _singlePipeline = CompilePipeline(_lutRootSignature, source, "CSSingleScattering", LutShaderFile);
        _densityPipeline = CompilePipeline(_lutRootSignature, source, "CSScatteringDensity", LutShaderFile);
        _indirectPipeline = CompilePipeline(_lutRootSignature, source, "CSIndirectIrradiance", LutShaderFile);
        _multiplePipeline = CompilePipeline(_lutRootSignature, source, "CSMultipleScattering", LutShaderFile);
        _groundPipeline = CompilePipeline(_lutRootSignature, source, "CSGroundIrradiance", LutShaderFile);
        _skyViewPipeline = CompilePipeline(_lutRootSignature, source, "CSSkyView", LutShaderFile);
    }

    private void CreateAtmosphereFixedResources()
    {
        _buildConstants = CreateBuffer(HeapType.Upload, ConstantBufferBytes, ResourceStates.GenericRead);
        _checkConstants = CreateBuffer(HeapType.Upload, ConstantBufferBytes, ResourceStates.GenericRead);
        _dummySrv = CreateDefaultBuffer<float>(new float[4]);
        _columnsLut = CreateUavBuffer(AtmosphereLuts.ColumnsFloats * sizeof(float));
        _scatterSum = CreateUavBuffer((ulong)AtmosphereScattering.FloatsPerChannel * 2 * sizeof(float));
        _groundLut = CreateUavBuffer(AtmosphereLuts.GroundIrradianceFloats * sizeof(float));
        _skyViewLut = CreateUavBuffer((ulong)AtmosphereLuts.SkyViewFloats * sizeof(float));
    }

    /// <summary>
    /// Makes the LUTs match <paramref name="frame"/>, rebuilding only what it changed: the wavelength tables and the
    /// columns LUT follow the model; the scattering orders and the ground irradiance also follow the ground altitude;
    /// the sky view follows everything (the sun above all). Returns the GPU seconds spent building, 0 when nothing
    /// was rebuilt. <paramref name="capture"/> receives the first iteration's tables when the orders are rebuilt.
    /// </summary>
    private double EnsureAtmosphere(AtmosphereFrame frame, Action<ScatteringOrder2>? capture = null)
    {
        IReadOnlyList<string> errors = frame.Validate();
        if (errors.Count > 0)
            throw new ArgumentException("Invalid atmosphere: " + string.Join(" ", errors), nameof(frame));
        WriteAtmosphereConstants(frame);

        double seconds = 0.0;
        if (!Equals(_tablesModel, frame.Model))
        {
            AtmosphereTables tables = AtmosphereTables.Build(frame.Model);
            _atmCoefficients?.Dispose();
            _atmSun?.Dispose();
            _atmCoefficients = CreateDefaultBuffer<float>(tables.Coefficients);
            _atmSun = CreateDefaultBuffer<float>(tables.Sun);
            seconds += BuildLut(_columnsPipeline, AtmosphereLuts.ColumnsMuSize * AtmosphereLuts.ColumnsAltitudeSize, 0, new() { [LutSlot.Output] = _columnsLut });
            _tablesModel = frame.Model;
            _ordersKey = null;
            _skyFrame = null;
        }
        if (_ordersKey != (frame.Model, frame.GroundAltitudeMetres))
        {
            seconds += BuildScatteringOrders(capture);
            _ordersKey = (frame.Model, frame.GroundAltitudeMetres);
            _skyFrame = null;
        }
        if (!Equals(_skyFrame, frame))
        {
            seconds += BuildLut(_skyViewPipeline, AtmosphereLuts.SkyViewFloats / 2, 0, new()
            {
                [LutSlot.Columns] = _columnsLut, [LutSlot.Weighted] = _scatterSum, [LutSlot.Ground] = _groundLut, [LutSlot.Output] = _skyViewLut,
            });
            _skyFrame = frame;
        }
        return seconds;
    }

    /// <summary>
    /// Bruneton's iteration (<see cref="AtmosphereScattering"/>): direct irradiance and single scattering, then for each
    /// order the density (added to the sum), the ground irradiance of the order before, and the order's radiance;
    /// last, the sky's irradiance on the ground from the sum. The 4D scratch tables live only for the build.
    /// </summary>
    private double BuildScatteringOrders(Action<ScatteringOrder2>? capture)
    {
        int items4d = AtmosphereScattering.FloatsPerChannel;
        int itemsGround = AtmosphereScattering.IrradianceFloats;
        using ID3D12Resource single = CreateUavBuffer((ulong)items4d * 2 * sizeof(float));
        using ID3D12Resource density = CreateUavBuffer((ulong)items4d * 2 * sizeof(float));
        using ID3D12Resource radiance = CreateUavBuffer((ulong)items4d * sizeof(float));
        using ID3D12Resource irradiance = CreateUavBuffer((ulong)itemsGround * sizeof(float));

        double seconds = BuildLut(_directPipeline, itemsGround, 0, new() { [LutSlot.Columns] = _columnsLut, [LutSlot.Output] = irradiance });
        float[] direct = capture is null ? [] : ReadFloats(irradiance, itemsGround);
        seconds += BuildLut(_singlePipeline, items4d, 0, new() { [LutSlot.Columns] = _columnsLut, [LutSlot.Output] = single });
        float[] order1 = [], order2Density = [], order2Radiance = [];
        for (int order = 2; order <= AtmosphereScattering.Orders; order++)
        {
            seconds += BuildLut(_densityPipeline, items4d, order, new()
            {
                [LutSlot.Columns] = _columnsLut, [LutSlot.Single] = single, [LutSlot.Order] = radiance, [LutSlot.Irradiance] = irradiance,
                [LutSlot.Output] = density, [LutSlot.Sum] = _scatterSum,
            });
            seconds += BuildLut(_indirectPipeline, itemsGround, order - 1, new()
            {
                [LutSlot.Single] = single, [LutSlot.Order] = radiance, [LutSlot.Output] = irradiance,
            });
            seconds += BuildLut(_multiplePipeline, items4d, order, new() { [LutSlot.Weighted] = density, [LutSlot.Output] = radiance });
            if (capture is not null && order == 2)
            {
                order1 = ReadFloats(irradiance, itemsGround);
                order2Density = ReadFloats(density, items4d * 2);
                order2Radiance = ReadFloats(radiance, items4d);
            }
        }
        // The sky's irradiance on the ground is per hero wavelength (50), unlike the irradiance by order (11).
        seconds += BuildLut(_groundPipeline, AtmosphereLuts.GroundIrradianceFloats, 0, new() { [LutSlot.Columns] = _columnsLut, [LutSlot.Weighted] = _scatterSum, [LutSlot.Output] = _groundLut });
        capture?.Invoke(new ScatteringOrder2(direct, ReadFloats(single, items4d * 2), order2Density, order1, order2Radiance));
        return seconds;
    }

    private void WriteAtmosphereConstants(AtmosphereFrame frame)
    {
        AtmosphereModel model = frame.Model;
        Write(_atmosphereConstants, new AtmosphereConstants
        {
            SeaRadius = (float)model.PlanetRadiusMetres,
            GroundAltitude = (float)frame.GroundAltitudeMetres,
            TopAltitude = (float)model.TopAltitudeMetres,
            GroundAlbedo = (float)model.GroundAlbedo,
            RayleighScaleHeight = (float)model.RayleighScaleHeightMetres,
            AerosolScaleHeight = (float)model.AerosolScaleHeightMetres,
            OzonePeak = (float)model.OzonePeakAltitudeMetres,
            OzoneHalfWidth = (float)model.OzoneHalfWidthMetres,
            ToSunX = frame.ToSun.X, ToSunY = frame.ToSun.Y, ToSunZ = frame.ToSun.Z,
            MieG = (float)model.AerosolAsymmetry,
            CameraAltitude = (float)frame.CameraAltitudeMetres,
            TerrainLowAltitude = (float)frame.TerrainLowAltitudeMetres,
            TerrainHighAltitude = (float)frame.TerrainHighAltitudeMetres,
            SunOneMinusCos = (float)SolarSpectrum.OneMinusCosRadius,
        });
    }

    /// <summary>
    /// Runs one LUT builder over <paramref name="items"/> work items with the given bindings (every other input slot
    /// gets a dummy, never a buffer that is also being written); returns its GPU seconds.
    /// </summary>
    private double BuildLut(ID3D12PipelineState pipeline, int items, int order, Dictionary<LutSlot, ID3D12Resource> bindings)
    {
        double seconds = 0.0;
        for (int first = 0; first < items; first += LutItemsPerDispatch)
            seconds += BuildLutChunk(pipeline, items, first, Math.Min(LutItemsPerDispatch, items - first), order, bindings);
        return seconds;
    }

    private double BuildLutChunk(ID3D12PipelineState pipeline, int items, int first, int count, int order, Dictionary<LutSlot, ID3D12Resource> bindings)
    {
        Write(_buildConstants, new BuildConstants { ItemCount = (uint)items, FirstItem = (uint)first, Order = (uint)order });
        ulong Address(LutSlot slot) => (bindings.TryGetValue(slot, out ID3D12Resource? r) ? r : slot is LutSlot.Output or LutSlot.Sum ? _dummyUav : _dummySrv).GPUVirtualAddress;
        return Timed(pipeline, _lutRootSignature, list =>
        {
            list.SetComputeRootShaderResourceView((uint)LutSlot.Coefficients, _atmCoefficients!.GPUVirtualAddress);
            list.SetComputeRootShaderResourceView((uint)LutSlot.Sun, _atmSun!.GPUVirtualAddress);
            foreach (LutSlot slot in new[] { LutSlot.Columns, LutSlot.Single, LutSlot.Order, LutSlot.Weighted, LutSlot.Irradiance, LutSlot.Ground })
                list.SetComputeRootShaderResourceView((uint)slot, Address(slot));
            list.SetComputeRootUnorderedAccessView((uint)LutSlot.Output, Address(LutSlot.Output));
            list.SetComputeRootUnorderedAccessView((uint)LutSlot.Sum, Address(LutSlot.Sum));
            list.SetComputeRootConstantBufferView((uint)LutSlot.Build, _buildConstants.GPUVirtualAddress);
            list.SetComputeRootConstantBufferView((uint)LutSlot.Atmosphere, _atmosphereConstants.GPUVirtualAddress);
            list.Dispatch((uint)((count + LutGroupSize - 1) / LutGroupSize), 1, 1);
        });
    }

    /// <summary>
    /// Builds the LUTs for <paramref name="frame"/> (if they are not already) and reads them back; with
    /// <paramref name="captureOrder2"/> the orders are rebuilt and the first iteration's tables kept too.
    /// </summary>
    public AtmosphereLutData BuildAtmosphere(AtmosphereFrame frame, bool captureOrder2, out double gpuSeconds)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ScatteringOrder2? order2 = null;
        if (captureOrder2)
            _ordersKey = null;
        gpuSeconds = EnsureAtmosphere(frame, captureOrder2 ? o => order2 = o : null);
        return new AtmosphereLutData(
            ReadFloats(_columnsLut, AtmosphereLuts.ColumnsFloats),
            ReadFloats(_scatterSum, AtmosphereScattering.FloatsPerChannel * 2),
            ReadFloats(_groundLut, AtmosphereLuts.GroundIrradianceFloats),
            ReadFloats(_skyViewLut, AtmosphereLuts.SkyViewFloats),
            order2);
    }

    /// <summary>
    /// Forces the next <see cref="BuildAtmosphere"/> or render to rebuild every LUT, so a determinism check really
    /// builds twice.
    /// </summary>
    public void ForgetAtmosphere() => (_tablesModel, _ordersKey, _skyFrame) = (null, null, null);

    /// <summary>
    /// The LUT sky against the reference (<c>--atmosphere-check</c>): for each of <paramref name="directions"/>
    /// from the camera of <paramref name="frame"/> and each wavelength, the LUT's radiance and
    /// <paramref name="chunks"/> × <paramref name="pathsPerThread"/> reference paths, summed per chunk, in
    /// dispatches of <paramref name="pathsPerDispatch"/> paths per thread (far below the TDR).
    /// </summary>
    public SkyCheckRaw RunSkyCheck(AtmosphereFrame frame, IReadOnlyList<Vector3> directions, int chunks, int pathsPerThread, int pathsPerDispatch)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(directions);
        if (directions.Count == 0 || chunks <= 0 || pathsPerThread <= 0 || pathsPerDispatch <= 0)
            throw new ArgumentException("A sky check needs directions, chunks and paths.");
        EnsureAtmosphere(frame);
        _checkPipeline ??= CompilePipeline(_rootSignature, ShaderSource.Load(ShaderFile, ReferenceDefine), "CSSkyCheck", ShaderFile);

        var packed = new float[directions.Count * 4];
        for (int i = 0; i < directions.Count; i++)
        {
            Vector3 d = Vector3.Normalize(directions[i]);
            (packed[i * 4], packed[i * 4 + 1], packed[i * 4 + 2]) = (d.X, d.Y, d.Z);
        }
        int items = directions.Count * AtmosphereLuts.Wavelengths * chunks;
        try
        {
            _checkDirections = CreateDefaultBuffer<float>(packed);
            _checkSums = CreateUavBuffer((ulong)items * 4 * sizeof(float));
            double seconds = 0.0;
            int dispatches = 0;
            for (int first = 0; first < pathsPerThread; first += pathsPerDispatch)
            {
                Write(_checkConstants, new CheckConstants
                {
                    Directions = (uint)directions.Count,
                    Chunks = (uint)chunks,
                    Paths = (uint)Math.Min(pathsPerDispatch, pathsPerThread - first),
                    FirstPath = (uint)first,
                });
                seconds += Timed(_checkPipeline, _rootSignature, list =>
                {
                    BindIntegrator(list);
                    list.Dispatch((uint)((items + LutGroupSize - 1) / LutGroupSize), 1, 1);
                });
                dispatches++;
            }
            return new SkyCheckRaw(ReadFloats(_checkSums, items * 4), seconds, dispatches);
        }
        finally
        {
            _checkSums?.Dispose();
            _checkDirections?.Dispose();
            (_checkSums, _checkDirections) = (null, null);
        }
    }

    private void DisposeAtmosphere()
    {
        _checkSums?.Dispose();
        _checkDirections?.Dispose();
        _atmSun?.Dispose();
        _atmCoefficients?.Dispose();
        _skyViewLut?.Dispose();
        _groundLut?.Dispose();
        _scatterSum?.Dispose();
        _columnsLut?.Dispose();
        _dummySrv?.Dispose();
        _checkConstants?.Dispose();
        _buildConstants?.Dispose();
        _checkPipeline?.Dispose();
        _skyViewPipeline?.Dispose();
        _groundPipeline?.Dispose();
        _multiplePipeline?.Dispose();
        _indirectPipeline?.Dispose();
        _densityPipeline?.Dispose();
        _singlePipeline?.Dispose();
        _directPipeline?.Dispose();
        _columnsPipeline?.Dispose();
        _lutRootSignature?.Dispose();
    }
}
