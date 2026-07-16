# Architecture

## Solution map

- `RayTracer.Core` (`net10.0`): rendering engine, geometry, lighting, sampling, diagnostics.
- `RayTracer.Gpu` (`net10.0-windows`): the single application executable — a WinForms host with the unified GPU/CPU config screen (`ConfigForm`), the GPU DXR renderers (Phase 1–6), the CPU software renderer (`RayForm` in `CpuRenderForm.cs`), and the screensaver. The default launch shows the config screen; a handful of headless `--*-selftest` / `--phase6-regress` switches remain for validation.
- `RayTracer.Tests` (`net10.0`): MSTest test suite.
- `Benchmark` (`net10.0`): BenchmarkDotNet performance harness (dev-only).
- `.net.csproj`: repo-level tooling/analyzer aggregation project.

## `RayTracer.Core` domain folders

- `Rendering/`: orchestration facade and render pipeline components (`JobSystem`, schedulers, resolvers).
- `Geometry/`: scene primitives and intersection acceleration (`BVH`, `AABB`, maze geometry, the `HitInfo` intersection result).
- `Lighting/`: light definitions, cone helpers, material and spectral lookup integration.
- `Optics/`: wavelength-dependent surface behaviour — `SurfaceKind` classification and the `Optics` refraction/Fresnel helpers shared by the spectral & optical effects (see `plan.md`).
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
