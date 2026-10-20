using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RayTracer.Gpu;
using Vista.Atmosphere;
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
/// The D3D12 host of the offline integrator (vista-plan §10 M0, M1.3): <c>Shaders/VistaIntegrator.hlsl</c> on DXR
/// 1.1 inline ray tracing. It uploads the <see cref="TerrainMesh"/> into default-heap buffers, builds a BLAS
/// (32-bit indices) and a one-instance TLAS, builds the atmosphere LUTs a scene needs
/// (<c>D3D12VistaRenderer.Atmosphere.cs</c>), and runs the dispatches a <see cref="BatchPlanner"/> plans —
/// <c>Next</c> → dispatch → <c>Complete</c> with the GPU time from timestamp queries — then reads the float sums
/// back and converts them with <see cref="IntegratorReadback"/>.
///
/// <para>A render covers <see cref="RenderSettings.Rendered"/>: the whole frame, or a loupe window of it
/// (vista-plan §7.4). Buffers and dispatches are window-sized; the shader keys each pixel's camera ray and
/// random numbers by its place in the full frame, so a window is exactly a crop of the full render.</para>
///
/// <para><see cref="AtmosphereMode.Reference"/> runs the same shader compiled with <c>ATMOSPHERE_REFERENCE</c>
/// (compiled on first use): the air path-traced as a medium instead of read from the LUTs.</para>
///
/// <para>Following the engine's convention (vista-plan §8.1), the D3D12 scaffolding — device creation, the
/// RT-tier check, command objects, the fence wait, buffer helpers, BLAS/TLAS building — is copied from the
/// <c>PhaseNRenderer</c> hosts rather than shared; only <see cref="ShaderCompiler"/> is reused from
/// RayTracer.Gpu. The device, pipelines, acceleration structure and LUTs stay alive between renders, so a
/// re-render only rebuilds what changed.</para>
/// </summary>
internal sealed partial class D3D12VistaRenderer : IVistaRenderer, IDisposable
{
    private const string ShaderFile = "VistaIntegrator.hlsl";
    private const string ReferenceDefine = "ATMOSPHERE_REFERENCE";
    private const string TerrainShadowsDefine = "REFERENCE_TERRAIN_SHADOWS";
    private const int GroupSize = 8;
    private const int ConstantBufferBytes = 256;

    private const uint AovAlbedo = 1u;
    private const uint AovNormal = 2u;
    private const uint AovFirstHit = 4u;
    private const uint AovVariance = 8u;

    // Root parameter slots of the integrator's root signature.
    private const int SlotTlas = 0, SlotPositions = 1, SlotNormals = 2, SlotIndices = 3, SlotCie = 4, SlotReflectance = 5, SlotAlbedo = 6;
    private const int SlotAtmCoefficients = 7, SlotAtmSun = 8, SlotColumns = 9, SlotSkyView = 10, SlotCheckDirections = 11;
    private const int SlotXyz = 12, SlotLuma = 13, SlotAlbedoSum = 14, SlotNormalSum = 15, SlotFirstHit = 16, SlotRayCounter = 17, SlotCheckSums = 18;
    private const int SlotConstants = 19, SlotAtmosphereConstants = 20, SlotCheckConstants = 21, SlotScatterSum = 22;
    private const int SlotSkyDistribution = 23, SlotSkyLuminance = 24;

    private readonly AutoResetEvent _fenceEvent = new(false);
    private readonly SortedDictionary<string, double> _initSeconds = new(StringComparer.Ordinal);

    private IDXGIFactory4 _factory = null!;
    private IDXGIAdapter3? _adapter;
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
    private ID3D12PipelineState? _referencePipeline;
    private ID3D12PipelineState? _shadowedPipeline;
    private ID3D12QueryHeap _timestamps = null!;
    private ID3D12Resource _timestampReadback = null!;
    private ID3D12Resource _constants = null!;
    private ID3D12Resource _atmosphereConstants = null!;
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
    private long _blasBytes;

    // Surface tables: rebuilt per render, a few kilobytes.
    private ID3D12Resource? _cie;
    private ID3D12Resource? _reflectance;
    private ID3D12Resource? _albedo;

    // Image buffers: kept while the size and the requested outputs stay the same.
    private (int Width, int Height, uint Mask) _imageKey;
    private ID3D12Resource? _xyzSum;
    private ID3D12Resource? _lumaStats;
    private ID3D12Resource? _albedoSum;
    private ID3D12Resource? _normalSum;
    private ID3D12Resource? _firstHit;

    /// <summary>Creates the device, the pipelines and the timing queries. Throws when no DXR 1.1 GPU is present.</summary>
    public D3D12VistaRenderer()
    {
        CheckConstantLayouts();
        var stopwatch = Stopwatch.StartNew();
        bool debug = EnableDebugLayer();
        CreateDeviceAndQueue(debug);
        VerifyRaytracingSupport();
        CreateCommandObjects();
        _initSeconds["deviceInit"] = Lap(stopwatch);
        CreatePipeline();
        CreateAtmospherePipelines();
        CreateSkyLightPipelines();
        CreateFixedResources();
        _initSeconds["shaderCompile"] = Lap(stopwatch);
    }

    /// <inheritdoc />
    public string DeviceName { get; private set; } = "unknown";

    // ── Constant buffer layouts (match the shaders' cbuffers, 16-byte rows) ────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct IntegratorConstants
    {
        public float CamPosX, CamPosY, CamPosZ, TanHalfFovY;
        public float RightX, RightY, RightZ, Aspect;
        public float UpX, UpY, UpZ, DeterministicCorrection;
        public float ForwardX, ForwardY, ForwardZ, Pad0;
        public uint SunUp, MaxBounces, AovMask, TerrainMaterial;
        public uint Width, Height, RowStart, RowCount;            // the rendered window; rows within it
        public uint FirstSample, SampleCount, Pad1, Pad2;
        public uint WindowX, WindowY, FrameWidth, FrameHeight;    // the window's place in the full frame
        public uint LightSampling, SkySampling, RandomStream, RrStartBounce;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AtmosphereConstants
    {
        public float SeaRadius, GroundAltitude, TopAltitude, GroundAlbedo;
        public float RayleighScaleHeight, AerosolScaleHeight, OzonePeak, OzoneHalfWidth;
        public float ToSunX, ToSunY, ToSunZ, MieG;
        public float CameraAltitude, TerrainLowAltitude, TerrainHighAltitude, SunOneMinusCos;
    }

    /// <summary>
    /// A cbuffer struct larger than its 256-byte upload buffer would silently lose its last fields on the GPU (an
    /// engine finding), so the layouts are checked once, loudly.
    /// </summary>
    private static void CheckConstantLayouts()
    {
        foreach ((string name, int size) in new[]
        {
            (nameof(IntegratorConstants), Unsafe.SizeOf<IntegratorConstants>()),
            (nameof(AtmosphereConstants), Unsafe.SizeOf<AtmosphereConstants>()),
            (nameof(BuildConstants), Unsafe.SizeOf<BuildConstants>()),
            (nameof(CheckConstants), Unsafe.SizeOf<CheckConstants>()),
            (nameof(ErosionConstants), Unsafe.SizeOf<ErosionConstants>()),
        })
        {
            if (size > ConstantBufferBytes || size % 16 != 0)
                throw new InvalidOperationException($"{name} is {size} bytes; constant buffers here hold {ConstantBufferBytes} in 16-byte rows.");
        }
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
        if (settings.Atmosphere is not (AtmosphereMode.Luts or AtmosphereMode.Reference or AtmosphereMode.ReferenceShadowed))
            throw new ArgumentException($"Unknown atmosphere mode {settings.Atmosphere}.", nameof(settings));
        if (settings.LightSampling is not (LightSampling.Mis or LightSampling.SunOnly))
            throw new ArgumentException($"Unknown light sampling {settings.LightSampling}.", nameof(settings));
        if (settings.RussianRouletteStart is < 0 or > RenderSettings.MaxRussianRouletteStart)
            throw new ArgumentException($"Russian roulette must start between bounce 0 and {RenderSettings.MaxRussianRouletteStart}.", nameof(settings));

        var stages = new SortedDictionary<string, double>(_initSeconds, StringComparer.Ordinal);
        _initSeconds.Clear();
        var stopwatch = Stopwatch.StartNew();
        ID3D12PipelineState pipeline = settings.Atmosphere switch
        {
            AtmosphereMode.Reference => ReferencePipeline(stages, shadows: false),
            AtmosphereMode.ReferenceShadowed => ReferencePipeline(stages, shadows: true),
            _ => _pipeline,
        };
        stopwatch.Restart();

        if (!ReferenceEquals(scene.Mesh, _mesh))
        {
            UploadMesh(scene.Mesh);
            stages["meshUpload"] = Lap(stopwatch);
            BuildAccelerationStructures(scene.Mesh);
            stages["accelBuild"] = Lap(stopwatch);
            _mesh = scene.Mesh;
        }

        AtmosphereFrame frame = scene.AirFrame();
        double lutGpuSeconds = EnsureAtmosphere(frame);
        if (lutGpuSeconds > 0.0)
        {
            stages["atmosphereLuts"] = Lap(stopwatch);
            stages["atmosphereLutsGpu"] = lutGpuSeconds;
        }

        IntegratorTables tables = IntegratorTables.Build(scene.Materials);
        UploadTables(tables);
        uint mask = MaskFor(aovs);
        EnsureImageBuffers(window.Width, window.Height, mask);
        ResetRayCounter();
        stages["setup"] = Lap(stopwatch);

        // The sky as a light (M1.5): only the LUT air has a sky view to sample; the reference keeps M1.4's estimator.
        bool skySampling = false;
        if (settings.LightSampling == LightSampling.Mis && settings.Atmosphere == AtmosphereMode.Luts)
        {
            double skyGpuSeconds = EnsureSkyLight(frame);
            if (skyGpuSeconds > 0.0)
            {
                stages["skyDistribution"] = Lap(stopwatch);
                stages["skyDistributionGpu"] = skyGpuSeconds;
            }
            skySampling = _skyLight!.Distribution.HasLight;
        }

        IntegratorConstants constants = BaseConstants(scene, settings, frame, tables, mask, skySampling);
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
            double seconds = Dispatch(pipeline, constants, window.Width, batch.RowCount);
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
            AccelerationStructureBytes = _blasBytes,
            VideoMemoryBytes = VideoMemoryUsed,
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

    private static IntegratorConstants BaseConstants(VistaScene scene, RenderSettings settings, AtmosphereFrame frame, IntegratorTables tables, uint mask,
        bool skySampling)
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
            SunUp = frame.SunReachesScene ? 1u : 0u,
            MaxBounces = (uint)settings.MaxBounces,
            AovMask = mask,
            TerrainMaterial = (uint)MaterialCatalog.Terrain.Id,
            Width = (uint)settings.Rendered.Width,
            Height = (uint)settings.Rendered.Height,
            WindowX = (uint)settings.Rendered.X,
            WindowY = (uint)settings.Rendered.Y,
            FrameWidth = (uint)settings.Width,
            FrameHeight = (uint)settings.Height,
            LightSampling = settings.LightSampling == LightSampling.Mis && settings.Atmosphere == AtmosphereMode.Luts ? 1u : 0u,
            SkySampling = skySampling ? 1u : 0u,
            RandomStream = settings.RandomStream,
            RrStartBounce = (uint)settings.RussianRouletteStart,
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
        if (chosen.CheckInterfaceSupport(typeof(IDXGIDevice).GUID, out long umd).Success)
            DriverVersion = $"{(umd >> 48) & 0xFFFF}.{(umd >> 32) & 0xFFFF}.{(umd >> 16) & 0xFFFF}.{umd & 0xFFFF}";
        _adapter = chosen.QueryInterfaceOrNull<IDXGIAdapter3>();
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

    private static RootParameter1 Srv(uint register) =>
        new(RootParameterType.ShaderResourceView, new RootDescriptor1(register, 0), ShaderVisibility.All);

    private static RootParameter1 Uav(uint register) =>
        new(RootParameterType.UnorderedAccessView, new RootDescriptor1(register, 0), ShaderVisibility.All);

    private static RootParameter1 Cbv(uint register) =>
        new(RootParameterType.ConstantBufferView, new RootDescriptor1(register, 0), ShaderVisibility.All);

    private void CreatePipeline()
    {
        var rootParameters = new[]
        {
            Srv(0), Srv(1), Srv(2), Srv(3), Srv(4), Srv(5), Srv(6),   // 0–6: TLAS, mesh, surface tables
            Srv(8), Srv(9), Srv(10), Srv(17), Srv(14),                // 7–11: atmosphere tables, columns, sky view, check directions
            Uav(0), Uav(1), Uav(2), Uav(3), Uav(4), Uav(5), Uav(6),   // 12–18: sums, first hit, ray counter, check sums
            Cbv(0), Cbv(1), Cbv(2),                                   // 19–21: integrator, atmosphere, check constants
            Srv(13),                                                  // 22: the summed scattering orders (aerial perspective)
            Srv(18), Uav(7),                                          // 23–24: the sky distribution, its measurement (M1.5)
        };
        _rootSignature = _device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.None, rootParameters));
        _pipeline = CompilePipeline(_rootSignature, ShaderSource.Load(ShaderFile), "CSMain", ShaderFile);
    }

    private ID3D12PipelineState CompilePipeline(ID3D12RootSignature rootSignature, string source, string entry, string name)
    {
        byte[] bytecode = ShaderCompiler.CompileCompute(source, entry, name);
        return _device.CreateComputePipelineState(new ComputePipelineStateDescription
        {
            RootSignature = rootSignature,
            ComputeShader = bytecode,
        });
    }

    /// <summary>The reference-atmosphere integrator, with or without the terrain's shadow in the air, compiled the first time it is asked for.</summary>
    private ID3D12PipelineState ReferencePipeline(SortedDictionary<string, double> stages, bool shadows)
    {
        ref ID3D12PipelineState? pipeline = ref shadows ? ref _shadowedPipeline : ref _referencePipeline;
        if (pipeline is null)
        {
            var stopwatch = Stopwatch.StartNew();
            string source = shadows ? ShaderSource.Load(ShaderFile, ReferenceDefine, TerrainShadowsDefine) : ShaderSource.Load(ShaderFile, ReferenceDefine);
            pipeline = CompilePipeline(_rootSignature, source, "CSMain", ShaderFile);
            stages["referenceShaderCompile"] = stopwatch.Elapsed.TotalSeconds;
        }
        return pipeline;
    }

    private void CreateFixedResources()
    {
        _timestamps = _device.CreateQueryHeap<ID3D12QueryHeap>(new QueryHeapDescription(QueryHeapType.Timestamp, 2));
        _timestampReadback = CreateBuffer(HeapType.Readback, 2 * sizeof(ulong), ResourceStates.CopyDest);
        _constants = CreateBuffer(HeapType.Upload, ConstantBufferBytes, ResourceStates.GenericRead);
        _atmosphereConstants = CreateBuffer(HeapType.Upload, ConstantBufferBytes, ResourceStates.GenericRead);
        _rayCounter = CreateUavBuffer(2 * sizeof(uint));
        _rayCounterZero = CreateUploadBuffer<uint>([0u, 0u]);
        _rayCounterReadback = CreateBuffer(HeapType.Readback, 2 * sizeof(uint), ResourceStates.CopyDest);
        _dummyUav = CreateUavBuffer(16);
        CreateAtmosphereFixedResources();
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
        _blasBytes = (long)blasInfo.ResultDataMaxSizeInBytes;

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
    private double Dispatch(ID3D12PipelineState pipeline, IntegratorConstants constants, int width, int rows)
    {
        Write(_constants, constants);
        return Timed(pipeline, _rootSignature, list =>
        {
            BindIntegrator(list);
            list.Dispatch((uint)((width + GroupSize - 1) / GroupSize), (uint)((rows + GroupSize - 1) / GroupSize), 1);
        });
    }

    /// <summary>Binds every root parameter of the integrator's root signature (unused ones to dummies).</summary>
    private void BindIntegrator(ID3D12GraphicsCommandList4 list)
    {
        list.SetComputeRootShaderResourceView(SlotTlas, (_tlas ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotPositions, (_positions ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotNormals, (_normals ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotIndices, (_indices ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotCie, (_cie ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotReflectance, (_reflectance ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotAlbedo, (_albedo ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotAtmCoefficients, _atmCoefficients!.GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotAtmSun, _atmSun!.GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotColumns, _columnsLut.GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotSkyView, _skyViewLut.GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotCheckDirections, (_checkDirections ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootUnorderedAccessView(SlotXyz, (_xyzSum ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootUnorderedAccessView(SlotLuma, (_lumaStats ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootUnorderedAccessView(SlotAlbedoSum, (_albedoSum ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootUnorderedAccessView(SlotNormalSum, (_normalSum ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootUnorderedAccessView(SlotFirstHit, (_firstHit ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootUnorderedAccessView(SlotRayCounter, _rayCounter.GPUVirtualAddress);
        list.SetComputeRootUnorderedAccessView(SlotCheckSums, (_checkSums ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootConstantBufferView(SlotConstants, _constants.GPUVirtualAddress);
        list.SetComputeRootConstantBufferView(SlotAtmosphereConstants, _atmosphereConstants.GPUVirtualAddress);
        list.SetComputeRootConstantBufferView(SlotCheckConstants, _checkConstants.GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotScatterSum, _scatterSum.GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotSkyDistribution, (_skyDistribution ?? _dummySrv).GPUVirtualAddress);
        list.SetComputeRootUnorderedAccessView(SlotSkyLuminance, (_skyLuminance ?? _dummyUav).GPUVirtualAddress);
    }

    /// <summary>Records <paramref name="record"/> between two timestamps, runs it, and returns its GPU time in seconds.</summary>
    private double Timed(ID3D12PipelineState pipeline, ID3D12RootSignature rootSignature, Action<ID3D12GraphicsCommandList4> record)
    {
        _allocator.Reset();
        _commandList.Reset(_allocator, pipeline);
        _commandList.SetComputeRootSignature(rootSignature);
        _commandList.EndQuery(_timestamps, QueryType.Timestamp, 0);
        record(_commandList);
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

    private static void Write<T>(ID3D12Resource upload, T value) where T : unmanaged
    {
        Span<T> mapped = upload.Map<T>(0, 1);
        mapped[0] = value;
        upload.Unmap(0);
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
    /// back to COMMON between them and are promoted implicitly to UAV, shader resource, copy source or copy
    /// destination: no explicit barriers needed.
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
        _cie?.Dispose();
        (_albedo, _reflectance, _cie) = (null, null, null);
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
        DisposeSkyLight();
        DisposeAtmosphere();
        DisposeErosion();
        _dummyUav?.Dispose();
        _rayCounterReadback?.Dispose();
        _rayCounterZero?.Dispose();
        _rayCounter?.Dispose();
        _atmosphereConstants?.Dispose();
        _constants?.Dispose();
        _timestampReadback?.Dispose();
        _timestamps?.Dispose();
        _shadowedPipeline?.Dispose();
        _referencePipeline?.Dispose();
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
        _adapter?.Dispose();
        _factory?.Dispose();
    }
}
