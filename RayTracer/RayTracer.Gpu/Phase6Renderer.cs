using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RayTracer;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.Direct3D12.Debug;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace RayTracer.Gpu;

/// <summary>
/// Phase 6 renderer: <b>productization</b> of the Phase 5 temporal + volumetric +
/// debug-view pipeline. The per-frame rendering is identical to Phase 5 — it reuses
/// the exact Phase 5 shaders (<c>PathTracePhase5.hlsl</c> / <c>ResolvePhase5.hlsl</c>
/// / <c>ReducePhase5.hlsl</c>) and produces bit-for-bit the same Beauty image — so
/// the frame-0 parity self-test still holds. What Phase 6 adds is host-side
/// robustness the earlier phases (fixed-size, throwaway-window demos) skipped:
///
/// <list type="bullet">
/// <item><b>Resize.</b> All size-dependent resources (the output texture, the UAV
///   descriptor heap, and every per-pixel buffer — accumulation, G-buffer, TAA
///   history, the statistics AOVs) are split out of one-time init into
///   <see cref="CreateSizeDependentResources"/> / <see cref="DisposeSizeDependentResources"/>
///   so <see cref="Resize"/> can tear them down and rebuild them at a new resolution
///   (and <see cref="IDXGISwapChain3.ResizeBuffers"/> the back buffers). Accumulation
///   resets after a resize (a resized image is a fresh convergence).</item>
/// <item><b>Device-removed recovery.</b> The device stack (device, queue, PSOs,
///   acceleration structures, all resources) is factored into
///   <see cref="DisposeGpuResources"/> so <see cref="TryRecoverDevice"/> can dispose
///   and re-run <see cref="Initialize"/> after a TDR / driver reset — the scene data
///   the renderer keeps (<c>_scene</c>/<c>_spectral</c>/<c>_lights</c>) is enough to
///   rebuild from scratch. <see cref="RenderFrame"/> returns <c>false</c> when a
///   <c>Present</c> / GPU wait reports the device lost so the caller can recover
///   (pitfall P10/P12).</item>
/// <item><b>Arbitrary window handle.</b> <see cref="Initialize"/> already takes an
///   HWND; Phase 6 stores it so recovery can rebuild the swap chain, and so the
///   screensaver preview path (6.2) can render into the settings dialog's child
///   window.</item>
/// </list>
///
/// As with Phase 1–5 the D3D12 scaffolding is intentionally duplicated rather than
/// refactoring the earlier (separately dev-box-validated) hosts.
/// </summary>
internal sealed class Phase6Renderer : IDisposable
{
    private const int FrameCount = 2;
    private const Format BackBufferFormat = Format.R8G8B8A8_UNorm;

    private int _width;
    private int _height;
    // Not readonly: RebuildScene swaps these in when the maze regenerates (plan §9).
    private PackedScene _scene;
    private SpectralResources _spectral;
    private PackedLights _lights;
    private readonly LightingMode _lightingMode;
    private readonly float _sampleClamp;
    private readonly uint _maxSampleCount;
    private readonly bool _subPixelJitter;
    private readonly uint _motionSampleCap;
    private readonly float _temporalBlendAlpha;
    private readonly uint _filterRadius;
    private readonly VolumetricOptions _volumetrics;
    private readonly bool _biomeIndicator;
    private readonly bool _bumpyWalls;

    private nint _windowHandle;
    private Camera _camera;
    private float _volTime;      // seconds; advances the fog animation (0 = static)
    private bool _forceReset;    // set by Resize / recovery: hard-reset the next frame

    // Last frame's reduced statistics (double-buffered readback, pitfall P6). Drives
    // the SampleCount / Variance debug normalizers and the windowed HUD.
    private Phase5Stats _lastStats;

    /// <summary>The debug view the resolve pass renders. Beauty (default) is the
    /// full Phase 4 image.</summary>
    public Phase5DebugMode DebugMode { get; set; } = Phase5DebugMode.Beauty;

    /// <summary>The most recent frame statistics read back from the on-GPU reduction.
    /// One frame behind the displayed image, as the readback is double-buffered.</summary>
    public Phase5Stats FrameStats => _lastStats;

    /// <summary>Current render width (updated by <see cref="Resize"/>).</summary>
    public int Width => _width;

    /// <summary>Current render height (updated by <see cref="Resize"/>).</summary>
    public int Height => _height;

    // Previous-frame camera state that reprojection targets.
    private Vector3 _prevCamPos;
    private Quaternion _prevCamRot;
    private bool _hasPrevCamera;
    private int _historyIndex; // ping-pong selector: read [_historyIndex], write [_historyIndex ^ 1]

    private IDXGIFactory4 _factory = null!;
    private ID3D12Device5 _device = null!;
    private ID3D12CommandQueue _queue = null!;
    private ID3D12CommandAllocator _allocator = null!;
    private ID3D12GraphicsCommandList4 _commandList = null!;

    private ID3D12Fence _fence = null!;
    private ulong _fenceValue;
    private readonly AutoResetEvent _fenceEvent = new(false);

    private ID3D12RootSignature _traceRootSignature = null!;
    private ID3D12PipelineState _tracePipeline = null!;
    private ID3D12RootSignature _resolveRootSignature = null!;
    private ID3D12PipelineState _resolvePipeline = null!;
    private ID3D12DescriptorHeap _uavHeap = null!;
    private ID3D12Resource _outputTexture = null!;

    // Scene / spectral / light resources.
    private ID3D12Resource _vertexBuffer = null!;
    private ID3D12Resource _indexBuffer = null!;
    private ID3D12Resource _primitiveBuffer = null!;
    // Analytic spheres (§0.4): a GpuSphere buffer (t8) + a procedural-AABB buffer for the BLAS.
    // A 1-element dummy when the scene has no spheres, so the SRV binding stays valid and the
    // BLAS stays triangle-only (byte-identical to the pre-sphere path).
    private ID3D12Resource _sphereBuffer = null!;
    private ID3D12Resource _aabbBuffer = null!;
    private int _sphereCount;
    // CPU-side copy of the packed spheres, so UpdateSpheres can rewrite just the centre/radius
    // of moving bubbles (§2.6) while preserving each sphere's material/optical fields.
    private GpuSphere[] _sphereData = [];
    private ID3D12Resource _deterXyzBuffer = null!;
    private ID3D12Resource _materialReflectanceBuffer = null!;
    // Caustic photon grid (shadows-and-caustics-plan §B4): the flattened PhotonMap uploaded as a photon
    // SRV (t9) + a per-cell (start,count) SRV (t10). A 1-element dummy binds when caustics are off, so
    // the trace root signature stays valid and the shader's gather early-outs (CausticEnabled == 0).
    private ID3D12Resource _causticPhotonBuffer = null!;
    private ID3D12Resource _causticCellBuffer = null!;
    private bool _causticEnabled;
    private (int X, int Y, int Z) _causticMinCell;
    private (uint X, uint Y, uint Z) _causticDim = (1u, 1u, 1u);
    private float _causticCellSize = 1f;
    private float _causticRadius = 1f;
    private float _causticStrength = 1f;
    // §A3: true when the scene carries dielectric triangles (glass windows §1.2 / glass jewels), so
    // TraceTransmittance forces triangles non-opaque and lets glass cast soft Fresnel-dimmed shadows
    // instead of hard black ones. False for glass-free mazes → the shadow ray keeps the pure
    // accept-first-hit fast path and stays byte-identical (selftest / regress carry no glass).
    private bool _shadowTransmitTriangles;
    // Maps a deterministic wavelength (nm) to its DeterXYZ row, so an uploaded photon carries the row
    // the shader indexes for XYZ(λ). Built in CreateCausticResources from the spectral tables.
    private readonly Dictionary<int, uint> _deterWavelengthRow = [];
    private ID3D12Resource _lightBuffer = null!;
    private ID3D12Resource _lightColorBuffer = null!;
    // Decal shading (plan §3): RGB→reflectance basis + the prop atlas. Both are static
    // across maze regenerations (basis is wavelength-derived, atlas is fixed art), so
    // RebuildScene leaves them alone; only device teardown recreates them.
    // DecalAtlasSize must match PropTextures.Size and the shader's DECAL_SIZE.
    private const int DecalAtlasSize = 128;
    private ID3D12Resource _rgbBasisBuffer = null!;
    private ID3D12Resource _decalBuffer = null!;
    // Overhead map (plan §7): maze wall bitmap + dims, set by SetMinimap; rebuilt per maze.
    private ID3D12Resource _mazeGridBuffer = null!;
    private uint _mazeGw = 1, _mazeGh = 1;
    private readonly bool _showOverheadMap;
    // Animated rat billboard (plan §8): world position updated each frame by the host.
    private readonly bool _showRat;
    private readonly float _classicDepthCue; // §1.4 classic depth-cue strength (0 = off)
    private readonly int _pixelSize;         // §1.5 retro pixelation block edge in pixels (≤1 = off)
    private Vector3 _ratPos;
    // Build-in reveal height (plan §9.3): large ⇒ no cull (walls fully built).
    private float _revealHeight = 1e9f;
    // Outro fade level (plan §9.4): 0 = normal, 1 = black.
    private float _fadeLevel;

    // Accumulation + G-buffer.
    private ID3D12Resource _accumBuffer = null!;
    private ID3D12Resource _directAccumBuffer = null!;
    private ID3D12Resource _indirectAccumBuffer = null!;
    private ID3D12Resource _sampleCountBuffer = null!;
    private ID3D12Resource _wavelengthCounterBuffer = null!;
    private ID3D12Resource _lastHitBuffer = null!;
    private ID3D12Resource _hitPointBuffer = null!;
    private ID3D12Resource _normalBuffer = null!;
    private ID3D12Resource _fogBuffer = null!;

    // Statistics AOVs + reduction.
    private ID3D12Resource _lumaM2Buffer = null!;
    private ID3D12Resource _clampAmountBuffer = null!;
    private ID3D12Resource _clampHitFrameBuffer = null!;
    private ID3D12Resource _historyWeightBuffer = null!;
    private ID3D12Resource _rejectedBuffer = null!;
    private ID3D12Resource _statsBuffer = null!;    // 8-float reduction output (default heap UAV)
    private ID3D12Resource _statsReadback = null!;  // readback copy the CPU maps
    private ID3D12Resource _reduceConstantBuffer = null!;
    private ID3D12RootSignature _reduceRootSignature = null!;
    private ID3D12PipelineState _reducePipeline = null!;
    private ID3D12RootSignature _pixelateRootSignature = null!; // §1.5 retro pixelation
    private ID3D12PipelineState _pixelatePipeline = null!;

    // Ping-pong TAA history (index 0/1 selected per frame).
    private readonly ID3D12Resource[] _historyXyz = new ID3D12Resource[2];
    private readonly ID3D12Resource[] _historyHit = new ID3D12Resource[2];
    private readonly ID3D12Resource[] _historyValid = new ID3D12Resource[2];

    private ID3D12Resource _traceConstantBuffer = null!;
    private ID3D12Resource _resolveConstantBuffer = null!;

    private ID3D12Resource _blas = null!;
    private ID3D12Resource _tlas = null!;
    private ID3D12Resource _blasScratch = null!;
    private ID3D12Resource _tlasScratch = null!;
    private ID3D12Resource _instanceBuffer = null!;
    // Spheres live in a separate BLAS (§0.4): D3D12 forbids mixing triangle + AABB geometry in
    // one BLAS, so the TLAS references both (a second instance) when the scene has spheres.
    private ID3D12Resource _sphereBlas = null!;
    private ID3D12Resource _sphereBlasScratch = null!;

    private IDXGISwapChain3? _swapChain;
    private readonly ID3D12Resource[] _backBuffers = new ID3D12Resource[FrameCount];

    public string AdapterName { get; private set; } = "unknown";

    public Phase6Renderer(
        int width, int height,
        PackedScene scene, SpectralResources spectral, PackedLights lights, Camera camera,
        VolumetricOptions volumetrics,
        LightingMode lightingMode = LightingMode.NEE, float sampleClamp = 0f,
        uint maxSampleCount = 2048, bool subPixelJitter = true,
        uint motionSampleCap = 12, float temporalBlendAlpha = 0.1f,
        uint filterRadius = 1, bool biomeIndicator = false,
        Phase5DebugMode debugMode = Phase5DebugMode.Beauty, bool bumpyWalls = false,
        bool showOverheadMap = false, bool showRat = false, float classicDepthCue = 0f,
        int pixelSize = 1)
    {
        _width = width;
        _height = height;
        _scene = scene;
        _shadowTransmitTriangles = HasTransmissiveTriangles(scene);
        _spectral = spectral;
        _lights = lights;
        _lightingMode = lightingMode;
        _sampleClamp = sampleClamp;
        _camera = camera;
        _volumetrics = volumetrics;
        _biomeIndicator = biomeIndicator;
        _bumpyWalls = bumpyWalls;
        _showOverheadMap = showOverheadMap;
        _showRat = showRat;
        _classicDepthCue = classicDepthCue;
        _pixelSize = Math.Max(1, pixelSize);
        _maxSampleCount = maxSampleCount;
        _subPixelJitter = subPixelJitter;
        _motionSampleCap = motionSampleCap;
        _temporalBlendAlpha = temporalBlendAlpha;
        _filterRadius = filterRadius;
        DebugMode = debugMode;
    }

    // True if any triangle primitive is a dielectric (glass window / jewel) — those are the only
    // triangles that transmit a shadow ray (mirror / thin-film / diffuse all cast hard shadows,
    // like BVH.Transmittance's default case). Bubbles are procedural spheres, handled separately.
    private static bool HasTransmissiveTriangles(PackedScene scene)
    {
        foreach (GpuPrimitive p in scene.Primitives)
            if (p.Surface == (uint)SurfaceKind.Dielectric)
                return true;
        return false;
    }

    // ── Constant buffer layouts (identical to Phase 5) ────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct TraceConstants
    {
        public float CamPosX, CamPosY, CamPosZ, Pad0;
        public float RotX, RotY, RotZ, RotW;
        public float TanHalfFov, AspectTanHalfFov, InvWidth, InvHeight;
        public float ImgPlaneZ, DeterministicCorrection, AmbientLevel, LightIntensity;
        public uint Width, Height, MaxSampleCount, ResetFlag;
        public uint NumPrimitives, SubPixelJitter, NumLights, LightingMode;
        public float SampleClamp;
        public uint SoftResetFlag, MotionSampleCap;
        public uint BumpyWalls; // repurposed from Pad1 (plan §6, §7 unrelated)
        public uint VolEnabled, VolSmokeMode, VolMarchSteps, VolShadowStepInterval;
        public float VolMaxMarchDistance, VolSigmaScaleFog, VolSigmaScaleGround, VolAnisotropyG;
        public float VolInscatterStrength, VolEarlyOutTransmittance;
        public uint BiomeIndicator;
        public float VolTime;
        public float RevealHeight; // §9.3 build-in reveal
        public float RatPosX, RatPosY, RatPosZ; // §8 rat billboard (for reflections)
        public float RatSize;
        public uint ShowRat, RatLayer;
        public uint VolShadowTransmittance; // §A2 fog self-shadow opt-in (repurposed from RPad)
        // §B4 caustic photon-grid params (match PathTracePhase6.hlsl + CausticReference).
        public uint CausticEnabled;
        public int CausticMinCellX, CausticMinCellY, CausticMinCellZ;
        public uint CausticDimX, CausticDimY, CausticDimZ;
        public float CausticCellSize;
        public float CausticRadius, CausticStrength;
        public uint ShadowTransmitTriangles; // §A3: glass triangles cast soft shadows (repurposed from CPad0)
        public float CPad1;
    }

    // Matches the shader's CausticPhoton (32-byte structured-buffer element): a caustic photon's world
    // landing point, receiver normal, radiant power, and its DeterXYZ row (baked wavelength → XYZ).
    [StructLayout(LayoutKind.Sequential)]
    private struct GpuCausticPhoton
    {
        public float PX, PY, PZ;
        public float NX, NY, NZ;
        public float Power;
        public uint WlRow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ResolveConstants
    {
        public float PrevCamPosX, PrevCamPosY, PrevCamPosZ, RPad0;
        public float PrevRotX, PrevRotY, PrevRotZ, PrevRotW;
        public float TanHalfFov, AspectTanHalfFov, TemporalBlendAlpha, ReprojThreshold;
        public uint Width, Height, UseTaa, IsMoving;
        public uint FilterRadius, DebugMode;
        public float VarianceNorm;
        public uint ShowMap; // §7 overhead map (repurposed from RPad1)
        public float CurrCamPosX, CurrCamPosY, CurrCamPosZ, SampleCountNorm;
        public uint MazeGw, MazeGh, PlayerGx, PlayerGy; // §7 minimap dims + player grid cell
        public float CurrRotX, CurrRotY, CurrRotZ, CurrRotW; // §8 rat projection
        public float RatPosX, RatPosY, RatPosZ, RatSize;     // §8 rat billboard
        public uint ShowRat, RatLayer;                        // §8
        public float FadeLevel;                               // §9.4 outro fade
        public float ClassicDepthCue;                         // §1.4 classic depth-cue strength (0 = off)
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReduceConstants
    {
        public uint PixelCount, RPad0, RPad1, RPad2;
    }

    // ── Initialization ────────────────────────────────────────────────

    public void Initialize(nint windowHandle)
    {
        _windowHandle = windowHandle;
        bool debug = EnableDebugLayer();
        CreateDeviceAndQueue(debug);
        VerifyRaytracingSupport();
        CreateCommandObjects();
        CreateConstantAndStatsResources();
        CreateSizeDependentResources();
        CreateSceneResources();
        CreateSpectralResources();
        CreateLightResources();
        CreateDecalResources();
        CreateCausticResources();
        CreatePipelines();
        BuildAccelerationStructures();

        if (windowHandle != 0)
            CreateSwapChain(windowHandle);
    }

    private static bool EnableDebugLayer()
    {
#if DEBUG
        if (D3D12GetDebugInterface<ID3D12Debug>(out var debugController).Success && debugController is not null)
        {
            debugController.EnableDebugLayer();
            debugController.Dispose();
            return true;
        }
#endif
        return false;
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

        if (_device is null)
        {
            for (uint i = 0; _factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
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
        }

        if (_device is null || chosen is null)
            throw new PlatformNotSupportedException("No Direct3D 12 capable GPU was found.");

        AdapterName = chosen.Description1.Description;
        chosen.Dispose();

        _queue = _device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
        _fence = _device.CreateFence(0, FenceFlags.None);
        _fenceValue = 0;
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
        _commandList = _device.CreateCommandList<ID3D12GraphicsCommandList4>(
            0, CommandListType.Direct, _allocator, null);
        _commandList.Close();
    }

    // Size-independent constant + stats buffers (created once per device). The
    // reduction constant's pixel count is refreshed by CreateSizeDependentResources.
    private void CreateConstantAndStatsResources()
    {
        _statsBuffer = CreateUavBuffer(8 * sizeof(float), ResourceStates.UnorderedAccess);
        _statsReadback = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Readback), HeapFlags.None,
            ResourceDescription.Buffer(8 * sizeof(float)), ResourceStates.CopyDest);

        // Constant buffers must be 256-byte aligned.
        _traceConstantBuffer = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload), HeapFlags.None,
            ResourceDescription.Buffer(256), ResourceStates.GenericRead);
        _resolveConstantBuffer = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload), HeapFlags.None,
            ResourceDescription.Buffer(256), ResourceStates.GenericRead);
        _reduceConstantBuffer = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload), HeapFlags.None,
            ResourceDescription.Buffer(256), ResourceStates.GenericRead);
    }

    // Everything whose size depends on the render resolution. Split out of one-time
    // init so Resize can dispose and rebuild it at a new size.
    private void CreateSizeDependentResources()
    {
        var texDesc = ResourceDescription.Texture2D(
            BackBufferFormat, (uint)_width, (uint)_height, 1, 1, 1, 0,
            ResourceFlags.AllowUnorderedAccess);
        _outputTexture = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default), HeapFlags.None, texDesc,
            ResourceStates.UnorderedAccess);

        _uavHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            1, DescriptorHeapFlags.ShaderVisible));

        var uavDesc = new UnorderedAccessViewDescription
        {
            Format = BackBufferFormat,
            ViewDimension = UnorderedAccessViewDimension.Texture2D,
        };
        _device.CreateUnorderedAccessView(
            _outputTexture, null, uavDesc, _uavHeap.GetCPUDescriptorHandleForHeapStart());

        int pixels = _width * _height;
        ulong f4 = (ulong)(pixels * 4 * sizeof(float));
        ulong u1 = (ulong)(pixels * sizeof(uint));
        ulong f1 = (ulong)(pixels * sizeof(float));

        _accumBuffer = CreateUavBuffer(f4, ResourceStates.UnorderedAccess);
        _directAccumBuffer = CreateUavBuffer(f4, ResourceStates.UnorderedAccess);
        _indirectAccumBuffer = CreateUavBuffer(f4, ResourceStates.UnorderedAccess);
        _sampleCountBuffer = CreateUavBuffer(u1, ResourceStates.UnorderedAccess);
        _wavelengthCounterBuffer = CreateUavBuffer(u1, ResourceStates.UnorderedAccess);
        _lastHitBuffer = CreateUavBuffer(u1, ResourceStates.UnorderedAccess);
        _hitPointBuffer = CreateUavBuffer(f4, ResourceStates.UnorderedAccess);
        _normalBuffer = CreateUavBuffer(f4, ResourceStates.UnorderedAccess);
        _fogBuffer = CreateUavBuffer(f4, ResourceStates.UnorderedAccess);

        _lumaM2Buffer = CreateUavBuffer(f1, ResourceStates.UnorderedAccess);
        _clampAmountBuffer = CreateUavBuffer(f1, ResourceStates.UnorderedAccess);
        _clampHitFrameBuffer = CreateUavBuffer(u1, ResourceStates.UnorderedAccess);
        _historyWeightBuffer = CreateUavBuffer(f1, ResourceStates.UnorderedAccess);
        _rejectedBuffer = CreateUavBuffer(u1, ResourceStates.UnorderedAccess);

        for (int i = 0; i < 2; i++)
        {
            _historyXyz[i] = CreateUavBuffer(f4, ResourceStates.UnorderedAccess);
            _historyHit[i] = CreateUavBuffer(f4, ResourceStates.UnorderedAccess);
            _historyValid[i] = CreateUavBuffer(u1, ResourceStates.UnorderedAccess);
        }

        // The reduction's pixel count follows the current resolution.
        Span<ReduceConstants> rc = _reduceConstantBuffer.Map<ReduceConstants>(0, 1);
        rc[0] = new ReduceConstants { PixelCount = (uint)pixels };
        _reduceConstantBuffer.Unmap(0);
    }

    private void DisposeSizeDependentResources()
    {
        for (int i = 0; i < 2; i++)
        {
            _historyValid[i]?.Dispose(); _historyValid[i] = null!;
            _historyHit[i]?.Dispose(); _historyHit[i] = null!;
            _historyXyz[i]?.Dispose(); _historyXyz[i] = null!;
        }
        _rejectedBuffer?.Dispose(); _rejectedBuffer = null!;
        _historyWeightBuffer?.Dispose(); _historyWeightBuffer = null!;
        _clampHitFrameBuffer?.Dispose(); _clampHitFrameBuffer = null!;
        _clampAmountBuffer?.Dispose(); _clampAmountBuffer = null!;
        _lumaM2Buffer?.Dispose(); _lumaM2Buffer = null!;
        _fogBuffer?.Dispose(); _fogBuffer = null!;
        _normalBuffer?.Dispose(); _normalBuffer = null!;
        _hitPointBuffer?.Dispose(); _hitPointBuffer = null!;
        _lastHitBuffer?.Dispose(); _lastHitBuffer = null!;
        _wavelengthCounterBuffer?.Dispose(); _wavelengthCounterBuffer = null!;
        _sampleCountBuffer?.Dispose(); _sampleCountBuffer = null!;
        _indirectAccumBuffer?.Dispose(); _indirectAccumBuffer = null!;
        _directAccumBuffer?.Dispose(); _directAccumBuffer = null!;
        _accumBuffer?.Dispose(); _accumBuffer = null!;
        _uavHeap?.Dispose(); _uavHeap = null!;
        _outputTexture?.Dispose(); _outputTexture = null!;
    }

    private void CreateSceneResources()
    {
        _vertexBuffer = CreateUploadBuffer<float>(_scene.Vertices, _scene.Vertices.Length * sizeof(float));
        _indexBuffer = CreateUploadBuffer<uint>(_scene.Indices, _scene.Indices.Length * sizeof(uint));
        int primBytes = _scene.Primitives.Length * Unsafe.SizeOf<GpuPrimitive>();
        _primitiveBuffer = CreateUploadBuffer<GpuPrimitive>(_scene.Primitives, primBytes);

        // Analytic spheres (§0.4): the GpuSphere buffer + a parallel procedural-AABB buffer
        // (min/max per sphere) for the BLAS. Fall back to a single dummy so the SRV binding is
        // always valid; the BLAS omits the procedural geometry entirely when _sphereCount == 0.
        _sphereCount = _scene.Spheres.Count;
        _sphereData = _sphereCount > 0 ? [.. _scene.Spheres] : [default];
        _sphereBuffer = CreateUploadBuffer<GpuSphere>(_sphereData, _sphereData.Length * Unsafe.SizeOf<GpuSphere>());

        var aabbs = new float[Math.Max(1, _sphereCount) * 6];
        for (int i = 0; i < _sphereCount; i++)
        {
            GpuSphere s = _scene.Spheres[i];
            aabbs[i * 6 + 0] = s.CX - s.Radius; aabbs[i * 6 + 1] = s.CY - s.Radius; aabbs[i * 6 + 2] = s.CZ - s.Radius;
            aabbs[i * 6 + 3] = s.CX + s.Radius; aabbs[i * 6 + 4] = s.CY + s.Radius; aabbs[i * 6 + 5] = s.CZ + s.Radius;
        }
        _aabbBuffer = CreateUploadBuffer<float>(aabbs, aabbs.Length * sizeof(float));
    }

    private void CreateSpectralResources()
    {
        _deterXyzBuffer = CreateUploadBuffer<float>(_spectral.DeterXYZ, _spectral.DeterXYZ.Length * sizeof(float));
        _materialReflectanceBuffer = CreateUploadBuffer<float>(
            _spectral.MaterialReflectance, _spectral.MaterialReflectance.Length * sizeof(float));
    }

    /// <summary>
    /// Creates the caustic photon-grid SRVs (§B4) as 1-element dummies so the trace root signature is
    /// always bindable; a real grid is uploaded later by <see cref="SetCausticGrid"/>. Also caches the
    /// deterministic wavelength → DeterXYZ row map used to bake each uploaded photon's XYZ index.
    /// </summary>
    private void CreateCausticResources()
    {
        _causticPhotonBuffer = CreateUploadBuffer<GpuCausticPhoton>([default], Unsafe.SizeOf<GpuCausticPhoton>());
        _causticCellBuffer = CreateUploadBuffer<uint>([0u, 0u], 2 * sizeof(uint));
        _causticEnabled = false;

        _deterWavelengthRow.Clear();
        int[] deter = _spectral.DeterWavelengths;
        for (uint i = 0; i < deter.Length; i++)
            _deterWavelengthRow[deter[i]] = i;
    }

    /// <summary>
    /// Uploads a flattened caustic photon grid (<see cref="PhotonMap.BuildGrid"/>) so the trace shader
    /// gathers it at diffuse hits (§B4). Photons carry their wavelength as a DeterXYZ row. Pass an empty
    /// grid (or never call this) to leave caustics off — the shader then early-outs and the image is
    /// byte-identical. Safe to call after <see cref="Initialize"/> and again per frame as casters drift.
    /// </summary>
    /// <param name="grid">The flattened photon grid to gather.</param>
    /// <param name="gatherRadius">Density-estimate radius (typically the grid's cell size).</param>
    /// <param name="strength">Overall composite multiplier for the caustic irradiance.</param>
    public void SetCausticGrid(CausticGrid grid, float gatherRadius, float strength)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (_device is null) return;
        WaitForGpu();
        _causticPhotonBuffer?.Dispose();
        _causticCellBuffer?.Dispose();

        if (grid.IsEmpty)
        {
            _causticPhotonBuffer = CreateUploadBuffer<GpuCausticPhoton>([default], Unsafe.SizeOf<GpuCausticPhoton>());
            _causticCellBuffer = CreateUploadBuffer<uint>([0u, 0u], 2 * sizeof(uint));
            _causticEnabled = false;
            return;
        }

        var photons = new GpuCausticPhoton[grid.Photons.Length];
        for (int i = 0; i < photons.Length; i++)
        {
            Photon p = grid.Photons[i];
            _deterWavelengthRow.TryGetValue(p.Wavelength, out uint row);
            photons[i] = new GpuCausticPhoton
            {
                PX = p.Position.X, PY = p.Position.Y, PZ = p.Position.Z,
                NX = p.Normal.X, NY = p.Normal.Y, NZ = p.Normal.Z,
                Power = p.Power, WlRow = row,
            };
        }

        var cells = new uint[grid.CellStart.Length * 2];
        for (int c = 0; c < grid.CellStart.Length; c++)
        {
            cells[c * 2] = (uint)grid.CellStart[c];
            cells[c * 2 + 1] = (uint)grid.CellCount[c];
        }

        _causticPhotonBuffer = CreateUploadBuffer<GpuCausticPhoton>(photons, photons.Length * Unsafe.SizeOf<GpuCausticPhoton>());
        _causticCellBuffer = CreateUploadBuffer<uint>(cells, cells.Length * sizeof(uint));
        _causticMinCell = (grid.MinCellX, grid.MinCellY, grid.MinCellZ);
        _causticDim = ((uint)grid.DimX, (uint)grid.DimY, (uint)grid.DimZ);
        _causticCellSize = grid.CellSize;
        _causticRadius = gatherRadius;
        _causticStrength = strength;
        _causticEnabled = true;
    }

    private void CreateLightResources()
    {
        float[] data = _lights.Count > 0 ? _lights.Data : new float[4];
        _lightBuffer = CreateUploadBuffer<float>(data, data.Length * sizeof(float));

        float[] colorData = _lights.Count > 0 ? _lights.ColorData : new float[4];
        _lightColorBuffer = CreateUploadBuffer<float>(colorData, colorData.Length * sizeof(float));
    }

    private void CreateDecalResources()
    {
        // RGB→reflectance basis (N×3) → float4 rows [br, bg, bb, 0] for the structured buffer.
        RgbToReflectanceBasis basis = RgbToReflectanceBasis.Build(_spectral);
        var basisF4 = new float[basis.Count * 4];
        for (int i = 0; i < basis.Count; i++)
        {
            basisF4[i * 4 + 0] = basis.Data[i * 3 + 0];
            basisF4[i * 4 + 1] = basis.Data[i * 3 + 1];
            basisF4[i * 4 + 2] = basis.Data[i * 3 + 2];
        }
        _rgbBasisBuffer = CreateUploadBuffer<float>(basisF4, basisF4.Length * sizeof(float));

        // Prop atlas: linear RGBA (float4 per texel), already in [layer*S*S + y*S + x] order.
        PropTextures props = PropTextures.Build(DecalAtlasSize);
        _decalBuffer = CreateUploadBuffer<float>(props.Pixels, props.Pixels.Length * sizeof(float));

        // Overhead-map grid: a 1-element placeholder until SetMinimap uploads a real maze
        // (the SRV must be bound every frame even when the map is off).
        _mazeGridBuffer = CreateUploadBuffer<uint>([0u], sizeof(uint));
        _mazeGw = _mazeGh = 1;
    }

    /// <summary>
    /// Uploads the maze wall bitmap for the overhead-map overlay (plan §7). Called after
    /// <see cref="Initialize"/> and whenever the maze regenerates. Safe to call with the map
    /// disabled — the shader ignores the buffer then.
    /// </summary>
    public void SetMinimap(uint[] grid, int gw, int gh)
    {
        if (_device is null) return;
        WaitForGpu();
        _mazeGridBuffer?.Dispose();
        _mazeGridBuffer = CreateUploadBuffer<uint>(grid, grid.Length * sizeof(uint));
        _mazeGw = (uint)gw;
        _mazeGh = (uint)gh;
    }

    private void CreatePipelines()
    {
        // Trace pass: SRVs t0-t5, UAVs u0-u2/u4-u12, CBV b0. Reuses Phase 5's shader.
        var traceParams = new[]
        {
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(0, 0), ShaderVisibility.All), // 0: TLAS (t0)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(1, 0), ShaderVisibility.All), // 1: Primitives (t1)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(2, 0), ShaderVisibility.All), // 2: DeterXYZ (t2)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(3, 0), ShaderVisibility.All), // 3: MaterialReflectance (t3)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(4, 0), ShaderVisibility.All), // 4: Lights (t4)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(0, 0), ShaderVisibility.All), // 5: Accum (u0)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(1, 0), ShaderVisibility.All), // 6: SampleCount (u1)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(2, 0), ShaderVisibility.All), // 7: WavelengthCounter (u2)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(4, 0), ShaderVisibility.All), // 8: LastHit (u4)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(5, 0), ShaderVisibility.All), // 9: DirectAccum (u5)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(6, 0), ShaderVisibility.All), // 10: IndirectAccum (u6)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(7, 0), ShaderVisibility.All), // 11: HitPointOut (u7)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(8, 0), ShaderVisibility.All), // 12: NormalOut (u8)
            new RootParameter1(RootParameterType.ConstantBufferView, new RootDescriptor1(0, 0), ShaderVisibility.All),  // 13: Constants (b0)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(5, 0), ShaderVisibility.All),  // 14: LightColors (t5)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(9, 0), ShaderVisibility.All), // 15: FogOut (u9)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(10, 0), ShaderVisibility.All), // 16: LumaM2 (u10)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(11, 0), ShaderVisibility.All), // 17: ClampAmount (u11)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(12, 0), ShaderVisibility.All), // 18: ClampHitFrame (u12)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(6, 0), ShaderVisibility.All),   // 19: RgbBasis (t6)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(7, 0), ShaderVisibility.All),   // 20: DecalPixels (t7)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(8, 0), ShaderVisibility.All),   // 21: Spheres (t8, §0.4)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(9, 0), ShaderVisibility.All),   // 22: CausticPhotons (t9, §B4)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(10, 0), ShaderVisibility.All),  // 23: CausticCells (t10, §B4)
        };
        _traceRootSignature = _device.CreateRootSignature(
            new RootSignatureDescription1(RootSignatureFlags.None, traceParams));

        byte[] traceBytecode = ShaderCompiler.CompileCompute(
            LoadShaderSource("PathTracePhase6.hlsl"), "CSMain", "PathTracePhase6.hlsl");
        _tracePipeline = _device.CreateComputePipelineState(new ComputePipelineStateDescription
        {
            RootSignature = _traceRootSignature,
            ComputeShader = traceBytecode,
        });

        var outputTable = new DescriptorRange1(DescriptorRangeType.UnorderedAccessView, 1, 3); // u3
        var resolveParams = new[]
        {
            new RootParameter1(new RootDescriptorTable1(outputTable), ShaderVisibility.All),                            // 0: Output (u3)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(0, 0), ShaderVisibility.All),  // 1: Accum (u0)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(4, 0), ShaderVisibility.All),  // 2: LastHit (u4)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(7, 0), ShaderVisibility.All),  // 3: HitPointIn (u7)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(8, 0), ShaderVisibility.All),  // 4: NormalIn (u8)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(9, 0), ShaderVisibility.All),  // 5: FogIn (u9)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(10, 0), ShaderVisibility.All), // 6: HistoryXyzIn (u10)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(11, 0), ShaderVisibility.All), // 7: HistoryHitIn (u11)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(12, 0), ShaderVisibility.All), // 8: HistoryValidIn (u12)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(13, 0), ShaderVisibility.All), // 9: HistoryXyzOut (u13)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(14, 0), ShaderVisibility.All), // 10: HistoryHitOut (u14)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(15, 0), ShaderVisibility.All), // 11: HistoryValidOut (u15)
            new RootParameter1(RootParameterType.ConstantBufferView, new RootDescriptor1(0, 0), ShaderVisibility.All),   // 12: ResolveConstants (b0)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(1, 0), ShaderVisibility.All),  // 13: SampleCount (u1)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(5, 0), ShaderVisibility.All),  // 14: DirectAccum (u5)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(6, 0), ShaderVisibility.All),  // 15: IndirectAccum (u6)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(2, 0), ShaderVisibility.All),  // 16: LumaM2 (u2)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(16, 0), ShaderVisibility.All), // 17: ClampAmountIn (u16)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(17, 0), ShaderVisibility.All), // 18: HistoryWeightOut (u17)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(18, 0), ShaderVisibility.All), // 19: RejectedOut (u18)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(0, 0), ShaderVisibility.All),  // 20: MazeGrid (t0, §7)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(1, 0), ShaderVisibility.All),  // 21: DecalPixels (t1, §8 rat)
        };
        _resolveRootSignature = _device.CreateRootSignature(
            new RootSignatureDescription1(RootSignatureFlags.None, resolveParams));

        byte[] resolveBytecode = ShaderCompiler.CompileCompute(
            LoadShaderSource("ResolvePhase6.hlsl"), "CSMain", "ResolvePhase6.hlsl");
        _resolvePipeline = _device.CreateComputePipelineState(new ComputePipelineStateDescription
        {
            RootSignature = _resolveRootSignature,
            ComputeShader = resolveBytecode,
        });

        var reduceParams = new[]
        {
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(0, 0), ShaderVisibility.All), // 0: SampleCount (u0)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(1, 0), ShaderVisibility.All), // 1: LumaM2 (u1)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(2, 0), ShaderVisibility.All), // 2: LastHit (u2)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(3, 0), ShaderVisibility.All), // 3: ClampHitFrame (u3)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(4, 0), ShaderVisibility.All), // 4: HistoryWeight (u4)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(5, 0), ShaderVisibility.All), // 5: Rejected (u5)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(6, 0), ShaderVisibility.All), // 6: Stats (u6)
            new RootParameter1(RootParameterType.ConstantBufferView, new RootDescriptor1(0, 0), ShaderVisibility.All),  // 7: ReduceConstants (b0)
        };
        _reduceRootSignature = _device.CreateRootSignature(
            new RootSignatureDescription1(RootSignatureFlags.None, reduceParams));

        byte[] reduceBytecode = ShaderCompiler.CompileCompute(
            LoadShaderSource("ReducePhase5.hlsl"), "CSMain", "ReducePhase5.hlsl");
        _reducePipeline = _device.CreateComputePipelineState(new ComputePipelineStateDescription
        {
            RootSignature = _reduceRootSignature,
            ComputeShader = reduceBytecode,
        });

        // §1.5 retro pixelation: an in-place post-pass on the resolved Output image. Its root
        // signature is just the Output UAV table (u3, reusing the resolve's descriptor heap
        // slot) plus 3 root constants (Width, Height, BlockSize) — no per-frame buffer.
        var pixelateOutputTable = new DescriptorRange1(DescriptorRangeType.UnorderedAccessView, 1, 3); // u3
        var pixelateParams = new[]
        {
            new RootParameter1(new RootDescriptorTable1(pixelateOutputTable), ShaderVisibility.All), // 0: Output (u3)
            new RootParameter1(new RootConstants(0, 0, 3), ShaderVisibility.All),                    // 1: PixelateConstants (b0)
        };
        _pixelateRootSignature = _device.CreateRootSignature(
            new RootSignatureDescription1(RootSignatureFlags.None, pixelateParams));

        byte[] pixelateBytecode = ShaderCompiler.CompileCompute(
            LoadShaderSource("PixelatePhase6.hlsl"), "CSMain", "PixelatePhase6.hlsl");
        _pixelatePipeline = _device.CreateComputePipelineState(new ComputePipelineStateDescription
        {
            RootSignature = _pixelateRootSignature,
            ComputeShader = pixelateBytecode,
        });
    }

    private static string LoadShaderSource(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Shaders", fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Shader not found at '{path}'.");
        return File.ReadAllText(path);
    }

    // ── Acceleration structures (identical to Phase 1/2/3/4/5) ─────────

    private void BuildAccelerationStructures()
    {
        int vertexCount = _scene.Vertices.Length / 3;
        int indexCount = _scene.Indices.Length;

        var geometries = new List<RaytracingGeometryDescription>
        {
            new()
            {
                Type = RaytracingGeometryType.Triangles,
                Flags = RaytracingGeometryFlags.Opaque,
                Triangles = new RaytracingGeometryTrianglesDescription
                {
                    VertexBuffer = new GpuVirtualAddressAndStride(_vertexBuffer.GPUVirtualAddress, 3 * sizeof(float)),
                    VertexCount = (uint)vertexCount,
                    VertexFormat = Format.R32G32B32_Float,
                    IndexBuffer = _indexBuffer.GPUVirtualAddress,
                    IndexCount = (uint)indexCount,
                    IndexFormat = Format.R32_UInt,
                    Transform3x4 = 0,
                },
            },
        };

        var blasInputs = new BuildRaytracingAccelerationStructureInputs
        {
            Type = RaytracingAccelerationStructureType.BottomLevel,
            Layout = ElementsLayout.Array,
            Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace,
            DescriptorsCount = (uint)geometries.Count,
            GeometryDescriptions = geometries.ToArray(),
        };
        RaytracingAccelerationStructurePrebuildInfo blasPrebuild =
            _device.GetRaytracingAccelerationStructurePrebuildInfo(blasInputs);

        _blasScratch = CreateUavBuffer(blasPrebuild.ScratchDataSizeInBytes, ResourceStates.UnorderedAccess);
        _blas = CreateUavBuffer(blasPrebuild.ResultDataMaxSizeInBytes, ResourceStates.RaytracingAccelerationStructure);

        // A separate sphere BLAS (procedural AABBs) — D3D12 forbids mixing geometry types (§0.4).
        BuildRaytracingAccelerationStructureInputs sphereBlasInputs = default;
        if (_sphereCount > 0)
        {
            sphereBlasInputs = SphereBlasInputs();
            RaytracingAccelerationStructurePrebuildInfo sBuild =
                _device.GetRaytracingAccelerationStructurePrebuildInfo(sphereBlasInputs);
            _sphereBlasScratch = CreateUavBuffer(sBuild.ScratchDataSizeInBytes, ResourceStates.UnorderedAccess);
            _sphereBlas = CreateUavBuffer(sBuild.ResultDataMaxSizeInBytes, ResourceStates.RaytracingAccelerationStructure);
        }

        var identity = new Matrix3x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0);
        var instances = new List<RaytracingInstanceDescription>
        {
            new() { Transform = identity, InstanceMask = 0xFF, Flags = RaytracingInstanceFlags.None, AccelerationStructure = _blas.GPUVirtualAddress },
        };
        if (_sphereCount > 0)
            instances.Add(new RaytracingInstanceDescription
            {
                Transform = identity, InstanceMask = 0xFF, Flags = RaytracingInstanceFlags.None,
                AccelerationStructure = _sphereBlas.GPUVirtualAddress,
            });
        _instanceBuffer = CreateUploadBuffer(
            new ReadOnlySpan<RaytracingInstanceDescription>(instances.ToArray()),
            instances.Count * Unsafe.SizeOf<RaytracingInstanceDescription>());

        BuildRaytracingAccelerationStructureInputs tlasInputs = TlasInputs(instances.Count);
        RaytracingAccelerationStructurePrebuildInfo tlasPrebuild =
            _device.GetRaytracingAccelerationStructurePrebuildInfo(tlasInputs);

        _tlasScratch = CreateUavBuffer(tlasPrebuild.ScratchDataSizeInBytes, ResourceStates.UnorderedAccess);
        _tlas = CreateUavBuffer(tlasPrebuild.ResultDataMaxSizeInBytes, ResourceStates.RaytracingAccelerationStructure);

        _allocator.Reset();
        _commandList.Reset(_allocator, null);

        _commandList.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription
        {
            Inputs = blasInputs,
            ScratchAccelerationStructureData = _blasScratch.GPUVirtualAddress,
            DestinationAccelerationStructureData = _blas.GPUVirtualAddress,
        });
        _commandList.ResourceBarrierUnorderedAccessView(_blas);

        if (_sphereCount > 0)
        {
            _commandList.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription
            {
                Inputs = sphereBlasInputs,
                ScratchAccelerationStructureData = _sphereBlasScratch.GPUVirtualAddress,
                DestinationAccelerationStructureData = _sphereBlas.GPUVirtualAddress,
            });
            _commandList.ResourceBarrierUnorderedAccessView(_sphereBlas);
        }

        _commandList.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription
        {
            Inputs = tlasInputs,
            ScratchAccelerationStructureData = _tlasScratch.GPUVirtualAddress,
            DestinationAccelerationStructureData = _tlas.GPUVirtualAddress,
        });
        _commandList.ResourceBarrierUnorderedAccessView(_tlas);

        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        WaitForGpu();
    }

    // Inputs for the procedural-AABB sphere BLAS (§0.4). Reads the current _aabbBuffer/_sphereCount,
    // so it is reused both for the initial build and the per-frame refit (UpdateSpheres).
    private BuildRaytracingAccelerationStructureInputs SphereBlasInputs() => new()
    {
        Type = RaytracingAccelerationStructureType.BottomLevel,
        Layout = ElementsLayout.Array,
        Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace,
        DescriptorsCount = 1,
        GeometryDescriptions = new[]
        {
            new RaytracingGeometryDescription
            {
                Type = RaytracingGeometryType.ProceduralPrimitiveAabbs,
                Flags = RaytracingGeometryFlags.Opaque,
                AABBs = new RaytracingGeometryAabbsDescription
                {
                    AABBCount = (ulong)_sphereCount,
                    AABBs = new GpuVirtualAddressAndStride(_aabbBuffer.GPUVirtualAddress, 6 * sizeof(float)),
                },
            },
        },
    };

    // Inputs for the TLAS over the current _instanceBuffer (triangle BLAS, plus the sphere BLAS when
    // present). Reused by the initial build and the per-frame refit.
    private BuildRaytracingAccelerationStructureInputs TlasInputs(int instanceCount) => new()
    {
        Type = RaytracingAccelerationStructureType.TopLevel,
        Layout = ElementsLayout.Array,
        Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace,
        DescriptorsCount = (uint)instanceCount,
        InstanceDescriptions = _instanceBuffer.GPUVirtualAddress,
    };

    /// <summary>
    /// Moves the analytic spheres (drifting bubbles, §2.6) for the current frame: rewrites each
    /// sphere's centre/radius in the GpuSphere + procedural-AABB upload buffers and refits the sphere
    /// BLAS and the TLAS in place (the static triangle BLAS is untouched). A no-op when the scene has
    /// no spheres. The material/optical fields are preserved (only position + radius change).
    /// Call before rendering a frame with <c>moving: true</c> so the accumulator does not smear the
    /// motion. <paramref name="centers"/>/<paramref name="radii"/> must be in packed-sphere order.
    /// </summary>
    public void UpdateSpheres(ReadOnlySpan<Vector3> centers, ReadOnlySpan<float> radii)
    {
        if (_device is null || _sphereCount == 0)
            return;
        if (centers.Length != _sphereCount || radii.Length != _sphereCount)
            throw new ArgumentException($"Expected {_sphereCount} spheres, got {centers.Length}/{radii.Length}.");

        WaitForGpu(); // the previous frame may still be reading the sphere/AABB buffers + BLAS

        Span<GpuSphere> sphereDst = _sphereBuffer.Map<GpuSphere>(0, _sphereCount);
        Span<float> aabbDst = _aabbBuffer.Map<float>(0, _sphereCount * 6);
        for (int i = 0; i < _sphereCount; i++)
        {
            float r = MathF.Max(0f, radii[i]);
            GpuSphere s = _sphereData[i];
            s.CX = centers[i].X; s.CY = centers[i].Y; s.CZ = centers[i].Z; s.Radius = r;
            _sphereData[i] = s;
            sphereDst[i] = s;
            aabbDst[i * 6 + 0] = s.CX - r; aabbDst[i * 6 + 1] = s.CY - r; aabbDst[i * 6 + 2] = s.CZ - r;
            aabbDst[i * 6 + 3] = s.CX + r; aabbDst[i * 6 + 4] = s.CY + r; aabbDst[i * 6 + 5] = s.CZ + r;
        }
        _sphereBuffer.Unmap(0);
        _aabbBuffer.Unmap(0);

        // Refit the sphere BLAS (its AABBs moved) then the TLAS (its cached bounds for that instance
        // are now stale). Both rebuild in place — same addresses/sizes — so the instance buffer and
        // scratch/result buffers are reused as-is.
        _allocator.Reset();
        _commandList.Reset(_allocator, null);
        _commandList.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription
        {
            Inputs = SphereBlasInputs(),
            ScratchAccelerationStructureData = _sphereBlasScratch.GPUVirtualAddress,
            DestinationAccelerationStructureData = _sphereBlas.GPUVirtualAddress,
        });
        _commandList.ResourceBarrierUnorderedAccessView(_sphereBlas);
        _commandList.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription
        {
            Inputs = TlasInputs(2), // triangle instance + sphere instance
            ScratchAccelerationStructureData = _tlasScratch.GPUVirtualAddress,
            DestinationAccelerationStructureData = _tlas.GPUVirtualAddress,
        });
        _commandList.ResourceBarrierUnorderedAccessView(_tlas);
        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        WaitForGpu();
    }

    private ID3D12Resource CreateUploadBuffer<T>(ReadOnlySpan<T> data, int byteLength) where T : unmanaged
    {
        ID3D12Resource buffer = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload), HeapFlags.None,
            ResourceDescription.Buffer((ulong)byteLength), ResourceStates.GenericRead);

        Span<T> dest = buffer.Map<T>(0, data.Length);
        data.CopyTo(dest);
        buffer.Unmap(0);
        return buffer;
    }

    private ID3D12Resource CreateUploadBuffer<T>(T[] data, int byteLength) where T : unmanaged
        => CreateUploadBuffer<T>(new ReadOnlySpan<T>(data), byteLength);

    private ID3D12Resource CreateUavBuffer(ulong size, ResourceStates initialState)
    {
        return _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default), HeapFlags.None,
            ResourceDescription.Buffer(size, ResourceFlags.AllowUnorderedAccess), initialState);
    }

    // ── Swap chain ────────────────────────────────────────────────────

    private void CreateSwapChain(nint windowHandle)
    {
        var swapChainDesc = new SwapChainDescription1
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Format = BackBufferFormat,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = FrameCount,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore,
            Flags = SwapChainFlags.None,
        };

        using IDXGISwapChain1 swapChain1 =
            _factory.CreateSwapChainForHwnd(_queue, windowHandle, swapChainDesc);
        _factory.MakeWindowAssociation(windowHandle, WindowAssociationFlags.IgnoreAltEnter);
        _swapChain = swapChain1.QueryInterface<IDXGISwapChain3>();

        for (int i = 0; i < FrameCount; i++)
            _backBuffers[i] = _swapChain.GetBuffer<ID3D12Resource>((uint)i);
    }

    /// <summary>
    /// Resizes the render target and all size-dependent resources to
    /// <paramref name="width"/>×<paramref name="height"/>. Recreates the swap-chain
    /// back buffers (when windowed), rebuilds every per-pixel buffer, updates the
    /// camera aspect, and forces a hard accumulation reset on the next frame (a
    /// resized image is a fresh convergence). No-op for a zero/unchanged size.
    /// </summary>
    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (width == _width && height == _height) return;

        WaitForGpu();

        _width = width;
        _height = height;

        DisposeSizeDependentResources();

        if (_swapChain is not null)
        {
            for (int i = 0; i < FrameCount; i++)
            {
                _backBuffers[i]?.Dispose();
                _backBuffers[i] = null!;
            }
            _swapChain.ResizeBuffers(FrameCount, (uint)width, (uint)height, BackBufferFormat, SwapChainFlags.None);
            for (int i = 0; i < FrameCount; i++)
                _backBuffers[i] = _swapChain.GetBuffer<ID3D12Resource>((uint)i);
        }

        CreateSizeDependentResources();

        // Camera is a class with an init-only Aspect; rebuild it with the new ratio
        // (keeps the current position/rotation). In windowed mode the per-frame
        // SetCamera overrides this; it matters for the headless resize self-test.
        _camera = new Camera
        {
            Position = _camera.Position,
            Rotation = _camera.Rotation,
            Fov = _camera.Fov,
            Aspect = (float)width / height,
            ImgPlaneZ = _camera.ImgPlaneZ,
        };
        _hasPrevCamera = false;
        _historyIndex = 0;
        _forceReset = true;
    }

    // ── Rendering ─────────────────────────────────────────────────────

    public void SetCamera(Camera camera) => _camera = camera;

    /// <summary>Updates the animated rat's world position (plan §8); read by the resolve pass.</summary>
    public void SetRatPosition(Vector3 pos) => _ratPos = pos;

    /// <summary>Sets the build-in reveal height (plan §9.3): walls are culled above it. Pass a
    /// large value (or ≥ wall height) for a fully-built maze.</summary>
    public void SetRevealHeight(float height) => _revealHeight = height;

    /// <summary>Sets the outro fade level (plan §9.4): 0 = normal, 1 = fully black.</summary>
    public void SetFadeLevel(float level) => _fadeLevel = level;

    /// <summary>Sets the fog animation clock (seconds); see Phase 4.</summary>
    public void SetFogTime(float seconds) => _volTime = seconds;

    private void UpdateTraceConstants(bool reset, bool moving)
    {
        float tanHalfFov = MathF.Tan(_camera.Fov * 0.5f);
        Quaternion rot = _camera.Rotation;
        var constants = new TraceConstants
        {
            CamPosX = _camera.Position.X,
            CamPosY = _camera.Position.Y,
            CamPosZ = _camera.Position.Z,
            RotX = rot.X,
            RotY = rot.Y,
            RotZ = rot.Z,
            RotW = rot.W,
            TanHalfFov = tanHalfFov,
            AspectTanHalfFov = _camera.Aspect * tanHalfFov,
            InvWidth = 1f / _width,
            InvHeight = 1f / _height,
            ImgPlaneZ = _camera.ImgPlaneZ,
            DeterministicCorrection = _spectral.DeterministicCorrection,
            AmbientLevel = Phase2Reference.AmbientLevel,
            LightIntensity = Phase2Reference.LightIntensity,
            Width = (uint)_width,
            Height = (uint)_height,
            MaxSampleCount = _maxSampleCount,
            ResetFlag = reset ? 1u : 0u,
            NumPrimitives = (uint)_scene.QuadCount,
            SubPixelJitter = _subPixelJitter ? 1u : 0u,
            NumLights = (uint)_lights.Count,
            LightingMode = (uint)_lightingMode,
            SampleClamp = _sampleClamp,
            SoftResetFlag = moving ? 1u : 0u,
            MotionSampleCap = _motionSampleCap,
            VolEnabled = _volumetrics.EnableVolumetrics ? 1u : 0u,
            VolSmokeMode = (uint)_volumetrics.SmokeMode,
            VolMarchSteps = (uint)Math.Max(0, _volumetrics.MarchSteps),
            VolShadowStepInterval = (uint)Math.Max(0, _volumetrics.ShadowStepInterval),
            VolMaxMarchDistance = _volumetrics.MaxMarchDistance,
            VolSigmaScaleFog = _volumetrics.SigmaScaleFog,
            VolSigmaScaleGround = _volumetrics.SigmaScaleGround,
            VolAnisotropyG = _volumetrics.AnisotropyG,
            VolInscatterStrength = _volumetrics.InscatterStrength,
            VolEarlyOutTransmittance = _volumetrics.EarlyOutTransmittance,
            BiomeIndicator = _biomeIndicator ? 1u : 0u,
            BumpyWalls = _bumpyWalls ? 1u : 0u,
            RevealHeight = _revealHeight,
            VolTime = _volTime,
            RatPosX = _ratPos.X,
            RatPosY = _ratPos.Y,
            RatPosZ = _ratPos.Z,
            RatSize = 0.7f,
            ShowRat = _showRat ? 1u : 0u,
            RatLayer = (uint)DecalLayer.Rat,
            VolShadowTransmittance = _volumetrics.ShadowTransmittance ? 1u : 0u,
            CausticEnabled = _causticEnabled ? 1u : 0u,
            CausticMinCellX = _causticMinCell.X,
            CausticMinCellY = _causticMinCell.Y,
            CausticMinCellZ = _causticMinCell.Z,
            CausticDimX = _causticDim.X,
            CausticDimY = _causticDim.Y,
            CausticDimZ = _causticDim.Z,
            CausticCellSize = _causticCellSize,
            CausticRadius = _causticRadius,
            CausticStrength = _causticStrength,
            ShadowTransmitTriangles = _shadowTransmitTriangles ? 1u : 0u,
        };

        Span<TraceConstants> dest = _traceConstantBuffer.Map<TraceConstants>(0, 1);
        dest[0] = constants;
        _traceConstantBuffer.Unmap(0);
    }

    private void UpdateResolveConstants(bool moving)
    {
        float tanHalfFov = MathF.Tan(_camera.Fov * 0.5f);
        float sampleCountNorm = Math.Max(1f, _lastStats.MaxObservedSampleCount);
        float varianceNorm = Phase5Reference.VarianceViewNorm(_lastStats.AverageVariance);

        // Player's minimap grid cell (odd/odd = cell interior in the 2N+1 wall bitmap).
        int cellX = (int)MathF.Floor(_camera.Position.X / MazeGeometryBuilder.CellSize);
        int cellY = (int)MathF.Floor(_camera.Position.Z / MazeGeometryBuilder.CellSize);
        uint playerGx = (uint)Math.Clamp(2 * cellX + 1, 0, (int)_mazeGw - 1);
        uint playerGy = (uint)Math.Clamp(2 * cellY + 1, 0, (int)_mazeGh - 1);

        var constants = new ResolveConstants
        {
            PrevCamPosX = _prevCamPos.X,
            PrevCamPosY = _prevCamPos.Y,
            PrevCamPosZ = _prevCamPos.Z,
            PrevRotX = _prevCamRot.X,
            PrevRotY = _prevCamRot.Y,
            PrevRotZ = _prevCamRot.Z,
            PrevRotW = _prevCamRot.W,
            TanHalfFov = tanHalfFov,
            AspectTanHalfFov = _camera.Aspect * tanHalfFov,
            TemporalBlendAlpha = _temporalBlendAlpha,
            ReprojThreshold = moving ? Phase3Reference.ReprojThresholdMoving : Phase3Reference.ReprojThresholdStill,
            Width = (uint)_width,
            Height = (uint)_height,
            UseTaa = _hasPrevCamera ? 1u : 0u,
            IsMoving = moving ? 1u : 0u,
            FilterRadius = _filterRadius,
            DebugMode = (uint)DebugMode,
            VarianceNorm = varianceNorm,
            CurrCamPosX = _camera.Position.X,
            CurrCamPosY = _camera.Position.Y,
            CurrCamPosZ = _camera.Position.Z,
            SampleCountNorm = sampleCountNorm,
            ShowMap = _showOverheadMap ? 1u : 0u,
            MazeGw = _mazeGw,
            MazeGh = _mazeGh,
            PlayerGx = playerGx,
            PlayerGy = playerGy,
            CurrRotX = _camera.Rotation.X,
            CurrRotY = _camera.Rotation.Y,
            CurrRotZ = _camera.Rotation.Z,
            CurrRotW = _camera.Rotation.W,
            RatPosX = _ratPos.X,
            RatPosY = _ratPos.Y,
            RatPosZ = _ratPos.Z,
            RatSize = 0.7f,
            ShowRat = _showRat ? 1u : 0u,
            ClassicDepthCue = _classicDepthCue,
            RatLayer = (uint)DecalLayer.Rat,
            FadeLevel = _fadeLevel,
        };

        Span<ResolveConstants> dest = _resolveConstantBuffer.Map<ResolveConstants>(0, 1);
        dest[0] = constants;
        _resolveConstantBuffer.Unmap(0);
    }

    private void RecordTrace()
    {
        _commandList.SetComputeRootSignature(_traceRootSignature);
        _commandList.SetPipelineState(_tracePipeline);
        _commandList.SetComputeRootShaderResourceView(0, _tlas.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(1, _primitiveBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(2, _deterXyzBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(3, _materialReflectanceBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(4, _lightBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(5, _accumBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(6, _sampleCountBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(7, _wavelengthCounterBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(8, _lastHitBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(9, _directAccumBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(10, _indirectAccumBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(11, _hitPointBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(12, _normalBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootConstantBufferView(13, _traceConstantBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(14, _lightColorBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(15, _fogBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(16, _lumaM2Buffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(17, _clampAmountBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(18, _clampHitFrameBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(19, _rgbBasisBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(20, _decalBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(21, _sphereBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(22, _causticPhotonBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(23, _causticCellBuffer.GPUVirtualAddress);

        uint groupsX = (uint)((_width + 7) / 8);
        uint groupsY = (uint)((_height + 7) / 8);
        _commandList.Dispatch(groupsX, groupsY, 1);
    }

    private void RecordResolve()
    {
        int cur = _historyIndex;
        int next = _historyIndex ^ 1;

        _commandList.SetDescriptorHeaps(_uavHeap);
        _commandList.SetComputeRootSignature(_resolveRootSignature);
        _commandList.SetPipelineState(_resolvePipeline);
        _commandList.SetComputeRootDescriptorTable(0, _uavHeap.GetGPUDescriptorHandleForHeapStart());
        _commandList.SetComputeRootUnorderedAccessView(1, _accumBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(2, _lastHitBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(3, _hitPointBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(4, _normalBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(5, _fogBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(6, _historyXyz[cur].GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(7, _historyHit[cur].GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(8, _historyValid[cur].GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(9, _historyXyz[next].GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(10, _historyHit[next].GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(11, _historyValid[next].GPUVirtualAddress);
        _commandList.SetComputeRootConstantBufferView(12, _resolveConstantBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(13, _sampleCountBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(14, _directAccumBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(15, _indirectAccumBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(16, _lumaM2Buffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(17, _clampAmountBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(18, _historyWeightBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(19, _rejectedBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(20, _mazeGridBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(21, _decalBuffer.GPUVirtualAddress);

        uint groupsX = (uint)((_width + 7) / 8);
        uint groupsY = (uint)((_height + 7) / 8);
        _commandList.Dispatch(groupsX, groupsY, 1);
    }

    private void RecordReduce()
    {
        _commandList.SetComputeRootSignature(_reduceRootSignature);
        _commandList.SetPipelineState(_reducePipeline);
        _commandList.SetComputeRootUnorderedAccessView(0, _sampleCountBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(1, _lumaM2Buffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(2, _lastHitBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(3, _clampHitFrameBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(4, _historyWeightBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(5, _rejectedBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(6, _statsBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootConstantBufferView(7, _reduceConstantBuffer.GPUVirtualAddress);
        _commandList.Dispatch(1, 1, 1); // single group strides over all pixels
    }

    // §1.5 retro pixelation post-pass: replaces each pixel with its block-centre pixel in place
    // on Output (see PixelatePhase6.hlsl for why the in-place read/write is race-free). Only
    // recorded when _pixelSize > 1.
    private void RecordPixelate()
    {
        _commandList.SetDescriptorHeaps(_uavHeap);
        _commandList.SetComputeRootSignature(_pixelateRootSignature);
        _commandList.SetPipelineState(_pixelatePipeline);
        _commandList.SetComputeRootDescriptorTable(0, _uavHeap.GetGPUDescriptorHandleForHeapStart());
        _commandList.SetComputeRoot32BitConstant(1, (uint)_width, 0);
        _commandList.SetComputeRoot32BitConstant(1, (uint)_height, 1);
        _commandList.SetComputeRoot32BitConstant(1, (uint)_pixelSize, 2);

        uint groupsX = (uint)((_width + 7) / 8);
        uint groupsY = (uint)((_height + 7) / 8);
        _commandList.Dispatch(groupsX, groupsY, 1);
    }

    private void RecordTraceResolveReduce()
    {
        RecordTrace();

        _commandList.ResourceBarrierUnorderedAccessView(_accumBuffer);
        _commandList.ResourceBarrierUnorderedAccessView(_directAccumBuffer);
        _commandList.ResourceBarrierUnorderedAccessView(_indirectAccumBuffer);
        _commandList.ResourceBarrierUnorderedAccessView(_sampleCountBuffer);
        _commandList.ResourceBarrierUnorderedAccessView(_lastHitBuffer);
        _commandList.ResourceBarrierUnorderedAccessView(_hitPointBuffer);
        _commandList.ResourceBarrierUnorderedAccessView(_normalBuffer);
        _commandList.ResourceBarrierUnorderedAccessView(_fogBuffer);
        _commandList.ResourceBarrierUnorderedAccessView(_lumaM2Buffer);
        _commandList.ResourceBarrierUnorderedAccessView(_clampAmountBuffer);
        _commandList.ResourceBarrierUnorderedAccessView(_clampHitFrameBuffer);

        RecordResolve();

        // §1.5 retro pixelation: chunk the freshly resolved Output in place. Skipped entirely
        // when off (block ≤ 1), so the presented image is bit-identical to the resolve result.
        if (_pixelSize > 1)
        {
            _commandList.ResourceBarrierUnorderedAccessView(_outputTexture);
            RecordPixelate();
            _commandList.ResourceBarrierUnorderedAccessView(_outputTexture);
        }

        _commandList.ResourceBarrierUnorderedAccessView(_historyWeightBuffer);
        _commandList.ResourceBarrierUnorderedAccessView(_rejectedBuffer);

        RecordReduce();

        _commandList.ResourceBarrierUnorderedAccessView(_statsBuffer);
        _commandList.ResourceBarrierTransition(_statsBuffer, ResourceStates.UnorderedAccess, ResourceStates.CopySource);
        _commandList.CopyBufferRegion(_statsReadback, 0, _statsBuffer, 0, 8 * sizeof(float));
        _commandList.ResourceBarrierTransition(_statsBuffer, ResourceStates.CopySource, ResourceStates.UnorderedAccess);
    }

    private void AdvanceFrameState()
    {
        _historyIndex ^= 1;
        _prevCamPos = _camera.Position;
        _prevCamRot = _camera.Rotation;
        _hasPrevCamera = true;
    }

    /// <summary>
    /// Traces + resolves one frame and presents it. Returns <c>false</c> if the
    /// device was lost (a <c>Present</c> failure or a GPU-wait timeout with a
    /// device-removed reason) so the caller can invoke <see cref="TryRecoverDevice"/>;
    /// returns <c>true</c> on a normal present.
    /// </summary>
    public bool RenderFrame(bool reset, bool moving)
    {
        if (_swapChain is null)
            throw new InvalidOperationException("RenderFrame requires a swap chain (windowed mode).");

        bool doReset = reset || _forceReset;
        UpdateTraceConstants(doReset, moving);
        UpdateResolveConstants(moving);

        int backIndex = (int)_swapChain.CurrentBackBufferIndex;
        ID3D12Resource backBuffer = _backBuffers[backIndex];

        _allocator.Reset();
        _commandList.Reset(_allocator, _tracePipeline);

        RecordTraceResolveReduce();

        _commandList.ResourceBarrierTransition(_outputTexture, ResourceStates.UnorderedAccess, ResourceStates.CopySource);
        _commandList.ResourceBarrierTransition(backBuffer, ResourceStates.Present, ResourceStates.CopyDest);
        _commandList.CopyResource(backBuffer, _outputTexture);
        _commandList.ResourceBarrierTransition(backBuffer, ResourceStates.CopyDest, ResourceStates.Present);
        _commandList.ResourceBarrierTransition(_outputTexture, ResourceStates.CopySource, ResourceStates.UnorderedAccess);

        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);

        if (_swapChain.Present(1, PresentFlags.None).Failure)
            return false;
        if (!WaitForGpu())
            return false;

        _forceReset = false;
        ReadbackStats();
        AdvanceFrameState();
        return true;
    }

    /// <summary>Traces + resolves one headless frame (no swap chain). Call
    /// <see cref="ReadbackOutput"/> afterwards to fetch the resolved image.</summary>
    public void RenderHeadlessFrame(bool reset, bool moving)
    {
        bool doReset = reset || _forceReset;
        UpdateTraceConstants(doReset, moving);
        UpdateResolveConstants(moving);

        _allocator.Reset();
        _commandList.Reset(_allocator, _tracePipeline);
        RecordTraceResolveReduce();
        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        WaitForGpu();

        _forceReset = false;
        ReadbackStats();
        AdvanceFrameState();
    }

    /// <summary>
    /// Best-effort device-removed recovery (pitfall P10/P12): tears the whole GPU
    /// stack down and re-runs <see cref="Initialize"/> on the stored window handle.
    /// The scene / spectral / light data the renderer holds is enough to rebuild
    /// everything. Returns <c>true</c> if a fresh device came up; the next frame is
    /// forced to hard-reset accumulation.
    /// </summary>
    public bool TryRecoverDevice()
    {
        try
        {
            nint handle = _windowHandle;
            _hasPrevCamera = false;
            _historyIndex = 0;
            _lastStats = default;
            DisposeGpuResources();
            Initialize(handle);
            _forceReset = true;
            return _device is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Swaps in a freshly generated maze scene and rebuilds only the scene-dependent GPU
    /// resources — vertex / index / primitive buffers, the spectral + light tables, and
    /// the BLAS/TLAS (plan §9.2). Everything else (device, pipelines, swap chain, per-pixel
    /// buffers) is untouched, so there is no shader recompile or device recreation and the
    /// transition is cheap. The scene buffers and the TLAS are bound by GPU virtual address
    /// each frame (root descriptors), so the next <see cref="RecordTrace"/> picks up the new
    /// resources automatically. Accumulation + temporal history hard-reset — a new maze is a
    /// fresh convergence. Returns false (and leaves the renderer usable) on failure.
    /// </summary>
    public bool RebuildScene(PackedScene scene, SpectralResources spectral, PackedLights lights, Camera camera)
    {
        if (_device is null)
            return false;
        try
        {
            WaitForGpu(); // drain in-flight frames still referencing the old scene buffers

            _instanceBuffer?.Dispose();
            _tlasScratch?.Dispose();
            _blasScratch?.Dispose();
            _tlas?.Dispose();
            _blas?.Dispose();
            _lightColorBuffer?.Dispose();
            _lightBuffer?.Dispose();
            _materialReflectanceBuffer?.Dispose();
            _deterXyzBuffer?.Dispose();
            _primitiveBuffer?.Dispose();
            _indexBuffer?.Dispose();
            _vertexBuffer?.Dispose();

            _scene = scene;
            _shadowTransmitTriangles = HasTransmissiveTriangles(scene);
            _spectral = spectral;
            _lights = lights;
            _camera = camera;

            CreateSceneResources();
            CreateSpectralResources();
            CreateLightResources();
            BuildAccelerationStructures();

            _hasPrevCamera = false;
            _historyIndex = 0;
            _lastStats = default;
            _forceReset = true; // next frame hard-clears accumulation
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ReadbackStats()
    {
        Span<float> s = _statsReadback.Map<float>(0, 8);
        _lastStats = new Phase5Stats(
            AverageVariance: s[0],
            AverageEffectiveSpp: s[1],
            ClampedPixelPercent: s[2],
            RejectedHistoryPercent: s[3],
            AverageHistoryWeight: s[4],
            HitPixelPercent: s[5],
            MaxObservedSampleCount: (uint)MathF.Round(s[6]));
        _statsReadback.Unmap(0);
    }

    /// <summary>
    /// Copies the resolved output texture back as tightly-packed RGBA8 rows
    /// (width*height*4 bytes).
    /// </summary>
    public byte[] ReadbackOutput()
    {
        _allocator.Reset();
        _commandList.Reset(_allocator, null);
        _commandList.ResourceBarrierTransition(_outputTexture, ResourceStates.UnorderedAccess, ResourceStates.CopySource);

        var texDesc = _outputTexture.Description;
        var layouts = new PlacedSubresourceFootPrint[1];
        var numRows = new uint[1];
        var rowSizes = new ulong[1];
        _device.GetCopyableFootprints(texDesc, 0, 1, 0, layouts, numRows, rowSizes, out ulong totalBytes);
        PlacedSubresourceFootPrint footprint = layouts[0];

        ID3D12Resource readback = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Readback), HeapFlags.None,
            ResourceDescription.Buffer(totalBytes), ResourceStates.CopyDest);

        _commandList.CopyTextureRegion(
            new TextureCopyLocation(readback, footprint), 0, 0, 0,
            new TextureCopyLocation(_outputTexture, 0), null);
        _commandList.ResourceBarrierTransition(_outputTexture, ResourceStates.CopySource, ResourceStates.UnorderedAccess);

        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        WaitForGpu();

        int rowPitch = (int)footprint.Footprint.RowPitch;
        Span<byte> mapped = readback.Map<byte>(0, (int)totalBytes);
        var packed = new byte[_width * _height * 4];
        for (int y = 0; y < _height; y++)
            mapped.Slice(y * rowPitch, _width * 4).CopyTo(packed.AsSpan(y * _width * 4));
        readback.Unmap(0);
        readback.Dispose();
        return packed;
    }

    // Waits for the GPU to drain the queue. Returns false if the device appears lost
    // (Signal failed, the wait timed out, or the device-removed reason is a failure),
    // so the windowed caller can trigger recovery instead of hanging on a dead device.
    private bool WaitForGpu()
    {
        ulong value = ++_fenceValue;
        if (_queue.Signal(_fence, value).Failure)
            return false;
        if (_fence.CompletedValue < value)
        {
            _fence.SetEventOnCompletion(value, _fenceEvent);
            if (!_fenceEvent.WaitOne(TimeSpan.FromSeconds(5)))
                return _device.DeviceRemovedReason.Success;
        }
        return _device.DeviceRemovedReason.Success;
    }

    // Disposes the whole GPU stack (everything except the managed _fenceEvent, which
    // is reused across a device-removed recovery). Shared by Dispose and recovery.
    private void DisposeGpuResources()
    {
        foreach (var bb in _backBuffers)
            bb?.Dispose();
        for (int i = 0; i < FrameCount; i++)
            _backBuffers[i] = null!;
        _swapChain?.Dispose(); _swapChain = null;
        _instanceBuffer?.Dispose();
        _tlasScratch?.Dispose();
        _blasScratch?.Dispose();
        _tlas?.Dispose();
        _blas?.Dispose();
        _reduceConstantBuffer?.Dispose();
        _resolveConstantBuffer?.Dispose();
        _traceConstantBuffer?.Dispose();
        DisposeSizeDependentResources();
        _pixelatePipeline?.Dispose();
        _pixelateRootSignature?.Dispose();
        _reducePipeline?.Dispose();
        _reduceRootSignature?.Dispose();
        _statsReadback?.Dispose();
        _statsBuffer?.Dispose();
        _causticPhotonBuffer?.Dispose();
        _causticCellBuffer?.Dispose();
        _mazeGridBuffer?.Dispose();
        _decalBuffer?.Dispose();
        _rgbBasisBuffer?.Dispose();
        _lightColorBuffer?.Dispose();
        _lightBuffer?.Dispose();
        _materialReflectanceBuffer?.Dispose();
        _deterXyzBuffer?.Dispose();
        _primitiveBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _vertexBuffer?.Dispose();
        _resolvePipeline?.Dispose();
        _resolveRootSignature?.Dispose();
        _tracePipeline?.Dispose();
        _traceRootSignature?.Dispose();
        _commandList?.Dispose();
        _allocator?.Dispose();
        _fence?.Dispose();
        _queue?.Dispose();
        _device?.Dispose(); _device = null!;
        _factory?.Dispose();
    }

    public void Dispose()
    {
        if (_device is not null)
            WaitForGpu();
        DisposeGpuResources();
        _fenceEvent.Dispose();
    }
}
