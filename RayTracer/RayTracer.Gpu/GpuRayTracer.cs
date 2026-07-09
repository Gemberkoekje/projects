using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.Direct3D12.Debug;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace RayTracer.Gpu;

/// <summary>
/// Phase 0 spike renderer. Brings up a D3D12 device, verifies DXR 1.1
/// (inline ray tracing) support, builds a hardware acceleration structure for
/// two triangles, and dispatches an inline-RayQuery compute shader that writes
/// the result to an offscreen texture. The texture is either presented to a
/// swap chain (windowed) or read back and validated (headless self-test).
/// </summary>
internal sealed class GpuRayTracer : IDisposable
{
    private const int FrameCount = 2;
    private const Format BackBufferFormat = Format.R8G8B8A8_UNorm;

    private readonly int _width;
    private readonly int _height;

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

    private ID3D12Resource _vertexBuffer = null!;
    private ID3D12Resource _blas = null!;
    private ID3D12Resource _tlas = null!;
    // Scratch/instance resources are kept alive for the lifetime of the object
    // to avoid releasing them while the build is still referenced.
    private ID3D12Resource _blasScratch = null!;
    private ID3D12Resource _tlasScratch = null!;
    private ID3D12Resource _instanceBuffer = null!;

    // Swap chain is only created in windowed mode.
    private IDXGISwapChain3? _swapChain;
    private readonly ID3D12Resource[] _backBuffers = new ID3D12Resource[FrameCount];

    public string AdapterName { get; private set; } = "unknown";

    public GpuRayTracer(int width, int height)
    {
        _width = width;
        _height = height;
    }

    // ── Initialization ────────────────────────────────────────────────

    public void Initialize(nint windowHandle)
    {
        bool debug = EnableDebugLayer();
        CreateDeviceAndQueue(debug);
        VerifyRaytracingSupport();
        CreateCommandObjects();
        CreateOutputResources();
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

        // Prefer the high-performance GPU when the factory supports the query.
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
                "DXR 1.1 inline ray tracing (Tier 1.1) is required. " +
                "Update the driver, or run the CPU renderer instead.");
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
            BackBufferFormat,
            (uint)_width,
            (uint)_height,
            1,   // arraySize
            1,   // mipLevels
            1,   // sampleCount
            0,   // sampleQuality
            ResourceFlags.AllowUnorderedAccess);

        _outputTexture = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default),
            HeapFlags.None,
            texDesc,
            ResourceStates.UnorderedAccess);

        _uavHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            1,
            DescriptorHeapFlags.ShaderVisible));

        var uavDesc = new UnorderedAccessViewDescription
        {
            Format = BackBufferFormat,
            ViewDimension = UnorderedAccessViewDimension.Texture2D,
        };
        _device.CreateUnorderedAccessView(
            _outputTexture, null, uavDesc, _uavHeap.GetCPUDescriptorHandleForHeapStart());
    }

    private void CreatePipeline()
    {
        // Root layout: UAV table (u0), TLAS as a root SRV (t0), 4 root constants (b0).
        var uavRange = new DescriptorRange1(DescriptorRangeType.UnorderedAccessView, 1, 0);
        var rootParameters = new[]
        {
            new RootParameter1(new RootDescriptorTable1(uavRange), ShaderVisibility.All),
            new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(0, 0), ShaderVisibility.All),
            new RootParameter1(new RootConstants(0, 0, 4), ShaderVisibility.All),
        };

        var rootSignatureDesc = new RootSignatureDescription1(RootSignatureFlags.None, rootParameters);
        _rootSignature = _device.CreateRootSignature(rootSignatureDesc);

        string hlsl = LoadShaderSource();
        byte[] bytecode = ShaderCompiler.CompileCompute(hlsl, "CSMain", "RayQuery.hlsl");

        var psoDesc = new ComputePipelineStateDescription
        {
            RootSignature = _rootSignature,
            ComputeShader = bytecode,
        };
        _pipelineState = _device.CreateComputePipelineState(psoDesc);
    }

    private static string LoadShaderSource()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Shaders", "RayQuery.hlsl");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Shader not found at '{path}'.");
        return File.ReadAllText(path);
    }

    // ── Acceleration structures ───────────────────────────────────────

    private void BuildAccelerationStructures()
    {
        // Two triangles forming a quad on the z=0 plane, facing the camera.
        Vector3[] vertices =
        {
            new(-1.0f, -1.0f, 0.0f),
            new( 1.0f, -1.0f, 0.0f),
            new( 1.0f,  1.0f, 0.0f),

            new(-1.0f, -1.0f, 0.0f),
            new( 1.0f,  1.0f, 0.0f),
            new(-1.0f,  1.0f, 0.0f),
        };

        int vertexBytes = vertices.Length * Unsafe.SizeOf<Vector3>();
        _vertexBuffer = CreateUploadBuffer(vertices.AsSpan(), vertexBytes);

        var geometryDesc = new RaytracingGeometryDescription
        {
            Type = RaytracingGeometryType.Triangles,
            Flags = RaytracingGeometryFlags.Opaque,
            Triangles = new RaytracingGeometryTrianglesDescription
            {
                VertexBuffer = new GpuVirtualAddressAndStride(
                    _vertexBuffer.GPUVirtualAddress, (ulong)Unsafe.SizeOf<Vector3>()),
                VertexCount = (uint)vertices.Length,
                VertexFormat = Format.R32G32B32_Float,
                IndexBuffer = 0,
                IndexCount = 0,
                IndexFormat = Format.Unknown,
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

        // One instance referencing the BLAS with an identity transform.
        var instanceDesc = new RaytracingInstanceDescription
        {
            // Row-major 3x4 identity (InstanceID/ContributionToHitGroupIndex
            // default to 0, which is what we want for a single instance).
            Transform = new Matrix3x4(
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0),
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

        // Record and submit both builds.
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
            new HeapProperties(HeapType.Upload),
            HeapFlags.None,
            ResourceDescription.Buffer((ulong)byteLength),
            ResourceStates.GenericRead);

        Span<T> dest = buffer.Map<T>(0, data.Length);
        data.CopyTo(dest);
        buffer.Unmap(0);
        return buffer;
    }

    private ID3D12Resource CreateUavBuffer(ulong size, ResourceStates initialState)
    {
        return _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default),
            HeapFlags.None,
            ResourceDescription.Buffer(size, ResourceFlags.AllowUnorderedAccess),
            initialState);
    }

    // ── Swap chain (windowed) ─────────────────────────────────────────

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

    /// <summary>Traces one frame and presents it to the window.</summary>
    public void RenderFrame(uint frameCounter)
    {
        if (_swapChain is null)
            throw new InvalidOperationException("RenderFrame requires a swap chain (windowed mode).");

        int backIndex = (int)_swapChain.CurrentBackBufferIndex;
        ID3D12Resource backBuffer = _backBuffers[backIndex];

        _allocator.Reset();
        _commandList.Reset(_allocator, _pipelineState);

        RecordTrace(frameCounter);

        // Copy the traced image into the back buffer, then present.
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

    private void RecordTrace(uint frameCounter)
    {
        _commandList.SetDescriptorHeaps(_uavHeap);
        _commandList.SetComputeRootSignature(_rootSignature);
        _commandList.SetPipelineState(_pipelineState);
        _commandList.SetComputeRootDescriptorTable(0, _uavHeap.GetGPUDescriptorHandleForHeapStart());
        _commandList.SetComputeRootShaderResourceView(1, _tlas.GPUVirtualAddress);
        _commandList.SetComputeRoot32BitConstant(2, (uint)_width, 0);
        _commandList.SetComputeRoot32BitConstant(2, (uint)_height, 1);
        _commandList.SetComputeRoot32BitConstant(2, frameCounter, 2);
        _commandList.SetComputeRoot32BitConstant(2, 0u, 3);

        uint groupsX = (uint)((_width + 7) / 8);
        uint groupsY = (uint)((_height + 7) / 8);
        _commandList.Dispatch(groupsX, groupsY, 1);
    }

    /// <summary>
    /// Headless validation: trace one frame to the offscreen texture, read it
    /// back, and confirm the ray query actually hit the triangles. Returns a
    /// human-readable summary and whether the checks passed.
    /// </summary>
    public (bool passed, string report) RunSelfTest()
    {
        _allocator.Reset();
        _commandList.Reset(_allocator, _pipelineState);
        RecordTrace(0);
        _commandList.ResourceBarrierTransition(_outputTexture, ResourceStates.UnorderedAccess, ResourceStates.CopySource);

        var texDesc = _outputTexture.Description;
        var layouts = new PlacedSubresourceFootPrint[1];
        var numRows = new uint[1];
        var rowSizes = new ulong[1];
        _device.GetCopyableFootprints(texDesc, 0, 1, 0, layouts, numRows, rowSizes, out ulong totalBytes);
        PlacedSubresourceFootPrint footprint = layouts[0];

        ID3D12Resource readback = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Readback),
            HeapFlags.None,
            ResourceDescription.Buffer(totalBytes),
            ResourceStates.CopyDest);

        _commandList.CopyTextureRegion(
            new TextureCopyLocation(readback, footprint), 0, 0, 0,
            new TextureCopyLocation(_outputTexture, 0), null);
        _commandList.ResourceBarrierTransition(_outputTexture, ResourceStates.CopySource, ResourceStates.UnorderedAccess);

        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        WaitForGpu();

        int rowPitch = (int)footprint.Footprint.RowPitch;
        Span<byte> mapped = readback.Map<byte>(0, (int)totalBytes);
        byte[] pixels = mapped.ToArray();
        readback.Unmap(0);
        readback.Dispose();

        float RedAt(int x, int y) => pixels[y * rowPitch + x * 4] / 255f;

        long hitCount = 0;
        for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
                if (RedAt(x, y) > 0.25f)
                    hitCount++;

        long total = (long)_width * _height;
        bool centerHit = RedAt(_width / 2, _height / 2) > 0.25f;
        bool cornerMiss = RedAt(2, 2) <= 0.25f;
        bool coverageOk = hitCount > 0 && hitCount < total;

        bool passed = centerHit && cornerMiss && coverageOk;
        double pct = 100.0 * hitCount / total;
        string report =
            $"  center pixel is a hit : {centerHit}\n" +
            $"  corner pixel is a miss: {cornerMiss}\n" +
            $"  triangle coverage     : {hitCount:N0}/{total:N0} px ({pct:F1}%)\n" +
            $"  overall               : {(passed ? "PASS" : "FAIL")}";
        return (passed, report);
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
