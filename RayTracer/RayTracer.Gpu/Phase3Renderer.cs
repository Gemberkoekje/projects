using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RayTracer;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.Direct3D12.Debug;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace RayTracer.Gpu;

/// <summary>
/// Phase 3 renderer: the Phase 2 spectral path tracer wrapped in a two-pass
/// <b>temporal pipeline</b>. A trace pass (<c>PathTracePhase3.hlsl</c>) writes the
/// accumulation buffers plus a G-buffer (world hit point, oriented normal, hit
/// mask); a resolve pass (<c>ResolvePhase3.hlsl</c>) applies the bilateral spatial
/// filter, reprojects into the previous frame using ping-pong history textures,
/// and blends with disocclusion / normal rejection before writing the sRGB image.
///
/// Camera motion drives the accumulation lifecycle: while moving, a soft reset
/// caps each pixel's effective sample count and the resolve tightens its
/// reprojection threshold and enables the spatial filter (mirroring the CPU
/// renderer's <c>IsMoving</c> path). The resolve math is a line-for-line port of
/// <see cref="Phase3Reference"/>, pinned to <c>JobSystem.TaaResolver</c> by the
/// unit tests.
///
/// As with Phase 1/2 the D3D12 scaffolding is intentionally duplicated rather
/// than refactoring the still-to-be-dev-box-validated earlier hosts.
/// </summary>
internal sealed class Phase3Renderer : IDisposable
{
    private const int FrameCount = 2;
    private const Format BackBufferFormat = Format.R8G8B8A8_UNorm;

    private readonly int _width;
    private readonly int _height;
    private readonly PackedScene _scene;
    private readonly SpectralResources _spectral;
    private readonly PackedLights _lights;
    private readonly LightingMode _lightingMode;
    private readonly float _sampleClamp;
    private readonly uint _maxSampleCount;
    private readonly bool _subPixelJitter;
    private readonly uint _motionSampleCap;
    private readonly float _temporalBlendAlpha;
    private readonly uint _filterRadius;
    private readonly bool _edgeAwareFilter;

    private Camera _camera;

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
    private ID3D12Resource _deterXyzBuffer = null!;
    private ID3D12Resource _materialReflectanceBuffer = null!;
    private ID3D12Resource _lightBuffer = null!;

    // Accumulation + G-buffer.
    private ID3D12Resource _accumBuffer = null!;
    private ID3D12Resource _directAccumBuffer = null!;
    private ID3D12Resource _indirectAccumBuffer = null!;
    private ID3D12Resource _sampleCountBuffer = null!;
    private ID3D12Resource _wavelengthCounterBuffer = null!;
    private ID3D12Resource _lastHitBuffer = null!;
    private ID3D12Resource _hitPointBuffer = null!;
    private ID3D12Resource _normalBuffer = null!;

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

    private IDXGISwapChain3? _swapChain;
    private readonly ID3D12Resource[] _backBuffers = new ID3D12Resource[FrameCount];

    public string AdapterName { get; private set; } = "unknown";

    public Phase3Renderer(
        int width, int height,
        PackedScene scene, SpectralResources spectral, PackedLights lights, Camera camera,
        LightingMode lightingMode = LightingMode.NEE, float sampleClamp = 0f,
        uint maxSampleCount = 2048, bool subPixelJitter = true,
        uint motionSampleCap = 20, float temporalBlendAlpha = 0.1f,
        uint filterRadius = 1, bool edgeAwareFilter = false)
    {
        _width = width;
        _height = height;
        _scene = scene;
        _spectral = spectral;
        _lights = lights;
        _lightingMode = lightingMode;
        _sampleClamp = sampleClamp;
        _camera = camera;
        _maxSampleCount = maxSampleCount;
        _subPixelJitter = subPixelJitter;
        _motionSampleCap = motionSampleCap;
        _temporalBlendAlpha = temporalBlendAlpha;
        _filterRadius = filterRadius;
        _edgeAwareFilter = edgeAwareFilter;
    }

    // ── Constant buffer layouts ───────────────────────────────────────

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
        public float Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ResolveConstants
    {
        public float PrevCamPosX, PrevCamPosY, PrevCamPosZ, RPad0;
        public float PrevRotX, PrevRotY, PrevRotZ, PrevRotW;
        public float TanHalfFov, AspectTanHalfFov, TemporalBlendAlpha, ReprojThreshold;
        public uint Width, Height, UseTaa, IsMoving;
        public uint FilterRadius, EdgeAware, RPad1, RPad2;
    }

    // ── Initialization ────────────────────────────────────────────────

    public void Initialize(nint windowHandle)
    {
        bool debug = EnableDebugLayer();
        CreateDeviceAndQueue(debug);
        VerifyRaytracingSupport();
        CreateCommandObjects();
        CreateOutputResources();
        CreateSceneResources();
        CreateSpectralResources();
        CreateLightResources();
        CreateAccumulationResources();
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

    private void CreateOutputResources()
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
    }

    private void CreateSceneResources()
    {
        _vertexBuffer = CreateUploadBuffer<float>(_scene.Vertices, _scene.Vertices.Length * sizeof(float));
        _indexBuffer = CreateUploadBuffer<uint>(_scene.Indices, _scene.Indices.Length * sizeof(uint));
        int primBytes = _scene.Primitives.Length * Unsafe.SizeOf<GpuPrimitive>();
        _primitiveBuffer = CreateUploadBuffer<GpuPrimitive>(_scene.Primitives, primBytes);
    }

    private void CreateSpectralResources()
    {
        _deterXyzBuffer = CreateUploadBuffer<float>(_spectral.DeterXYZ, _spectral.DeterXYZ.Length * sizeof(float));
        _materialReflectanceBuffer = CreateUploadBuffer<float>(
            _spectral.MaterialReflectance, _spectral.MaterialReflectance.Length * sizeof(float));
    }

    private void CreateLightResources()
    {
        float[] data = _lights.Count > 0 ? _lights.Data : new float[4];
        _lightBuffer = CreateUploadBuffer<float>(data, data.Length * sizeof(float));
    }

    private void CreateAccumulationResources()
    {
        int pixels = _width * _height;
        ulong f4 = (ulong)(pixels * 4 * sizeof(float));
        ulong u1 = (ulong)(pixels * sizeof(uint));

        _accumBuffer = CreateUavBuffer(f4, ResourceStates.UnorderedAccess);
        _directAccumBuffer = CreateUavBuffer(f4, ResourceStates.UnorderedAccess);
        _indirectAccumBuffer = CreateUavBuffer(f4, ResourceStates.UnorderedAccess);
        _sampleCountBuffer = CreateUavBuffer(u1, ResourceStates.UnorderedAccess);
        _wavelengthCounterBuffer = CreateUavBuffer(u1, ResourceStates.UnorderedAccess);
        _lastHitBuffer = CreateUavBuffer(u1, ResourceStates.UnorderedAccess);
        _hitPointBuffer = CreateUavBuffer(f4, ResourceStates.UnorderedAccess);
        _normalBuffer = CreateUavBuffer(f4, ResourceStates.UnorderedAccess);

        for (int i = 0; i < 2; i++)
        {
            _historyXyz[i] = CreateUavBuffer(f4, ResourceStates.UnorderedAccess);
            _historyHit[i] = CreateUavBuffer(f4, ResourceStates.UnorderedAccess);
            _historyValid[i] = CreateUavBuffer(u1, ResourceStates.UnorderedAccess);
        }

        // Constant buffers must be 256-byte aligned.
        _traceConstantBuffer = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload), HeapFlags.None,
            ResourceDescription.Buffer(256), ResourceStates.GenericRead);
        _resolveConstantBuffer = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload), HeapFlags.None,
            ResourceDescription.Buffer(256), ResourceStates.GenericRead);
    }

    private void CreatePipelines()
    {
        // Trace pass: SRVs t0-t4, UAVs u0-u2/u4-u8, CBV b0. No Output (u3).
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
        };
        _traceRootSignature = _device.CreateRootSignature(
            new RootSignatureDescription1(RootSignatureFlags.None, traceParams));

        byte[] traceBytecode = ShaderCompiler.CompileCompute(
            LoadShaderSource("PathTracePhase3.hlsl"), "CSMain", "PathTracePhase3.hlsl");
        _tracePipeline = _device.CreateComputePipelineState(new ComputePipelineStateDescription
        {
            RootSignature = _traceRootSignature,
            ComputeShader = traceBytecode,
        });

        // Resolve pass: Output table (u3), input/output UAV buffers, CBV b0.
        var outputTable = new DescriptorRange1(DescriptorRangeType.UnorderedAccessView, 1, 3); // u3
        var resolveParams = new[]
        {
            new RootParameter1(new RootDescriptorTable1(outputTable), ShaderVisibility.All),                            // 0: Output (u3)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(0, 0), ShaderVisibility.All),  // 1: Accum (u0)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(4, 0), ShaderVisibility.All),  // 2: LastHit (u4)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(7, 0), ShaderVisibility.All),  // 3: HitPointIn (u7)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(8, 0), ShaderVisibility.All),  // 4: NormalIn (u8)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(10, 0), ShaderVisibility.All), // 5: HistoryXyzIn (u10)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(11, 0), ShaderVisibility.All), // 6: HistoryHitIn (u11)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(12, 0), ShaderVisibility.All), // 7: HistoryValidIn (u12)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(13, 0), ShaderVisibility.All), // 8: HistoryXyzOut (u13)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(14, 0), ShaderVisibility.All), // 9: HistoryHitOut (u14)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(15, 0), ShaderVisibility.All), // 10: HistoryValidOut (u15)
            new RootParameter1(RootParameterType.ConstantBufferView, new RootDescriptor1(0, 0), ShaderVisibility.All),   // 11: ResolveConstants (b0)
        };
        _resolveRootSignature = _device.CreateRootSignature(
            new RootSignatureDescription1(RootSignatureFlags.None, resolveParams));

        byte[] resolveBytecode = ShaderCompiler.CompileCompute(
            LoadShaderSource("ResolvePhase3.hlsl"), "CSMain", "ResolvePhase3.hlsl");
        _resolvePipeline = _device.CreateComputePipelineState(new ComputePipelineStateDescription
        {
            RootSignature = _resolveRootSignature,
            ComputeShader = resolveBytecode,
        });
    }

    private static string LoadShaderSource(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Shaders", fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Shader not found at '{path}'.");
        return File.ReadAllText(path);
    }

    // ── Acceleration structures (identical to Phase 1/2) ──────────────

    private void BuildAccelerationStructures()
    {
        int vertexCount = _scene.Vertices.Length / 3;
        int indexCount = _scene.Indices.Length;

        var geometryDesc = new RaytracingGeometryDescription
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
        };

        var blasInputs = new BuildRaytracingAccelerationStructureInputs
        {
            Type = RaytracingAccelerationStructureType.BottomLevel,
            Layout = ElementsLayout.Array,
            Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace,
            DescriptorsCount = 1,
            GeometryDescriptions = new[] { geometryDesc },
        };
        RaytracingAccelerationStructurePrebuildInfo blasPrebuild =
            _device.GetRaytracingAccelerationStructurePrebuildInfo(blasInputs);

        _blasScratch = CreateUavBuffer(blasPrebuild.ScratchDataSizeInBytes, ResourceStates.UnorderedAccess);
        _blas = CreateUavBuffer(blasPrebuild.ResultDataMaxSizeInBytes, ResourceStates.RaytracingAccelerationStructure);

        var instanceDesc = new RaytracingInstanceDescription
        {
            Transform = new Matrix3x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0),
            InstanceMask = 0xFF,
            Flags = RaytracingInstanceFlags.None,
            AccelerationStructure = _blas.GPUVirtualAddress,
        };
        _instanceBuffer = CreateUploadBuffer(
            MemoryMarshal.CreateReadOnlySpan(ref instanceDesc, 1),
            Unsafe.SizeOf<RaytracingInstanceDescription>());

        var tlasInputs = new BuildRaytracingAccelerationStructureInputs
        {
            Type = RaytracingAccelerationStructureType.TopLevel,
            Layout = ElementsLayout.Array,
            Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace,
            DescriptorsCount = 1,
            InstanceDescriptions = _instanceBuffer.GPUVirtualAddress,
        };
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

    // ── Rendering ─────────────────────────────────────────────────────

    public void SetCamera(Camera camera) => _camera = camera;

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
        };

        Span<TraceConstants> dest = _traceConstantBuffer.Map<TraceConstants>(0, 1);
        dest[0] = constants;
        _traceConstantBuffer.Unmap(0);
    }

    private void UpdateResolveConstants(bool moving)
    {
        float tanHalfFov = MathF.Tan(_camera.Fov * 0.5f);
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
            EdgeAware = _edgeAwareFilter ? 1u : 0u,
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
        _commandList.SetComputeRootUnorderedAccessView(5, _historyXyz[cur].GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(6, _historyHit[cur].GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(7, _historyValid[cur].GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(8, _historyXyz[next].GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(9, _historyHit[next].GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(10, _historyValid[next].GPUVirtualAddress);
        _commandList.SetComputeRootConstantBufferView(11, _resolveConstantBuffer.GPUVirtualAddress);

        uint groupsX = (uint)((_width + 7) / 8);
        uint groupsY = (uint)((_height + 7) / 8);
        _commandList.Dispatch(groupsX, groupsY, 1);
    }

    // Records trace → UAV sync → resolve into the current command list.
    private void RecordTraceAndResolve()
    {
        RecordTrace();

        // Make the trace pass's accumulation + G-buffer writes visible to the
        // resolve pass (they alias the same UAVs across two dispatches).
        _commandList.ResourceBarrierUnorderedAccessView(_accumBuffer);
        _commandList.ResourceBarrierUnorderedAccessView(_lastHitBuffer);
        _commandList.ResourceBarrierUnorderedAccessView(_hitPointBuffer);
        _commandList.ResourceBarrierUnorderedAccessView(_normalBuffer);

        RecordResolve();
    }

    // Advances the ping-pong selector and stores the camera the next frame's
    // reprojection will target.
    private void AdvanceFrameState()
    {
        _historyIndex ^= 1;
        _prevCamPos = _camera.Position;
        _prevCamRot = _camera.Rotation;
        _hasPrevCamera = true;
    }

    /// <summary>Traces + resolves one frame and presents it. <paramref name="reset"/>
    /// hard-clears the running mean; <paramref name="moving"/> engages the motion
    /// path (soft reset + spatial filter + tighter reprojection).</summary>
    public void RenderFrame(bool reset, bool moving)
    {
        if (_swapChain is null)
            throw new InvalidOperationException("RenderFrame requires a swap chain (windowed mode).");

        UpdateTraceConstants(reset, moving);
        UpdateResolveConstants(moving);

        int backIndex = (int)_swapChain.CurrentBackBufferIndex;
        ID3D12Resource backBuffer = _backBuffers[backIndex];

        _allocator.Reset();
        _commandList.Reset(_allocator, _tracePipeline);

        RecordTraceAndResolve();

        _commandList.ResourceBarrierTransition(_outputTexture, ResourceStates.UnorderedAccess, ResourceStates.CopySource);
        _commandList.ResourceBarrierTransition(backBuffer, ResourceStates.Present, ResourceStates.CopyDest);
        _commandList.CopyResource(backBuffer, _outputTexture);
        _commandList.ResourceBarrierTransition(backBuffer, ResourceStates.CopyDest, ResourceStates.Present);
        _commandList.ResourceBarrierTransition(_outputTexture, ResourceStates.CopySource, ResourceStates.UnorderedAccess);

        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        _swapChain.Present(1, PresentFlags.None);
        WaitForGpu();

        AdvanceFrameState();
    }

    /// <summary>Traces + resolves one headless frame (no swap chain). Call
    /// <see cref="ReadbackOutput"/> afterwards to fetch the resolved image.</summary>
    public void RenderHeadlessFrame(bool reset, bool moving)
    {
        UpdateTraceConstants(reset, moving);
        UpdateResolveConstants(moving);

        _allocator.Reset();
        _commandList.Reset(_allocator, _tracePipeline);
        RecordTraceAndResolve();
        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        WaitForGpu();

        AdvanceFrameState();
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

    private void WaitForGpu()
    {
        ulong value = ++_fenceValue;
        _queue.Signal(_fence, value);
        if (_fence.CompletedValue < value)
        {
            _fence.SetEventOnCompletion(value, _fenceEvent);
            _fenceEvent.WaitOne();
        }
    }

    public void Dispose()
    {
        if (_device is not null)
            WaitForGpu();

        foreach (var bb in _backBuffers)
            bb?.Dispose();
        _swapChain?.Dispose();
        _instanceBuffer?.Dispose();
        _tlasScratch?.Dispose();
        _blasScratch?.Dispose();
        _tlas?.Dispose();
        _blas?.Dispose();
        _resolveConstantBuffer?.Dispose();
        _traceConstantBuffer?.Dispose();
        for (int i = 0; i < 2; i++)
        {
            _historyValid[i]?.Dispose();
            _historyHit[i]?.Dispose();
            _historyXyz[i]?.Dispose();
        }
        _normalBuffer?.Dispose();
        _hitPointBuffer?.Dispose();
        _lastHitBuffer?.Dispose();
        _wavelengthCounterBuffer?.Dispose();
        _sampleCountBuffer?.Dispose();
        _indirectAccumBuffer?.Dispose();
        _directAccumBuffer?.Dispose();
        _accumBuffer?.Dispose();
        _lightBuffer?.Dispose();
        _materialReflectanceBuffer?.Dispose();
        _deterXyzBuffer?.Dispose();
        _primitiveBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _vertexBuffer?.Dispose();
        _outputTexture?.Dispose();
        _uavHeap?.Dispose();
        _resolvePipeline?.Dispose();
        _resolveRootSignature?.Dispose();
        _tracePipeline?.Dispose();
        _traceRootSignature?.Dispose();
        _commandList?.Dispose();
        _allocator?.Dispose();
        _fence?.Dispose();
        _fenceEvent.Dispose();
        _queue?.Dispose();
        _device?.Dispose();
        _factory?.Dispose();
    }
}
