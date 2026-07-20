---
type: community
cohesion: 0.08
members: 31
---

# GPU Device Resources (D3D12)

**Cohesion:** 0.08 - loosely connected
**Members:** 31 nodes

## Members
- [[.CreateCommandObjects()]] - code - RayTracer.Gpu/GpuRayTracer.cs
- [[.CreateDeviceAndQueue()]] - code - RayTracer.Gpu/GpuRayTracer.cs
- [[.CreateOutputResources()]] - code - RayTracer.Gpu/GpuRayTracer.cs
- [[.CreatePipeline()]] - code - RayTracer.Gpu/GpuRayTracer.cs
- [[.CreateSwapChain()]] - code - RayTracer.Gpu/GpuRayTracer.cs
- [[.Dispose()]] - code - RayTracer.Gpu/GpuRayTracer.cs
- [[.EnableDebugLayer()]] - code - RayTracer.Gpu/GpuRayTracer.cs
- [[.LoadShaderSource()]] - code - RayTracer.Gpu/GpuRayTracer.cs
- [[.RecordTrace()]] - code - RayTracer.Gpu/GpuRayTracer.cs
- [[.RenderFrame()]] - code - RayTracer.Gpu/GpuRayTracer.cs
- [[.RunSelfTest()]] - code - RayTracer.Gpu/GpuRayTracer.cs
- [[.VerifyRaytracingSupport()]] - code - RayTracer.Gpu/GpuRayTracer.cs
- [[.WaitForGpu()]] - code - RayTracer.Gpu/GpuRayTracer.cs
- [[AutoResetEvent]] - code
- [[Format]] - code
- [[GpuRayTracer]] - code - RayTracer.Gpu/GpuRayTracer.cs
- [[ID3D12CommandAllocator]] - code
- [[ID3D12CommandQueue]] - code
- [[ID3D12DescriptorHeap]] - code
- [[ID3D12Device5]] - code
- [[ID3D12Fence]] - code
- [[ID3D12GraphicsCommandList4]] - code
- [[ID3D12PipelineState]] - code
- [[ID3D12RootSignature]] - code
- [[IDXGIFactory4]] - code
- [[IDXGISwapChain3]] - code
- [[IDisposable]] - code
- [[int_21]] - code
- [[passed]] - code
- [[report]] - code
- [[ulong_1]] - code

## Live Query (requires Dataview plugin)

```dataview
TABLE source_file, type FROM #community/GPU_Device_Resources_D3D12
SORT file.name ASC
```

## Connections to other communities
- 8 edges to [[_COMMUNITY_Maze Geometry Packing]]
- 5 edges to [[_COMMUNITY_Community 178]]
- 1 edge to [[_COMMUNITY_Maze GPU Launcher & Classic Mode]]
- 1 edge to [[_COMMUNITY_GPU CommandFence Resources]]
- 1 edge to [[_COMMUNITY_GPU Phase2 Renderer (D3D12)]]
- 1 edge to [[_COMMUNITY_GPU Phase3 Renderer (D3D12)]]
- 1 edge to [[_COMMUNITY_GPU Phase4 Renderer (D3D12)]]
- 1 edge to [[_COMMUNITY_GPU Phase5 Renderer (D3D12)]]
- 1 edge to [[_COMMUNITY_DXR Acceleration Structures]]
- 1 edge to [[_COMMUNITY_Community 254]]
- 1 edge to [[_COMMUNITY_Maze Modes & Volumetrics]]
- 1 edge to [[_COMMUNITY_Maze Program & Bitmap Output]]

## Top bridge nodes
- [[IDisposable]] - degree 7, connects to 6 communities
- [[GpuRayTracer]] - degree 34, connects to 3 communities
- [[.CreatePipeline()]] - degree 4, connects to 2 communities
- [[.RunSelfTest()]] - degree 6, connects to 1 community
- [[.WaitForGpu()]] - degree 5, connects to 1 community