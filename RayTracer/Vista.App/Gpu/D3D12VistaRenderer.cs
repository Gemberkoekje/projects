using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RayTracer.Gpu;
using Vista.Rendering;
using Vista.Scene;
using Vista.World;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace Vista.App.Gpu;

/// <summary>
/// The D3D12 host of offline integrator v0 (vista-plan §10 M0): <c>Shaders/VistaIntegrator.hlsl</c> on DXR 1.1
/// inline ray tracing. It uploads the <see cref="TerrainMesh"/> into default-heap buffers, builds a BLAS
/// (32-bit indices) and a one-instance TLAS, and runs the dispatches a <see cref="BatchPlanner"/> plans —
/// <c>Next</c> → dispatch → <c>Complete</c> with the GPU time from timestamp queries — then reads the float
/// sums back and converts them with <see cref="IntegratorReadback"/>.
///
/// <para>A render covers <see cref="RenderSettings.Rendered"/>: the whole frame, or a loupe window of it
/// (vista-plan §7.4). Buffers and dispatches are window-sized; the shader keys each pixel's camera ray and
/// random numbers by its place in the full frame, so a window is exactly a crop of the full render.</para>
///
/// <para>Following the engine's convention (vista-plan §8.1), the D3D12 scaffolding — device creation, the
/// RT-tier check, command objects, the fence wait, buffer helpers, BLAS/TLAS building — is copied from the
/// <c>PhaseNRenderer</c> hosts rather than shared; only <see cref="ShaderCompiler"/> is reused from
/// RayTracer.Gpu. The device, pipeline and acceleration structure stay alive between renders of the same
/// mesh, so a re-render (the determinism checks, a watch mode later) only re-runs the dispatches.</para>
/// </summary>
internal sealed class D3D12VistaRenderer : IVistaRenderer, IDisposable
{
    private const string ShaderFile = "VistaIntegrator.hlsl";
    private const int GroupSize = 8;
    private const int ConstantBufferBytes = 256;

    private const uint AovAlbedo = 1u;
    private const uint AovNormal = 2u;
    private const uint AovFirstHit = 4u;
    private const uint AovVariance = 8u;

    private readonly AutoResetEvent _fenceEvent = new(false);
    private readonly SortedDictionary<string, double> _initSeconds = new(StringComparer.Ordinal);

    private IDXGIFactory4 _factory = null!;
    private ID3D12Device5 _device = null!;
    private ID3D12CommandQueue _queue = null!;
    private ID3D12CommandAllocator _allocator = null!;
    private ID3D12GraphicsCommandList4 _commandList = null!;
    private ID3D12Fence _fence = null!;
    private ulong _fenceValue;
    private ulong _timestampFrequency;
#if DEBUG
    private ID3D12InfoQueue? _infoQueue;
#endif

    private ID3D12RootSignature _rootSignature = null!;
    private ID3D12PipelineState _pipeline = null!;
    private ID3D12QueryHeap _timestamps = null!;
    private ID3D12Resource _timestampReadback = null!;
    private ID3D12Resource _constants = null!;
    private ID3D12Resource _rayCounter = null!;
    private ID3D12Resource _rayCounterZero = null!;
    private ID3D12Resource _rayCounterReadback = null!;
    private ID3D12Resource _dummyUav = null!;

    // Scene: kept while the same mesh is rendered again.
    private TerrainMesh? _mesh;
    private ID3D12Resource? _positions;
    private ID3D12Resource? _normals;
    private ID3D12Resource? _indices;
    private ID3D12Resource? _blas;
    private ID3D12Resource? _tlas;
    private ID3D12Resource? _instance;

    // Spectral tables: rebuilt per render (they depend on the sun), a few kilobytes.
    private ID3D12Resource? _cie;
    private ID3D12Resource? _light;
    private ID3D12Resource? _reflectance;
    private ID3D12Resource? _albedo;

    // Image buffers: kept while the size and the requested outputs stay the same.
    private (int Width, int Height, uint Mask) _imageKey;
    private ID3D12Resource? _xyzSum;
    private ID3D12Resource? _lumaStats;
    private ID3D12Resource? _albedoSum;
    private ID3D12Resource? _normalSum;
    private ID3D12Resource? _firstHit;

    /// <summary>Creates the device, the pipeline and the timing queries. Throws when no DXR 1.1 GPU is present.</summary>
    public D3D12VistaRenderer()
    {
        var stopwatch = Stopwatch.StartNew();
        bool debug = EnableDebugLayer();
        CreateDeviceAndQueue(debug);
        VerifyRaytracingSupport();
        CreateCommandObjects();
        _initSeconds["deviceInit"] = Lap(stopwatch);
        CreatePipeline();
        CreateFixedResources();
        _initSeconds["shaderCompile"] = Lap(stopwatch);
    }

    /// <inheritdoc />
    public string DeviceName { get; private set; } = "unknown";

    // ── Constant buffer layout (matches VistaIntegrator.hlsl's cbuffer, 16-byte rows) ─────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct IntegratorConstants
    {
        public float CamPosX, CamPosY, CamPosZ, TanHalfFovY;
        public float RightX, RightY, RightZ, Aspect;
        public float UpX, UpY, UpZ, DeterministicCorrection;
        public float ForwardX, ForwardY, ForwardZ, SkyStrength;
        public float ToSunX, ToSunY, ToSunZ, SunOneMinusCosMax;
        public float RayleighDepth; public uint SunUp, MaxBounces, AovMask;
        public uint Width, Height, RowStart, RowCount;            // the rendered window; rows within it
        public uint FirstSample, SampleCount, TerrainMaterial, Pad0;
        public uint WindowX, WindowY, FrameWidth, FrameHeight;    // the window's place in the full frame
    }

    /// <inheritdoc />
    public RenderOutput Render(VistaScene scene, RenderSettings settings, IReadOnlySet<Aov> aovs, BatchPlanner planner)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(aovs);
        ArgumentNullException.ThrowIfNull(planner);
        PixelWindow window = settings.Rendered;
        if (!window.FitsIn(settings.Width, settings.Height))
            throw new ArgumentException($"Window {window} does not fit the {settings.Width}×{settings.Height} frame.", nameof(settings));
        if (planner.Width != window.Width || planner.Height != window.Height || planner.SamplesPerPixel != settings.SamplesPerPixel)
            throw new ArgumentException("The planner was made for a different image.", nameof(planner));
        if (planner.Dispatches != 0)
            throw new ArgumentException("The planner has already issued dispatches.", nameof(planner));
        if (scene.Materials.Count == 0)
            throw new ArgumentException("The scene has no materials.", nameof(scene));

        var stages = new SortedDictionary<string, double>(_initSeconds, StringComparer.Ordinal);
        _initSeconds.Clear();
        var stopwatch = Stopwatch.StartNew();

        if (!ReferenceEquals(scene.Mesh, _mesh))
        {
            UploadMesh(scene.Mesh);
            stages["meshUpload"] = Lap(stopwatch);
            BuildAccelerationStructures(scene.Mesh);
            stages["accelBuild"] = Lap(stopwatch);
            _mesh = scene.Mesh;
        }

        SkyLight light = SkyLight.For(scene.Sun);
        IntegratorTables tables = IntegratorTables.Build(scene.Materials, light);
        UploadTables(tables);
        uint mask = MaskFor(aovs);
        EnsureImageBuffers(window.Width, window.Height, mask);
        ResetRayCounter();
        stages["setup"] = Lap(stopwatch);

        IntegratorConstants constants = BaseConstants(scene, settings, light, tables, mask);
        double traceSeconds = 0.0;
        double longest = 0.0;
        double maxCostNs = 0.0;
        while (planner.HasNext)
        {
            SampleBatch batch = planner.Next();
            constants.RowStart = (uint)batch.RowStart;
            constants.RowCount = (uint)batch.RowCount;
            constants.FirstSample = (uint)batch.FirstSample;
            constants.SampleCount = (uint)batch.SampleCount;
            double seconds = Dispatch(constants, window.Width, batch.RowCount);
            planner.Complete(batch, TimeSpan.FromSeconds(seconds));
            traceSeconds += seconds;
            longest = Math.Max(longest, seconds);
            maxCostNs = Math.Max(maxCostNs, seconds * 1e9 / batch.PixelSamples(window.Width));
        }
        stages["trace"] = Lap(stopwatch);

        int pixels = window.Pixels;
        float[] xyz = IntegratorReadback.MeanXyz(ReadFloats(_xyzSum!, pixels * IntegratorReadback.XyzSumStride), pixels, settings.SamplesPerPixel);
        var buffers = new Dictionary<Aov, float[]>();
        if (aovs.Contains(Aov.Albedo))
            buffers[Aov.Albedo] = IntegratorReadback.MeanAlbedo(ReadFloats(_albedoSum!, pixels * IntegratorReadback.VectorSumStride), pixels, settings.SamplesPerPixel);
        if (aovs.Contains(Aov.Normal))
            buffers[Aov.Normal] = IntegratorReadback.MeanNormal(ReadFloats(_normalSum!, pixels * IntegratorReadback.VectorSumStride), pixels);
        if (aovs.Contains(Aov.Depth) || aovs.Contains(Aov.MaterialId))
        {
            float[] firstHit = ReadFloats(_firstHit!, pixels * IntegratorReadback.FirstHitStride);
            if (aovs.Contains(Aov.Depth))
                buffers[Aov.Depth] = IntegratorReadback.Depth(firstHit, pixels);
            if (aovs.Contains(Aov.MaterialId))
                buffers[Aov.MaterialId] = IntegratorReadback.MaterialId(firstHit, pixels);
        }
        if (aovs.Contains(Aov.Variance))
            buffers[Aov.Variance] = IntegratorReadback.Variance(ReadFloats(_lumaStats!, pixels * IntegratorReadback.LumaStatsStride), pixels, settings.SamplesPerPixel);
        ulong rays = ReadRayCounter();
        stages["readback"] = Lap(stopwatch);

        var stats = new RenderStats
        {
            RaysTraced = rays,
            PixelSamples = planner.PixelSamplesCompleted,
            Dispatches = planner.Dispatches,
            TraceSeconds = traceSeconds,
            LongestDispatchSeconds = longest,
            MaxCostNsPerPixelSample = maxCostNs,
            StageSeconds = stages,
        };
        return new RenderOutput(window.Width, window.Height, xyz, buffers, stats);
    }

    private static uint MaskFor(IReadOnlySet<Aov> aovs)
    {
        uint mask = 0u;
        if (aovs.Contains(Aov.Albedo))
            mask |= AovAlbedo;
        if (aovs.Contains(Aov.Normal))
            mask |= AovNormal;
        if (aovs.Contains(Aov.Depth) || aovs.Contains(Aov.MaterialId))
            mask |= AovFirstHit;
        if (aovs.Contains(Aov.Variance))
            mask |= AovVariance;
        return mask;
    }

    private static IntegratorConstants BaseConstants(VistaScene scene, RenderSettings settings, SkyLight light, IntegratorTables tables, uint mask)
    {
        CameraSpec camera = scene.Camera;
        Vector3D position = new(camera.Position.X, camera.Position.Y, camera.Position.Z);
        Vector3D right = camera.Right();
        Vector3D up = camera.Up();
        Vector3D forward = camera.Forward();
        return new IntegratorConstants
        {
            CamPosX = (float)position.X, CamPosY = (float)position.Y, CamPosZ = (float)position.Z,
            TanHalfFovY = (float)Math.Tan(camera.VerticalFovRadians() * 0.5),
            RightX = (float)right.X, RightY = (float)right.Y, RightZ = (float)right.Z,
            Aspect = (float)settings.Width / settings.Height,
            UpX = (float)up.X, UpY = (float)up.Y, UpZ = (float)up.Z,
            DeterministicCorrection = tables.DeterministicCorrection,
            ForwardX = (float)forward.X, ForwardY = (float)forward.Y, ForwardZ = (float)forward.Z,
            SkyStrength = light.SkyStrength,
            ToSunX = light.ToSun.X, ToSunY = light.ToSun.Y, ToSunZ = light.ToSun.Z,
            SunOneMinusCosMax = (float)light.SunOneMinusCosMax,
            RayleighDepth = light.RayleighDepth,
            SunUp = light.SunUp ? 1u : 0u,
            MaxBounces = (uint)settings.MaxBounces,
            AovMask = mask,
            Width = (uint)settings.Rendered.Width,
            Height = (uint)settings.Rendered.Height,
            TerrainMaterial = (uint)MaterialCatalog.Terrain.Id,
            WindowX = (uint)settings.Rendered.X,
            WindowY = (uint)settings.Rendered.Y,
            FrameWidth = (uint)settings.Width,
            FrameHeight = (uint)settings.Height,
        };
    }

    // ── Device ──────────────────────────────────────────────────────────────────────────────────────

    private static bool EnableDebugLayer()
    {
#if DEBUG
        // Debug builds run the D3D12 debug layer with GPU-based validation; DrainDebugMessages prints what it finds.
        if (D3D12GetDebugInterface<ID3D12Debug1>(out var debugController).Success && debugController is not null)
        {
            debugController.EnableDebugLayer();
            debugController.SetEnableGPUBasedValidation(true);
            debugController.Dispose();
            return true;
        }
#endif
        return false;
    }

    [Conditional("DEBUG")]
    private void DrainDebugMessages()
    {
#if DEBUG
        if (_infoQueue is null)
            return;
        for (ulong i = 0; i < _infoQueue.NumStoredMessages; i++)
        {
            Vortice.Direct3D12.Debug.Message message = _infoQueue.GetMessage(i);
            if (message.Severity <= MessageSeverity.Warning)
                Console.Error.WriteLine($"D3D12 {message.Severity}: {message.Description}");
        }
        _infoQueue.ClearStoredMessages();
#endif
    }

    private void CreateDeviceAndQueue(bool debug)
    {
        _factory = CreateDXGIFactory2<IDXGIFactory4>(debug);

        IDXGIAdapter1? chosen = null;
        if (_factory.QueryInterfaceOrNull<IDXGIFactory6>() is { } factory6)
        {
            for (uint i = 0;
                 factory6.EnumAdapterByGpuPreference(i, GpuPreference.HighPerformance, out IDXGIAdapter1? adapter).Success;
                 i++)
            {
                if (adapter is null) break;
                if ((adapter.Description1.Flags & AdapterFlags.Software) != 0)
                {
                    adapter.Dispose();
                    continue;
                }
                if (D3D12CreateDevice(adapter, FeatureLevel.Level_12_0, out ID3D12Device5? device).Success)
                {
                    chosen = adapter;
                    _device = device!;
                    break;
                }
                adapter.Dispose();
            }
            factory6.Dispose();
        }

        if (_device is null || chosen is null)
            throw new PlatformNotSupportedException("No Direct3D 12 capable GPU was found.");

        DeviceName = chosen.Description1.Description;
        chosen.Dispose();

        _queue = _device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
        _fence = _device.CreateFence(0, FenceFlags.None);
        _queue.GetTimestampFrequency(out _timestampFrequency).CheckError();
#if DEBUG
        _infoQueue = debug ? _device.QueryInterfaceOrNull<ID3D12InfoQueue>() : null;
#endif
    }

    private void VerifyRaytracingSupport()
    {
        FeatureDataD3D12Options5 options5 =
            _device.CheckFeatureSupport<FeatureDataD3D12Options5>(Vortice.Direct3D12.Feature.Options5);
        if (options5.RaytracingTier < RaytracingTier.Tier1_1)
        {
            throw new PlatformNotSupportedException(
                $"This GPU reports raytracing tier '{options5.RaytracingTier}'. " +
                "DXR 1.1 inline ray tracing (Tier 1.1) is required.");
        }
    }

    private void CreateCommandObjects()
    {
        _allocator = _device.CreateCommandAllocator(CommandListType.Direct);
        _commandList = _device.CreateCommandList<ID3D12GraphicsCommandList4>(0, CommandListType.Direct, _allocator, null);
        _commandList.Close();
    }

    private void CreatePipeline()
    {
        static RootParameter1 Srv(uint register) =>
            new(RootParameterType.ShaderResourceView, new RootDescriptor1(register, 0), ShaderVisibility.All);
        static RootParameter1 Uav(uint register) =>
            new(RootParameterType.UnorderedAccessView, new RootDescriptor1(register, 0), ShaderVisibility.All);

        var rootParameters = new[]
        {
            Srv(0), Srv(1), Srv(2), Srv(3), Srv(4), Srv(5), Srv(6), Srv(7), // 0–7: TLAS, mesh, tables
            Uav(0), Uav(1), Uav(2), Uav(3), Uav(4), Uav(5),                 // 8–13: sums, first hit, ray counter
            new RootParameter1(RootParameterType.ConstantBufferView, new RootDescriptor1(0, 0), ShaderVisibility.All), // 14
        };
        _rootSignature = _device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.None, rootParameters));

        string path = Path.Combine(AppContext.BaseDirectory, "Shaders", ShaderFile);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Shader not found at '{path}'.");
        byte[] bytecode = ShaderCompiler.CompileCompute(File.ReadAllText(path), "CSMain", ShaderFile);
        _pipeline = _device.CreateComputePipelineState(new ComputePipelineStateDescription
        {
            RootSignature = _rootSignature,
            ComputeShader = bytecode,
        });
    }

    private void CreateFixedResources()
    {
        _timestamps = _device.CreateQueryHeap<ID3D12QueryHeap>(new QueryHeapDescription(QueryHeapType.Timestamp, 2));
        _timestampReadback = CreateBuffer(HeapType.Readback, 2 * sizeof(ulong), ResourceStates.CopyDest);
        _constants = CreateBuffer(HeapType.Upload, ConstantBufferBytes, ResourceStates.GenericRead);
        _rayCounter = CreateUavBuffer(2 * sizeof(uint));
        _rayCounterZero = CreateUploadBuffer<uint>([0u, 0u]);
        _rayCounterReadback = CreateBuffer(HeapType.Readback, 2 * sizeof(uint), ResourceStates.CopyDest);
        _dummyUav = CreateUavBuffer(16);
    }

    // ── Scene ───────────────────────────────────────────────────────────────────────────────────────

    private void UploadMesh(TerrainMesh mesh)
    {
        DisposeScene();
        _positions = CreateDefaultBuffer<float>(mesh.Positions);
        _normals = CreateDefaultBuffer<float>(mesh.Normals);
        _indices = CreateDefaultBuffer<uint>(mesh.Indices);
    }

    private void BuildAccelerationStructures(TerrainMesh mesh)
    {
        var geometry = new RaytracingGeometryDescription
        {
            Type = RaytracingGeometryType.Triangles,
            Flags = RaytracingGeometryFlags.Opaque,
            Triangles = new RaytracingGeometryTrianglesDescription
            {
                VertexBuffer = new GpuVirtualAddressAndStride(_positions!.GPUVirtualAddress, 3 * sizeof(float)),
                VertexCount = (uint)mesh.VertexCount,
                VertexFormat = Format.R32G32B32_Float,
                IndexBuffer = _indices!.GPUVirtualAddress,
                IndexCount = (uint)mesh.Indices.Length,
                IndexFormat = Format.R32_UInt,
                Transform3x4 = 0,
            },
        };
        var blasInputs = new BuildRaytracingAccelerationStructureInputs
        {
            Type = RaytracingAccelerationStructureType.BottomLevel,
            Layout = ElementsLayout.Array,
            Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace,
            DescriptorsCount = 1,
            GeometryDescriptions = [geometry],
        };
        RaytracingAccelerationStructurePrebuildInfo blasInfo = _device.GetRaytracingAccelerationStructurePrebuildInfo(blasInputs);
        using ID3D12Resource blasScratch = CreateUavBuffer(blasInfo.ScratchDataSizeInBytes);
        _blas = CreateUavBuffer(blasInfo.ResultDataMaxSizeInBytes, ResourceStates.RaytracingAccelerationStructure);

        var instance = new RaytracingInstanceDescription
        {
            Transform = new Matrix3x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0),
            InstanceMask = 0xFF,
            Flags = RaytracingInstanceFlags.None,
            AccelerationStructure = _blas.GPUVirtualAddress,
        };
        _instance = CreateUploadBuffer<RaytracingInstanceDescription>(MemoryMarshal.CreateReadOnlySpan(ref instance, 1));

        var tlasInputs = new BuildRaytracingAccelerationStructureInputs
        {
            Type = RaytracingAccelerationStructureType.TopLevel,
            Layout = ElementsLayout.Array,
            Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace,
            DescriptorsCount = 1,
            InstanceDescriptions = _instance.GPUVirtualAddress,
        };
        RaytracingAccelerationStructurePrebuildInfo tlasInfo = _device.GetRaytracingAccelerationStructurePrebuildInfo(tlasInputs);
        using ID3D12Resource tlasScratch = CreateUavBuffer(tlasInfo.ScratchDataSizeInBytes);
        _tlas = CreateUavBuffer(tlasInfo.ResultDataMaxSizeInBytes, ResourceStates.RaytracingAccelerationStructure);

        _allocator.Reset();
        _commandList.Reset(_allocator, null);
        _commandList.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription
        {
            Inputs = blasInputs,
            ScratchAccelerationStructureData = blasScratch.GPUVirtualAddress,
            DestinationAccelerationStructureData = _blas.GPUVirtualAddress,
        });
        _commandList.ResourceBarrierUnorderedAccessView(_blas);
        _commandList.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription
        {
            Inputs = tlasInputs,
            ScratchAccelerationStructureData = tlasScratch.GPUVirtualAddress,
            DestinationAccelerationStructureData = _tlas.GPUVirtualAddress,
        });
        _commandList.ResourceBarrierUnorderedAccessView(_tlas);
        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        WaitForGpu();
    }

    private void UploadTables(IntegratorTables tables)
    {
        DisposeTables();
        _cie = CreateDefaultBuffer<float>(tables.Cie);
        _light = CreateDefaultBuffer<float>(tables.Light);
        _reflectance = CreateDefaultBuffer<float>(tables.Reflectance);
        _albedo = CreateDefaultBuffer<float>(tables.Albedo);
    }

    private void EnsureImageBuffers(int width, int height, uint mask)
    {
        if (_xyzSum is not null && _imageKey == (width, height, mask))
            return;
        DisposeImage();
        ulong pixels = (ulong)width * (ulong)height;
        _xyzSum = CreateUavBuffer(pixels * IntegratorReadback.XyzSumStride * sizeof(float));
        // Outputs nobody asked for are never written (the shader checks AovMask), so a dummy binding is safe.
        _lumaStats = (mask & AovVariance) != 0 ? CreateUavBuffer(pixels * IntegratorReadback.LumaStatsStride * sizeof(float)) : null;
        _albedoSum = (mask & AovAlbedo) != 0 ? CreateUavBuffer(pixels * IntegratorReadback.VectorSumStride * sizeof(float)) : null;
        _normalSum = (mask & AovNormal) != 0 ? CreateUavBuffer(pixels * IntegratorReadback.VectorSumStride * sizeof(float)) : null;
        _firstHit = (mask & AovFirstHit) != 0 ? CreateUavBuffer(pixels * IntegratorReadback.FirstHitStride * sizeof(float)) : null;
        _imageKey = (width, height, mask);
    }

    // ── Dispatch ────────────────────────────────────────────────────────────────────────────────────

    private void ResetRayCounter()
    {
        _allocator.Reset();
        _commandList.Reset(_allocator, null);
        _commandList.CopyBufferRegion(_rayCounter, 0, _rayCounterZero, 0, 2 * sizeof(uint));
        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        WaitForGpu();
    }

    /// <summary>Runs one batch and returns its GPU time in seconds.</summary>
    private double Dispatch(IntegratorConstants constants, int width, int rows)
    {
        Span<IntegratorConstants> mapped = _constants.Map<IntegratorConstants>(0, 1);
        mapped[0] = constants;
        _constants.Unmap(0);

        _allocator.Reset();
        _commandList.Reset(_allocator, _pipeline);
        _commandList.SetComputeRootSignature(_rootSignature);
        _commandList.SetComputeRootShaderResourceView(0, _tlas!.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(1, _positions!.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(2, _normals!.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(3, _indices!.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(4, _cie!.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(5, _light!.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(6, _reflectance!.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(7, _albedo!.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(8, _xyzSum!.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(9, (_lumaStats ?? _dummyUav).GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(10, (_albedoSum ?? _dummyUav).GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(11, (_normalSum ?? _dummyUav).GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(12, (_firstHit ?? _dummyUav).GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(13, _rayCounter.GPUVirtualAddress);
        _commandList.SetComputeRootConstantBufferView(14, _constants.GPUVirtualAddress);

        _commandList.EndQuery(_timestamps, QueryType.Timestamp, 0);
        _commandList.Dispatch((uint)((width + GroupSize - 1) / GroupSize), (uint)((rows + GroupSize - 1) / GroupSize), 1);
        _commandList.EndQuery(_timestamps, QueryType.Timestamp, 1);
        _commandList.ResolveQueryData(_timestamps, QueryType.Timestamp, 0, 2, _timestampReadback, 0);
        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        WaitForGpu();

        Span<ulong> ticks = _timestampReadback.Map<ulong>(0, 2);
        ulong elapsed = ticks[1] - ticks[0];
        _timestampReadback.Unmap(0);
        return (double)elapsed / _timestampFrequency;
    }

    // ── Readback ────────────────────────────────────────────────────────────────────────────────────

    private float[] ReadFloats(ID3D12Resource buffer, int count)
    {
        ulong bytes = (ulong)count * sizeof(float);
        using ID3D12Resource readback = CreateBuffer(HeapType.Readback, bytes, ResourceStates.CopyDest);
        CopyToReadback(buffer, readback, bytes);
        var data = new float[count];
        readback.Map<float>(0, count).CopyTo(data);
        readback.Unmap(0);
        return data;
    }

    private ulong ReadRayCounter()
    {
        CopyToReadback(_rayCounter, _rayCounterReadback, 2 * sizeof(uint));
        Span<uint> words = _rayCounterReadback.Map<uint>(0, 2);
        ulong count = ((ulong)words[1] << 32) | words[0];
        _rayCounterReadback.Unmap(0);
        return count;
    }

    private void CopyToReadback(ID3D12Resource source, ID3D12Resource readback, ulong bytes)
    {
        _allocator.Reset();
        _commandList.Reset(_allocator, null);
        _commandList.CopyBufferRegion(readback, 0, source, 0, bytes);
        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        WaitForGpu();
    }

    // ── Buffer helpers ──────────────────────────────────────────────────────────────────────────────

    private ID3D12Resource CreateBuffer(HeapType heap, ulong bytes, ResourceStates state) =>
        _device.CreateCommittedResource(new HeapProperties(heap), HeapFlags.None, ResourceDescription.Buffer(bytes), state);

    /// <summary>
    /// A default-heap buffer shaders can write. Buffers are created in COMMON whatever the initial state says
    /// (except acceleration structures), and every command list here is its own submission, so buffers decay
    /// back to COMMON between them and are promoted implicitly to UAV, copy source or copy destination: no
    /// explicit barriers needed.
    /// </summary>
    private ID3D12Resource CreateUavBuffer(ulong bytes, ResourceStates state = ResourceStates.Common) =>
        _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default), HeapFlags.None,
            ResourceDescription.Buffer(bytes, ResourceFlags.AllowUnorderedAccess), state);

    private ID3D12Resource CreateUploadBuffer<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        ID3D12Resource buffer = CreateBuffer(HeapType.Upload, (ulong)(data.Length * Unsafe.SizeOf<T>()), ResourceStates.GenericRead);
        data.CopyTo(buffer.Map<T>(0, data.Length));
        buffer.Unmap(0);
        return buffer;
    }

    /// <summary>A default-heap copy of <paramref name="data"/> in the shader-resource state (also the state BLAS inputs need).</summary>
    private ID3D12Resource CreateDefaultBuffer<T>(T[] data) where T : unmanaged
    {
        ulong bytes = (ulong)data.Length * (ulong)Unsafe.SizeOf<T>();
        using ID3D12Resource staging = CreateUploadBuffer<T>(data);
        ID3D12Resource buffer = CreateBuffer(HeapType.Default, bytes, ResourceStates.Common);
        _allocator.Reset();
        _commandList.Reset(_allocator, null);
        _commandList.CopyBufferRegion(buffer, 0, staging, 0, bytes);
        _commandList.ResourceBarrierTransition(buffer, ResourceStates.CopyDest, ResourceStates.NonPixelShaderResource);
        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        WaitForGpu();
        return buffer;
    }

    private void WaitForGpu()
    {
        ulong value = ++_fenceValue;
        _queue.Signal(_fence, value).CheckError();
        if (_fence.CompletedValue < value)
        {
            _fence.SetEventOnCompletion(value, _fenceEvent).CheckError();
            _fenceEvent.WaitOne();
        }
        DrainDebugMessages();
        if (_device.DeviceRemovedReason.Failure)
        {
            throw new InvalidOperationException(
                $"The GPU device was removed ({_device.DeviceRemovedReason}). A dispatch probably exceeded the " +
                "Windows GPU timeout (TDR); lower --dispatch-ms.");
        }
    }

    private static double Lap(Stopwatch stopwatch)
    {
        double seconds = stopwatch.Elapsed.TotalSeconds;
        stopwatch.Restart();
        return seconds;
    }

    // ── Disposal ────────────────────────────────────────────────────────────────────────────────────

    private void DisposeScene()
    {
        _instance?.Dispose();
        _tlas?.Dispose();
        _blas?.Dispose();
        _indices?.Dispose();
        _normals?.Dispose();
        _positions?.Dispose();
        (_instance, _tlas, _blas, _indices, _normals, _positions, _mesh) = (null, null, null, null, null, null, null);
    }

    private void DisposeTables()
    {
        _albedo?.Dispose();
        _reflectance?.Dispose();
        _light?.Dispose();
        _cie?.Dispose();
        (_albedo, _reflectance, _light, _cie) = (null, null, null, null);
    }

    private void DisposeImage()
    {
        _firstHit?.Dispose();
        _normalSum?.Dispose();
        _albedoSum?.Dispose();
        _lumaStats?.Dispose();
        _xyzSum?.Dispose();
        (_firstHit, _normalSum, _albedoSum, _lumaStats, _xyzSum) = (null, null, null, null, null);
    }

    public void Dispose()
    {
        if (_device is not null && _queue is not null && _fence is not null)
        {
            try
            {
                WaitForGpu();
            }
            catch (InvalidOperationException)
            {
                // A removed device has nothing left to wait for; release everything anyway.
            }
        }
        DisposeImage();
        DisposeTables();
        DisposeScene();
        _dummyUav?.Dispose();
        _rayCounterReadback?.Dispose();
        _rayCounterZero?.Dispose();
        _rayCounter?.Dispose();
        _constants?.Dispose();
        _timestampReadback?.Dispose();
        _timestamps?.Dispose();
        _pipeline?.Dispose();
        _rootSignature?.Dispose();
        _commandList?.Dispose();
        _allocator?.Dispose();
        _fence?.Dispose();
        _fenceEvent.Dispose();
#if DEBUG
        _infoQueue?.Dispose();
#endif
        _queue?.Dispose();
        _device?.Dispose();
        _factory?.Dispose();
    }
}
