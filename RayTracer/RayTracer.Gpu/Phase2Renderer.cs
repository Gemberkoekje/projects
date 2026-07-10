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
/// Phase 2 renderer: spectral path tracing of the real maze scene <b>with
/// lighting</b> (NEE direct + one/tertiary-bounce indirect) on the GPU. It
/// extends the proven Phase 1 resource pattern (see <see cref="Phase1Renderer"/>)
/// with a light buffer, direct/indirect accumulation buffers, and the lighting
/// constants, dispatching <c>PathTracePhase2.hlsl</c> once per frame.
///
/// Kept as a separate renderer so Phase 1's fullbright path stays untouched;
/// the shared D3D12 scaffolding is intentionally duplicated rather than
/// refactoring the still-to-be-dev-box-validated Phase 1 host.
/// </summary>
internal sealed class Phase2Renderer : IDisposable
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

    private Camera _camera;

    private IDXGIFactory4 _factory = null!;
    private ID3D12Device5 _device = null!;
    private ID3D12CommandQueue _queue = null!;
    private ID3D12CommandAllocator _allocator = null!;
    private ID3D12GraphicsCommandList4 _commandList = null!;

    private ID3D12Fence _fence = null!;
    private ulong _fenceValue;
    private readonly AutoResetEvent _fenceEvent = new(false);

    private ID3D12RootSignature _rootSignature = null!;
    private ID3D12PipelineState _pipelineState = null!;
    private ID3D12DescriptorHeap _uavHeap = null!;
    private ID3D12Resource _outputTexture = null!;

    // Scene / spectral / light / accumulation resources.
    private ID3D12Resource _vertexBuffer = null!;
    private ID3D12Resource _indexBuffer = null!;
    private ID3D12Resource _primitiveBuffer = null!;
    private ID3D12Resource _deterXyzBuffer = null!;
    private ID3D12Resource _materialReflectanceBuffer = null!;
    private ID3D12Resource _lightBuffer = null!;
    private ID3D12Resource _accumBuffer = null!;
    private ID3D12Resource _directAccumBuffer = null!;
    private ID3D12Resource _indirectAccumBuffer = null!;
    private ID3D12Resource _sampleCountBuffer = null!;
    private ID3D12Resource _wavelengthCounterBuffer = null!;
    private ID3D12Resource _lastHitBuffer = null!;
    private ID3D12Resource _constantBuffer = null!;

    private ID3D12Resource _blas = null!;
    private ID3D12Resource _tlas = null!;
    private ID3D12Resource _blasScratch = null!;
    private ID3D12Resource _tlasScratch = null!;
    private ID3D12Resource _instanceBuffer = null!;

    private IDXGISwapChain3? _swapChain;
    private readonly ID3D12Resource[] _backBuffers = new ID3D12Resource[FrameCount];

    public string AdapterName { get; private set; } = "unknown";

    public Phase2Renderer(
        int width, int height,
        PackedScene scene, SpectralResources spectral, PackedLights lights, Camera camera,
        LightingMode lightingMode = LightingMode.NEE, float sampleClamp = 0f,
        uint maxSampleCount = 2048, bool subPixelJitter = true)
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
    }

    // ── Constant buffer layout (matches PathTracePhase2.hlsl's cbuffer) ─

    [StructLayout(LayoutKind.Sequential)]
    private struct Phase2Constants
    {
        public float CamPosX, CamPosY, CamPosZ, Pad0;
        public float RotX, RotY, RotZ, RotW;
        public float TanHalfFov, AspectTanHalfFov, InvWidth, InvHeight;
        public float ImgPlaneZ, DeterministicCorrection, AmbientLevel, LightIntensity;
        public uint Width, Height, MaxSampleCount, ResetFlag;
        public uint NumPrimitives, SubPixelJitter, NumLights, LightingMode;
        public float SampleClamp, Pad1, Pad2, Pad3;
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
        CreatePipeline();
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
        // Always allocate at least one float4 so the SRV is valid even for a
        // lightless scene (the shader gates lighting on NumLights > 0 anyway).
        float[] data = _lights.Count > 0 ? _lights.Data : new float[4];
        _lightBuffer = CreateUploadBuffer<float>(data, data.Length * sizeof(float));
    }

    private void CreateAccumulationResources()
    {
        int pixels = _width * _height;
        // float4 per pixel for each running-mean accumulator.
        _accumBuffer = CreateUavBuffer((ulong)(pixels * 4 * sizeof(float)), ResourceStates.UnorderedAccess);
        _directAccumBuffer = CreateUavBuffer((ulong)(pixels * 4 * sizeof(float)), ResourceStates.UnorderedAccess);
        _indirectAccumBuffer = CreateUavBuffer((ulong)(pixels * 4 * sizeof(float)), ResourceStates.UnorderedAccess);
        _sampleCountBuffer = CreateUavBuffer((ulong)(pixels * sizeof(uint)), ResourceStates.UnorderedAccess);
        _wavelengthCounterBuffer = CreateUavBuffer((ulong)(pixels * sizeof(uint)), ResourceStates.UnorderedAccess);
        _lastHitBuffer = CreateUavBuffer((ulong)(pixels * sizeof(uint)), ResourceStates.UnorderedAccess);

        // Constant buffers must be 256-byte aligned.
        _constantBuffer = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload), HeapFlags.None,
            ResourceDescription.Buffer(256), ResourceStates.GenericRead);
    }

    private void CreatePipeline()
    {
        var outputTable = new DescriptorRange1(DescriptorRangeType.UnorderedAccessView, 1, 3); // u3
        var rootParameters = new[]
        {
            new RootParameter1(new RootDescriptorTable1(outputTable), ShaderVisibility.All),                       // 0: Output (u3)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(0, 0), ShaderVisibility.All),  // 1: TLAS (t0)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(1, 0), ShaderVisibility.All),  // 2: Primitives (t1)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(2, 0), ShaderVisibility.All),  // 3: DeterXYZ (t2)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(3, 0), ShaderVisibility.All),  // 4: MaterialReflectance (t3)
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(4, 0), ShaderVisibility.All),  // 5: Lights (t4)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(0, 0), ShaderVisibility.All), // 6: Accum (u0)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(1, 0), ShaderVisibility.All), // 7: SampleCount (u1)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(2, 0), ShaderVisibility.All), // 8: WavelengthCounter (u2)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(4, 0), ShaderVisibility.All), // 9: LastHit (u4)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(5, 0), ShaderVisibility.All), // 10: DirectAccum (u5)
            new RootParameter1(RootParameterType.UnorderedAccessView, new RootDescriptor1(6, 0), ShaderVisibility.All), // 11: IndirectAccum (u6)
            new RootParameter1(RootParameterType.ConstantBufferView, new RootDescriptor1(0, 0), ShaderVisibility.All),  // 12: Constants (b0)
        };

        var rootSignatureDesc = new RootSignatureDescription1(RootSignatureFlags.None, rootParameters);
        _rootSignature = _device.CreateRootSignature(rootSignatureDesc);

        string hlsl = LoadShaderSource();
        byte[] bytecode = ShaderCompiler.CompileCompute(hlsl, "CSMain", "PathTracePhase2.hlsl");

        var psoDesc = new ComputePipelineStateDescription
        {
            RootSignature = _rootSignature,
            ComputeShader = bytecode,
        };
        _pipelineState = _device.CreateComputePipelineState(psoDesc);
    }

    private static string LoadShaderSource()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Shaders", "PathTracePhase2.hlsl");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Shader not found at '{path}'.");
        return File.ReadAllText(path);
    }

    // ── Acceleration structures ───────────────────────────────────────

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

    private void UpdateConstants(bool reset)
    {
        float tanHalfFov = MathF.Tan(_camera.Fov * 0.5f);
        Quaternion rot = _camera.Rotation;
        var constants = new Phase2Constants
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
        };

        Span<Phase2Constants> dest = _constantBuffer.Map<Phase2Constants>(0, 1);
        dest[0] = constants;
        _constantBuffer.Unmap(0);
    }

    private void RecordTrace()
    {
        _commandList.SetDescriptorHeaps(_uavHeap);
        _commandList.SetComputeRootSignature(_rootSignature);
        _commandList.SetPipelineState(_pipelineState);
        _commandList.SetComputeRootDescriptorTable(0, _uavHeap.GetGPUDescriptorHandleForHeapStart());
        _commandList.SetComputeRootShaderResourceView(1, _tlas.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(2, _primitiveBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(3, _deterXyzBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(4, _materialReflectanceBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootShaderResourceView(5, _lightBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(6, _accumBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(7, _sampleCountBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(8, _wavelengthCounterBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(9, _lastHitBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(10, _directAccumBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootUnorderedAccessView(11, _indirectAccumBuffer.GPUVirtualAddress);
        _commandList.SetComputeRootConstantBufferView(12, _constantBuffer.GPUVirtualAddress);

        uint groupsX = (uint)((_width + 7) / 8);
        uint groupsY = (uint)((_height + 7) / 8);
        _commandList.Dispatch(groupsX, groupsY, 1);
    }

    /// <summary>Traces one accumulation frame and presents it. <paramref name="reset"/>
    /// clears the running mean (use after a camera change).</summary>
    public void RenderFrame(bool reset)
    {
        if (_swapChain is null)
            throw new InvalidOperationException("RenderFrame requires a swap chain (windowed mode).");

        UpdateConstants(reset);

        int backIndex = (int)_swapChain.CurrentBackBufferIndex;
        ID3D12Resource backBuffer = _backBuffers[backIndex];

        _allocator.Reset();
        _commandList.Reset(_allocator, _pipelineState);

        RecordTrace();

        _commandList.ResourceBarrierTransition(_outputTexture, ResourceStates.UnorderedAccess, ResourceStates.CopySource);
        _commandList.ResourceBarrierTransition(backBuffer, ResourceStates.Present, ResourceStates.CopyDest);
        _commandList.CopyResource(backBuffer, _outputTexture);
        _commandList.ResourceBarrierTransition(backBuffer, ResourceStates.CopyDest, ResourceStates.Present);
        _commandList.ResourceBarrierTransition(_outputTexture, ResourceStates.CopySource, ResourceStates.UnorderedAccess);

        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        _swapChain.Present(1, PresentFlags.None);
        WaitForGpu();
    }

    /// <summary>
    /// Headless render of <paramref name="frames"/> accumulation passes; returns
    /// the resolved image as tightly-packed RGBA8 rows (width*height*4 bytes).
    /// </summary>
    public byte[] RenderHeadless(int frames)
    {
        for (int f = 0; f < frames; f++)
        {
            UpdateConstants(reset: f == 0);
            _allocator.Reset();
            _commandList.Reset(_allocator, _pipelineState);
            RecordTrace();
            _commandList.Close();
            _queue.ExecuteCommandList(_commandList);
            WaitForGpu();
        }

        return ReadbackOutput();
    }

    private byte[] ReadbackOutput()
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
        _constantBuffer?.Dispose();
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
        _pipelineState?.Dispose();
        _rootSignature?.Dispose();
        _commandList?.Dispose();
        _allocator?.Dispose();
        _fence?.Dispose();
        _fenceEvent.Dispose();
        _queue?.Dispose();
        _device?.Dispose();
        _factory?.Dispose();
    }
}
