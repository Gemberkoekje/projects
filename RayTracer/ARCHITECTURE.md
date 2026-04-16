# Architecture

## Solution map

- `RayTracer.Core` (`net10.0`): rendering engine, geometry, lighting, sampling, diagnostics.
- `RayTracer` (`net10.0-windows`): WinForms application host and UI.
- `RayTracer.Tests` (`net10.0`): MSTest test suite.
- `Benchmark` (`net10.0`): BenchmarkDotNet performance harness.
- `ConsoleApp1` (`net10.0`): auxiliary console sandbox.
- `.net.csproj`: repo-level tooling/analyzer aggregation project.

## `RayTracer.Core` domain folders

- `Rendering/`: orchestration facade and render pipeline components (`JobSystem`, schedulers, resolvers).
- `Geometry/`: scene primitives and intersection acceleration (`BVH`, `AABB`, maze geometry).
- `Lighting/`: light definitions, cone helpers, material and spectral lookup integration.
- `Sampling/`: wavelength sampling and spectral distribution support.
- `Pipeline/`: camera, controller, and matrix/pipeline transforms.
- `Debug/`: debug modes and debug visualization support.
- `Diagnostics/`: runtime diagnostics and frame metrics.

## Render data flow

1. Scene geometry is built and packed into `BVH`.
2. `JobSystem` schedules work by tile through `TileScheduler`.
3. `PathTracer` traces rays and updates accumulation/per-pixel state.
4. `AccumulationBuffer` applies reset/soft-reset lifecycle behavior.
5. `TaaResolver` reprojects history and blends temporal data.
6. `DisplayResolver` converts XYZ to display output (`sRGB`) and writes final buffers.
7. Optional `DebugBufferRenderer` overlays diagnostic/debug output.

## Design intent

- Keep `JobSystem` as a thin orchestration facade.
- Keep hot loops in focused components for easier profiling and optimization.
- Prefer deterministic tests around resolve, accumulation, and color conversion behavior.
