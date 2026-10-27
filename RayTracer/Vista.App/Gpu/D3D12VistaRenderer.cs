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
/// (<c>D3D12VistaRenderer.Atmosphere.cs</c>), measures the light on the incident meter's card from them
/// (<c>D3D12VistaRenderer.Incident.cs</c>), and runs the dispatches a <see cref="BatchPlanner"/> plans —
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
    private const string LowAirDefine = "LOW_AIR";   // the valleys' pools' code (M1.13b), compiled only into the variant that renders them
    private const int GroupSize = 8;
    private const int ConstantBufferBytes = 256;
    private const int AtmosphereConstantBytes = 512;   // the valleys' pools (M1.13b) took the atmosphere's past 256

    /// <summary>
    /// The most triangles one acceleration structure is built from (vista-plan §10 M1.9): the build is one submission, and it must
    /// stay well inside the Windows GPU timeout (33.6 M build in about a second on the 3070; a mesh of 70 M or so hung it).
    /// </summary>
    private const long MaxTriangles = 48_000_000;

    private const uint AovAlbedo = 1u;
    private const uint AovNormal = 2u;
    private const uint AovFirstHit = 4u;
    private const uint AovVariance = 8u;

    // Root parameter slots of the integrator's root signature.
    private const int SlotTlas = 0, SlotPositions = 1, SlotNormals = 2, SlotIndices = 3, SlotCie = 4, SlotReflectance = 5, SlotAlbedo = 6;
    private const int SlotAtmCoefficients = 7, SlotAtmSun = 8, SlotColumns = 9, SlotSkyView = 10, SlotCheckDirections = 11;
    private const int SlotXyz = 12, SlotLuma = 13, SlotAlbedoSum = 14, SlotNormalSum = 15, SlotFirstHit = 16, SlotRayCounter = 17, SlotCheckSums = 18;
    private const int SlotConstants = 19, SlotAtmosphereConstants = 20, SlotCheckConstants = 21, SlotScatterSum = 22;
    private const int SlotSkyDistribution = 23, SlotSkyLuminance = 24, SlotMapNormals = 25, SlotNormalMap0 = 26, SlotNormalMap1 = 27;
    private const int SlotMaterialMap = 28, SlotBedTable = 29, SlotMaterialConstants = 30, SlotLowAir = 31;

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
    private ID3D12PipelineState? _lowAirPipeline;
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
    private ID3D12Resource? _mapNormals;
    private long _blasBytes;

    // The world's normal maps (M1.9): kept while the meshes rendered share them, as every mesh of one world does.
    private IReadOnlyList<NormalMap>? _normalMaps;
    private ID3D12Resource?[] _mapBuffers = [];

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
        _initSeconds[RenderStageKeys.DeviceInit] = Lap(stopwatch);
        CreatePipeline();
        CreateAtmospherePipelines();
        CreateLowAirPipelines();
        CreateSkyLightPipelines();
        CreateIncidentPipeline();
        CreateFixedResources();
        _initSeconds[RenderStageKeys.ShaderCompile] = Lap(stopwatch);
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
        public float CamWorldX, CamWorldZ;                        // the mesh's tangent point: the frame's (0, 0, 0) in the world
        public uint MapCount, Pad3;
        public float Map0OriginX, Map0OriginZ, Map0Cell;           // the world grid's normal map (M1.9)
        public uint Map0Size;
        public float Map1OriginX, Map1OriginZ, Map1Cell;           // the 32 m far grid's
        public uint Map1Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AtmosphereConstants
    {
        public float SeaRadius, GroundAltitude, TopAltitude, GroundAlbedo;
        public float RayleighScaleHeight, AerosolScaleHeight, OzonePeak, OzoneHalfWidth;
        public float ToSunX, ToSunY, ToSunZ, MieG;
        public float CameraAltitude, TerrainLowAltitude, TerrainHighAltitude, SunOneMinusCos;
        public float ValleyHazeTop, ValleyHazeSoftness, ValleyHazeDensity, HazeScale;
        public uint LowMode, LowLevels, LowNoiseSeed, LowVolumeLayers;                   // the valleys' pools (M1.13b)
        public float Level0OriginX, Level0OriginZ, Level0Cell, Level0Size;
        public float Level1OriginX, Level1OriginZ, Level1Cell, Level1Size;
        public uint Level1Offset, Volume0Offset, Volume1Offset, VolumeRatio;
        public float SlabTop, SlabBottom, HazeMax, MistMax;
        public float BlendMetres, HazeSoftness, VolumeMargin, MistG;
        public float WispAmplitude, WispScale, WispVertical, PatchStrength;
        public float PatchScale, MistWaterLimit, MistAlbedo, MarchStep;
        public float MsA, MsB, MsC;
        public uint MsOctaves;
        public uint MarchMaxSteps;
        public float Ambient;
        public uint CeilingOffset, CeilingBlock;
        public float MarchGrowth;
        public uint BounceSteps;
        public float BounceStep, MistSoftness;
        public float MsForward, MsForwardPower, MsDiffuse, MsDiffusePower;
        public uint SkySteps;
        public float SkyGrowth, MistStep, MistBandStep;
        public float Fray, ShadowedScattering;
        public uint VolumePoolLayers;
        public float Pad8;
    }

    /// <summary>
    /// A cbuffer struct larger than its 256-byte upload buffer would silently lose its last fields on the GPU (an
    /// engine finding), so the layouts are checked once, loudly.
    /// </summary>
    private static void CheckConstantLayouts()
    {
        foreach ((string name, int size, int capacity) in new[]
        {
            (nameof(IntegratorConstants), Unsafe.SizeOf<IntegratorConstants>(), ConstantBufferBytes),
            (nameof(AtmosphereConstants), Unsafe.SizeOf<AtmosphereConstants>(), AtmosphereConstantBytes),
            (nameof(BuildConstants), Unsafe.SizeOf<BuildConstants>(), ConstantBufferBytes),
            (nameof(CheckConstants), Unsafe.SizeOf<CheckConstants>(), ConstantBufferBytes),
            (nameof(ErosionConstants), Unsafe.SizeOf<ErosionConstants>(), ConstantBufferBytes),
            (nameof(MaterialConstants), Unsafe.SizeOf<MaterialConstants>(), MaterialConstantBytes),
        })
        {
            if (size > capacity || size % 16 != 0)
                throw new InvalidOperationException($"{name} is {size} bytes; its constant buffer holds {capacity} in 16-byte rows.");
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
        if (Math.Abs(scene.Mesh.Frame.GroundRadius - scene.AirFrame().GroundRadius) > 1e-3)
            throw new ArgumentException("The mesh is bent onto another ground sphere than the scene's atmosphere: mesh it for this base altitude.", nameof(scene));
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
            _ when scene.LowAir is not null => LowAirPipeline(stages),
            _ => _pipeline,
        };
        stopwatch.Restart();

        if (!ReferenceEquals(scene.Mesh, _mesh))
        {
            DisposeScene();
            CheckFits(scene.Mesh);
            UploadMesh(scene.Mesh);
            EnsureNormalMaps(scene.Mesh.NormalMaps);
            EnsureMaterials(scene.Mesh.Materials);
            stages[RenderStageKeys.MeshUpload] = Lap(stopwatch);
            BuildAccelerationStructures(scene.Mesh);
            stages[RenderStageKeys.AccelerationBuild] = Lap(stopwatch);
            _mesh = scene.Mesh;
        }

        // Each part of the air is rebuilt only when the frame changed what it depends on (M1.8 reports which). The valleys' pools
        // (M1.13b) go up first: the atmosphere's constants carry their layout.
        EnsureLowAirCells(scene.LowAir);
        AtmosphereFrame frame = scene.AirFrame();
        WavelengthTable wavelengths = scene.Wavelengths();
        LutBuild luts = EnsureAtmosphere(frame, wavelengths);
        if (luts.Any)
        {
            stages["atmosphereLuts"] = Lap(stopwatch);
            if (luts.Tables)
                stages[RenderStageKeys.AtmosphereTables] = luts.TablesSeconds;
            if (luts.Orders)
                stages[RenderStageKeys.ScatteringOrders] = luts.OrdersSeconds;
            if (luts.SkyView)
                stages[RenderStageKeys.SkyView] = luts.SkyViewSeconds;
        }

        double volumeSeconds = EnsureSunlightVolume(scene, frame);
        if (volumeSeconds > 0.0)
        {
            stages["sunlightVolume"] = Lap(stopwatch);
            stages[RenderStageKeys.SunlightVolume] = volumeSeconds;
        }

        IntegratorTables tables = IntegratorTables.Build(scene.Materials, wavelengths);
        UploadTables(tables);
        uint mask = MaskFor(aovs);
        EnsureImageBuffers(window.Width, window.Height, mask);
        ResetRayCounter();
        stages["setup"] = Lap(stopwatch);

        // The sky as a light (M1.5): only the LUT air has a sky view to sample; the reference keeps M1.4's estimator.
        bool skySampling = false;
        if (settings.LightSampling == LightSampling.Mis && settings.Atmosphere == AtmosphereMode.Luts)
        {
            if (EnsureSkyLight(frame, out double skyGpuSeconds))
            {
                stages["skyDistribution"] = Lap(stopwatch);
                stages[RenderStageKeys.SkyDistribution] = skyGpuSeconds;
            }
            skySampling = _skyLight!.Distribution.HasLight;
        }

        IntegratorConstants constants = BaseConstants(scene, settings, frame, tables, mask, skySampling);
        Write(_materialConstants, MaterialConstantsFor(scene, settings.Diffuse, settings.Canopy, settings.Height));
        double traceSeconds = 0.0;
        double maxCostNs = 0.0;
        var pace = new DispatchPace();
        while (planner.HasNext)
        {
            SampleBatch batch = planner.Next();
            double seconds = DispatchBatch(pipeline, constants, window.Width, batch, ref pace);
            planner.Complete(batch, TimeSpan.FromSeconds(seconds));
            traceSeconds += seconds;
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
            LongestDispatchSeconds = pace.Longest,
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
        // The camera in the mesh's frame (vista-plan §10 M1.9): (0, height, 0) when the mesh was built for it.
        Vector3D position = scene.Mesh.Frame.ToFrame(camera.Position.X, camera.Position.Y, camera.Position.Z);
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
            CamWorldX = (float)scene.Mesh.Frame.TangentX,
            CamWorldZ = (float)scene.Mesh.Frame.TangentZ,
            MapCount = (uint)Math.Min(scene.Mesh.NormalMaps.Count, 2),
            Map0OriginX = Map(scene, 0).OriginX, Map0OriginZ = Map(scene, 0).OriginZ, Map0Cell = Map(scene, 0).Cell, Map0Size = Map(scene, 0).Size,
            Map1OriginX = Map(scene, 1).OriginX, Map1OriginZ = Map(scene, 1).OriginZ, Map1Cell = Map(scene, 1).Cell, Map1Size = Map(scene, 1).Size,
        };
    }

    /// <summary>Normal map <paramref name="index"/> of the scene's mesh for the constants, or zeros when it has none.</summary>
    private static (float OriginX, float OriginZ, float Cell, uint Size) Map(VistaScene scene, int index) =>
        index < scene.Mesh.NormalMaps.Count
            ? ((float)scene.Mesh.NormalMaps[index].OriginX, (float)scene.Mesh.NormalMaps[index].OriginZ, (float)scene.Mesh.NormalMaps[index].Cell,
                (uint)scene.Mesh.NormalMaps[index].Size)
            : (0f, 0f, 0f, 0u);

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
            Srv(19), Srv(20), Srv(21),                                // 25–27: the vertices' map normals, the two normal maps (M1.9)
            Srv(22), Srv(23), Cbv(3),                                 // 28–30: the material map, the bed table, the material constants (M1.12)
            Srv(24),                                                  // 31: the valleys' pools and their sunlight volumes (M1.13b); 64 of 64 words
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
            string source = shadows ? ShaderSource.Load(ShaderFile, ReferenceDefine, LowAirDefine, TerrainShadowsDefine) : ShaderSource.Load(ShaderFile, ReferenceDefine, LowAirDefine);
            pipeline = CompilePipeline(_rootSignature, source, "CSMain", ShaderFile);
            stages[RenderStageKeys.ReferenceShaderCompile] = stopwatch.Elapsed.TotalSeconds;
        }
        return pipeline;
    }

    /// <summary>
    /// The integrator with the valleys' pools (M1.13b), compiled the first time a render has them: their code costs registers, which
    /// cost a view without pools 40% of its speed (the aerial view, 7.0 against 9.9 ns a sample), so the plain one leaves it out.
    /// </summary>
    private ID3D12PipelineState LowAirPipeline(SortedDictionary<string, double> stages)
    {
        if (_lowAirPipeline is null)
        {
            var stopwatch = Stopwatch.StartNew();
            _lowAirPipeline = CompilePipeline(_rootSignature, ShaderSource.Load(ShaderFile, LowAirDefine), "CSMain", ShaderFile);
            stages[RenderStageKeys.LowAirShaderCompile] = stopwatch.Elapsed.TotalSeconds;
        }
        return _lowAirPipeline;
    }

    private void CreateFixedResources()
    {
        _timestamps = _device.CreateQueryHeap<ID3D12QueryHeap>(new QueryHeapDescription(QueryHeapType.Timestamp, 2));
        _timestampReadback = CreateBuffer(HeapType.Readback, 2 * sizeof(ulong), ResourceStates.CopyDest);
        _constants = CreateBuffer(HeapType.Upload, ConstantBufferBytes, ResourceStates.GenericRead);
        _atmosphereConstants = CreateBuffer(HeapType.Upload, AtmosphereConstantBytes, ResourceStates.GenericRead);
        _rayCounter = CreateUavBuffer(2 * sizeof(uint));
        _rayCounterZero = CreateUploadBuffer<uint>([0u, 0u]);
        _rayCounterReadback = CreateBuffer(HeapType.Readback, 2 * sizeof(uint), ResourceStates.CopyDest);
        _dummyUav = CreateUavBuffer(16);
        CreateAtmosphereFixedResources();
        CreateMaterialResources();
    }

    // ── Scene ───────────────────────────────────────────────────────────────────────────────────────

    private void UploadMesh(TerrainMesh mesh)
    {
        DisposeScene();
        _positions = CreateDefaultBuffer<float>(mesh.Positions);
        _normals = CreateDefaultBuffer<float>(mesh.Normals);
        _indices = CreateDefaultBuffer<uint>(mesh.Indices);
        _mapNormals = mesh.MapNormals.Length > 0 ? CreateDefaultBuffer<uint>(mesh.MapNormals) : null;
    }

    /// <summary>Uploads the world's normal maps when the mesh's are not the ones already on the GPU (M1.9).</summary>
    private void EnsureNormalMaps(IReadOnlyList<NormalMap> maps)
    {
        if (ReferenceEquals(maps, _normalMaps))
            return;
        DisposeNormalMaps();
        _mapBuffers = [.. maps.Take(2).Select(m => (ID3D12Resource?)CreateDefaultBuffer<uint>(m.Packed))];
        _normalMaps = maps;
    }

    /// <summary>
    /// Refuses a mesh this GPU cannot take, before anything is uploaded (vista-plan §10 M1.9): more triangles than one build may
    /// hold, or more memory — the mesh's buffers, its acceleration structure and the build's scratch — than the adapter's budget has
    /// free. Either way the view's error target is too fine for this frame; the message says so.
    /// </summary>
    private void CheckFits(TerrainMesh mesh)
    {
        if (mesh.TriangleCount > MaxTriangles)
        {
            throw new InsufficientMemoryException(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"The terrain mesh has {mesh.TriangleCount:N0} triangles; one acceleration structure takes at most {MaxTriangles:N0} here. Raise --mesh-error."));
        }
        if (_adapter is null)
            return;
        RaytracingAccelerationStructurePrebuildInfo info = _device.GetRaytracingAccelerationStructurePrebuildInfo(BlasInputs(mesh, 0, 0));
        long need = mesh.ByteSize + (long)info.ResultDataMaxSizeInBytes + (long)info.ScratchDataSizeInBytes;
        QueryVideoMemoryInfo memory = _adapter.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
        long free = (long)memory.Budget - (long)memory.CurrentUsage;
        if (need > free)
        {
            throw new InsufficientMemoryException(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"The terrain mesh ({mesh.TriangleCount:N0} triangles) needs {need / 1048576.0:N0} MiB of video memory with its acceleration structure; " +
                $"{free / 1048576.0:N0} MiB is free. Raise --mesh-error, or free the GPU."));
        }
    }

    /// <summary>The bottom-level build of <paramref name="mesh"/> from buffers at these addresses (zero for a size query).</summary>
    private static BuildRaytracingAccelerationStructureInputs BlasInputs(TerrainMesh mesh, ulong positions, ulong indices) => new()
    {
        Type = RaytracingAccelerationStructureType.BottomLevel,
        Layout = ElementsLayout.Array,
        Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace,
        DescriptorsCount = 1,
        GeometryDescriptions =
        [
            new RaytracingGeometryDescription
            {
                Type = RaytracingGeometryType.Triangles,
                Flags = RaytracingGeometryFlags.Opaque,
                Triangles = new RaytracingGeometryTrianglesDescription
                {
                    VertexBuffer = new GpuVirtualAddressAndStride(positions, 3 * sizeof(float)),
                    VertexCount = (uint)mesh.VertexCount,
                    VertexFormat = Format.R32G32B32_Float,
                    IndexBuffer = indices,
                    IndexCount = (uint)mesh.Indices.Length,
                    IndexFormat = Format.R32_UInt,
                    Transform3x4 = 0,
                },
            },
        ],
    };

    private void BuildAccelerationStructures(TerrainMesh mesh)
    {
        BuildRaytracingAccelerationStructureInputs blasInputs = BlasInputs(mesh, _positions!.GPUVirtualAddress, _indices!.GPUVirtualAddress);
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

    /// <summary>
    /// Runs one planned batch; with the GPU shared (<see cref="GpuShare"/>) as several shorter dispatches of fewer rows, and of fewer
    /// samples when one row takes too long, each under <see cref="GpuShare.MaxDispatchMs"/> at the last cost per pixel-sample and at
    /// most <see cref="DispatchPace.Growth"/> times the last's pixel-samples (<paramref name="pace"/>, updated): a render's first
    /// dispatch is one row of one sample, so a costly view (a reference through dense mist) never runs a long dispatch before its
    /// cost is known, nor does a cheap row of sky size a costly one. Each row band runs all its samples in order before the next, so
    /// every pixel adds its samples in order and the image is the batch's own, bit for bit. Returns the GPU seconds.
    /// </summary>
    private double DispatchBatch(ID3D12PipelineState pipeline, IntegratorConstants constants, int width, SampleBatch batch, ref DispatchPace pace)
    {
        double seconds = 0.0;
        for (int row = 0; row < batch.RowCount;)
        {
            int rows = (int)Math.Clamp(pace.Allowed() / ((double)width * batch.SampleCount), 1.0, batch.RowCount - row);
            for (int sample = 0; sample < batch.SampleCount;)
            {
                int count = rows == 1 ? (int)Math.Clamp(pace.Allowed() / width, 1.0, batch.SampleCount - sample) : batch.SampleCount;
                constants.RowStart = (uint)(batch.RowStart + row);
                constants.RowCount = (uint)rows;
                constants.FirstSample = (uint)(batch.FirstSample + sample);
                constants.SampleCount = (uint)count;
                double s = Dispatch(pipeline, constants, width, rows);
                seconds += s;
                pace.Ran(s, (double)rows * width * count);
                sample += count;
            }
            row += rows;
        }
        return seconds;
    }

    /// <summary>The trace's dispatches so far, which size the next with the GPU shared (<see cref="DispatchBatch"/>).</summary>
    private struct DispatchPace
    {
        /// <summary>How many times the last dispatch's pixel-samples the next may take.</summary>
        public const double Growth = 4.0;

        private double _costNs, _lastPixelSamples;

        /// <summary>The longest dispatch so far, s.</summary>
        public double Longest { get; private set; }

        /// <summary>The pixel-samples the next dispatch may take: all of them at full speed, one before the cost is known.</summary>
        public readonly double Allowed() => !GpuShare.Capped ? double.MaxValue
            : _costNs > 0.0 ? Math.Min(GpuShare.MaxDispatchMs * 1e6 / _costNs, Growth * _lastPixelSamples) : 1.0;

        /// <summary>Records a dispatch of <paramref name="pixelSamples"/> that took <paramref name="seconds"/>.</summary>
        public void Ran(double seconds, double pixelSamples)
        {
            _lastPixelSamples = pixelSamples;
            _costNs = seconds * 1e9 / pixelSamples;
            Longest = Math.Max(Longest, seconds);
        }
    }

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
        list.SetComputeRootShaderResourceView(SlotMapNormals, (_mapNormals ?? _dummySrv).GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotNormalMap0, (_mapBuffers.ElementAtOrDefault(0) ?? _dummySrv).GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotNormalMap1, (_mapBuffers.ElementAtOrDefault(1) ?? _dummySrv).GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotMaterialMap, (_materialMap ?? _dummySrv).GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotBedTable, (_bedTable ?? _dummySrv).GPUVirtualAddress);
        list.SetComputeRootConstantBufferView(SlotMaterialConstants, _materialConstants.GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(SlotLowAir, (_lowAirBuffer ?? _dummySrv).GPUVirtualAddress);
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
        double seconds = (double)elapsed / _timestampFrequency;
        GpuShare.Pause(seconds); // the desktop's turn on a shared GPU
        return seconds;
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
    private ID3D12Resource CreateDefaultBuffer<T>(ReadOnlySpan<T> data) where T : unmanaged
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

    private void DisposeNormalMaps()
    {
        foreach (ID3D12Resource? buffer in _mapBuffers)
            buffer?.Dispose();
        (_mapBuffers, _normalMaps) = ([], null);
    }

    private void DisposeScene()
    {
        _mapNormals?.Dispose();
        _mapNormals = null;
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
        DisposeNormalMaps();
        DisposeMaterials();
        _materialProbePipeline?.Dispose();
        _materialConstants?.Dispose();
        DisposeSkyLight();
        _incidentPipeline?.Dispose();
        DisposeLowAirPipelines();
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
        _lowAirPipeline?.Dispose();
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
