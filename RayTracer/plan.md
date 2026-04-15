# RayTracer Refactor Plan

## Phase 0 Execution Status (2026-04-15)

### 1) Architecture inventory (completed)
- **Projects in solution**
  - `RayTracer.Core/RayTracer.csproj` (`net10.0`)
  - `RayTracer/RayTracer.App.csproj` (`net10.0-windows`, WinForms)
  - `RayTracer.Tests/RayTracer.Tests.csproj` (`net10.0`, MSTest)
  - `ConsoleApp1/ConsoleApp1.csproj` (`net10.0`)
  - `.net.csproj` (temp tooling project)
- **Current module focus in `RayTracer.Core`**
  - Rendering/orchestration centered in `JobSystem`
  - Geometry/intersection: `AABB`, `BVH`, `Plane`, `TracableRectangle`, etc.
  - Scene generation/navigation: `Maze`, `MazeGeometryBuilder`, `MazeNavigator`
  - Camera/control: `Camera`, `CameraController`
  - Spectral/material pipeline: `WavelengthLookup`, `MaterialsLookup`
- **Largest source files (risk hotspots by size)**
  - `RayTracer.Core/JobSystem.cs` (~1524 lines)
  - `RayTracer.Tests/AccumulationTests.cs` (~635 lines)
  - `RayTracer/CalibrationForm.cs` (~505 lines)
  - `RayTracer/Program.cs` (~504 lines)
  - `RayTracer.Tests/SpectralColorTests.cs` (~408 lines)
- **Initial high-churn signals from git history**
  - Frequent edits around `RayTracer.Core/JobSystem.cs` and `RayTracer/Program.cs`.

### 2) Quality gates (completed)
- ✅ Build passes (`run_build` successful).
- ✅ Existing automated tests pass (`98/98` passing before Phase 0 additions).
- ✅ Smoke tests added for startup/minimal render path:
  - `RayTracer.Tests/Phase0SmokeTests.cs`
    - `StartupSmoke_CanBuildSceneAndCreateJobSystem`
    - `MinimalRenderPathSmoke_ResolveDisplayBufferWithTaa_WritesOpaqueOutput`
    - `BaselineSmoke_CapturesRaysPerSecondAndAllocationSnapshot`
- ✅ Smoke test run result: `3/3` passing.

### 3) Baseline performance snapshot (completed)
Captured from `BaselineSmoke_CapturesRaysPerSecondAndAllocationSnapshot`:
- elapsed time: **~16054.71 ms**
- throughput: **~24,020,567 rays/sec**
- allocation delta: **~10,799,004,152 bytes**
- process working set snapshot: **~88,862,720 bytes**

Notes:
- This is a practical baseline from current calibration-style execution, suitable for before/after comparison during Phase 2 extractions.
- Build output currently reports a high warning count (433); this should be tracked as part of later quality tightening in Phase 6.

## Goals
- Reduce file size and cognitive load by splitting large classes into focused components.
- Increase confidence with broader, faster, and more deterministic tests.
- Improve maintainability, performance visibility, and developer experience.
- Keep behavior stable while refactoring incrementally.

## Guiding Principles
- Prefer small, composable classes with single responsibilities.
- Refactor behind tests; avoid large behavior changes without coverage.
- Keep changes incremental and shippable (small PR-sized steps).
- Measure before/after for performance-sensitive code paths.

## Success Criteria
- No single source file over ~500 lines (exceptions documented).
- Core rendering paths covered by unit + integration + regression tests.
- Stable CI pipeline: build + tests + static analysis + formatting checks.
- No net regressions in baseline render correctness or performance.

## Phase 0 — Baseline & Safety Net
1. Create architecture inventory:
   - Map projects, modules, and dependencies.
   - Identify high-risk/high-churn files (starting with `RayTracer.Core/JobSystem.cs`).
2. Define quality gates:
   - Build must pass.
   - Existing tests must pass.
   - Add smoke tests for app startup and minimal render path.
3. Capture performance baseline:
   - Frame time, rays/sec, memory use, GC allocations.

## Phase 1 — Project Structure Cleanup
1. Introduce folders by domain in `RayTracer.Core`:
   - `Rendering/`, `Sampling/`, `Lighting/`, `Geometry/`, `Debug/`, `Diagnostics/`, `Pipeline/`.
2. Group related types into focused files (one public type per file where practical).
3. Standardize naming and visibility:
   - Explicit access modifiers.
   - Minimize mutable public state.

## Phase 2 — Decompose `JobSystem`
1. Split responsibilities into dedicated components:
   - `TileScheduler` (jobs/channel/worker orchestration)
   - `PathTracer` (trace logic and bounce evaluation)
   - `AccumulationBuffer` (sample accumulation, variance, clamp stats)
   - `TaaResolver` (history reprojection and blending)
   - `DisplayResolver` (XYZ->sRGB conversion and buffer write)
   - `DebugBufferRenderer` (debug view palettes and buffer rendering)
2. Replace shared mutable arrays with structured state objects:
   - `PerPixelState`, `HistoryState`, `DebugState`.
3. Keep `JobSystem` as orchestration/facade only.
4. Add contract tests before each extraction to lock behavior.

## Phase 3 — API & Configuration Improvements
1. Convert broad constructor parameters into option records:
   - `RenderOptions`, `SamplingOptions`, `DenoiseOptions`, `DebugOptions`.
2. Validate options at boundaries.
3. Remove dead fields/imports and tighten nullability contracts.

## Phase 4 — Testing Expansion
1. Unit tests:
   - Deterministic tests for sampling, reprojection, filtering, clamping, and color transforms.
2. Property-based tests:
   - Invariants for accumulation, variance bounds, and matrix/color conversions.
3. Integration tests:
   - Small scene snapshots with tolerance checks.
4. Regression tests:
   - Golden-image or metric-based comparisons for selected presets.
5. Performance tests:
   - Benchmark critical hot paths and track trends over time.

## Phase 5 — Performance & Memory Pass
1. Profile hot paths after decomposition.
2. Reduce allocations in inner loops.
3. Consider data layout optimizations only when profiling justifies it.
4. Add lightweight runtime metrics logging for frame diagnostics.

## Phase 6 — Tooling & Quality Automation
1. Enable stricter analyzers and warnings-as-errors (incrementally).
2. Add formatting/style enforcement.
3. Update CI:
   - Restore, build, tests, analyzer checks, optional benchmarks.
4. Add contributor docs for architecture and testing workflow.

## Suggested Work Breakdown (PR Sequence)
1. Baseline metrics + smoke tests.
2. Extract color/display conversion path.
3. Extract TAA resolve path.
4. Extract debug rendering path.
5. Extract tile scheduling/worker logic.
6. Extract tracing/lighting pipeline.
7. Introduce state containers and reduce `JobSystem` to coordinator.
8. Expand regression/perf tests and tighten analyzers.

## Risks & Mitigations
- **Risk:** Behavior drift during extraction.
  - **Mitigation:** Add characterization tests before moving code.
- **Risk:** Performance regressions.
  - **Mitigation:** Keep baseline benchmarks and gate merges on key metrics.
- **Risk:** Large PR complexity.
  - **Mitigation:** Keep changes vertical and small, merge frequently.

## Immediate Next Steps (First 1–2 Days)
1. Add characterization tests around current `JobSystem` outputs:
   - Accumulation updates, TAA accept/reject, debug buffer rendering.
2. Extract `DisplayResolver` and corresponding tests.
3. Extract `DebugBufferRenderer` and corresponding tests.
4. Re-run full build + tests and compare baseline metrics.
