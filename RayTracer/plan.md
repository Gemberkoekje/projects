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

## Phase 1 Execution Status (2026-04-15)

### 1) Domain folders introduced in `RayTracer.Core` (completed)
- ✅ `Rendering/`
- ✅ `Sampling/`
- ✅ `Lighting/`
- ✅ `Geometry/`
- ✅ `Debug/`
- ✅ `Diagnostics/`
- ✅ `Pipeline/`

### 2) Types regrouped into domain folders (completed)
- **Rendering/**
  - `JobSystem.cs`
  - `PerformanceCalibrator.cs`
  - `RenderPreset.cs`
- **Sampling/**
  - `WavelengthLookup.cs`
- **Lighting/**
  - `Light.cs`
  - `LightCones.cs`
  - `LightingMode.cs`
  - `MaterialsLookup.cs`
- **Geometry/**
  - `AABB.cs`
  - `BVH.cs`
  - `Tracable.cs`
  - `Plane.cs`
  - `TracableRectangle.cs`
  - `BrickRectangle.cs`
  - `CeilingTileRectangle.cs`
  - `Ray.cs`
  - `Maze.cs`
  - `MazeGeometryBuilder.cs`
  - `MazeNavigator.cs`
- **Debug/**
  - `DebugViewMode.cs`
- **Diagnostics/**
  - `CpuThrottle.cs`
- **Pipeline/**
  - `Camera.cs`
  - `CameraController.cs`
  - `Matrix.cs`

### 3) Naming/visibility cleanup pass (completed for Phase 1 scope)
- ✅ Added explicit access modifier for implicit private member in `JobSystem`.
- ✅ Confirmed one public type per file remains the practical pattern in `RayTracer.Core`.
- 🔁 Deeper mutable-state reduction deferred to Phase 2 extraction work (`JobSystem` decomposition).

## Phase 2 Execution Status (2026-04-15)

### 1) `JobSystem` decomposition scaffold + orchestration extraction (completed)
- ✅ Converted `JobSystem` to `partial` and introduced dedicated rendering components:
  - `Rendering/TileScheduler.cs`
  - `Rendering/PathTracer.cs`
  - `Rendering/AccumulationBuffer.cs`
  - `Rendering/TaaResolver.cs`
  - `Rendering/DisplayResolver.cs`
  - `Rendering/DebugBufferRenderer.cs`
- ✅ `JobSystem` now delegates externally visible orchestration methods to component instances:
  - `SetupJobs` / `AddJobs` → `TileScheduler`
  - `ResolveDisplayBufferWithTaa` → `TaaResolver`
  - `ResetAccumulation` / `SoftResetAccumulation` → `AccumulationBuffer`
  - `Render` → `DisplayResolver`
  - `GetDebugLegend` / `RenderDebugModeToBuffer` → `DebugBufferRenderer`
- ✅ Trace execution now routes through `PathTracer` (`Trace` facade + `TraceCore`).

### 2) Structured render state containers (completed)
- ✅ Added `Rendering/RenderState.cs` with:
  - `PerPixelState`
  - `HistoryState`
  - `DebugState`
- ✅ `JobSystem` now exposes structured state views:
  - `PerPixel`
  - `History`
  - `Debug`
- ✅ State objects are currently reference-backed by existing arrays to preserve behavior while enabling follow-up extractions.

### 3) Build/test safety validation after extraction (completed)
- ✅ Workspace build passes.
- ✅ `RayTracer.Tests` test run passes: **101/101**.

### 4) Phase 2 notes
- The decomposition has been applied as a behavior-preserving extraction step with dedicated components and state containers in place.
- Remaining deep internals can now be migrated component-by-component with low risk in later phases.

## Phase 3 Execution Status (2026-04-15)

### 1) API/config option records introduced (completed)
- ✅ Added `RayTracer.Core/Rendering/JobSystemOptions.cs` with:
  - `RenderOptions`
  - `SamplingOptions`
  - `DenoiseOptions`
  - `DebugOptions`
- ✅ Added a new `JobSystem` constructor that consumes typed option records.
- ✅ Kept legacy constructor overload as a compatibility shim to avoid breaking existing test/setup paths.
- ✅ Migrated active runtime call sites to the new option-record constructor:
  - `RayTracer/Program.cs`
  - `RayTracer.Core/Rendering/PerformanceCalibrator.cs`
  - `Benchmark/DebugResolveBenchmark.cs`

### 2) Boundary validation (completed)
- ✅ Added core argument guards in `JobSystem` constructor (width/height/stride/null checks).
- ✅ Added option validation for:
  - `TileSize`
  - `SppPerJob`
  - `MaxSampleCount`
  - `MotionSampleCap`
  - `FilterRadius`
  - `TemporalBlendAlpha`
  - `SampleClamp`
- ✅ Added dedicated tests in `RayTracer.Tests/JobSystemOptionsValidationTests.cs`:
  - `Constructor_InvalidWidth_Throws`
  - `Constructor_InvalidStride_Throws`
  - `Constructor_InvalidOptions_Throws`

### 3) Cleanup + nullability tightening (completed for Phase 3 scope)
- ✅ Removed unused imports from `RayTracer.Core/Rendering/JobSystem.cs`.
- ✅ Removed unused `Correction` constructor path and dead `LinearToSRGB` instance helper.
- ✅ Tightened immutability for configuration-backed members (`_bvh`, `_lights`, and option-backed properties now constructor-assigned).

### 4) Validation snapshot after Phase 3 changes
- ✅ Workspace build passes.
- ✅ `RayTracer.Tests` test run passes: **104/104**.

## Phase 4 Execution Status (2026-04-15)

### 1) Unit test expansion for render components (completed)
- ✅ Added deterministic filtering tests (box + edge-aware) in `RayTracer.Tests/Phase4TestingExpansionTests.cs`.
- ✅ Added deterministic TAA behavior tests for first-frame no-history and explicit reprojection rejection.
- ✅ Existing spectral and accumulation suites retained as characterization coverage for color transforms and accumulation behavior.

### 2) Property-style invariant tests (completed)
- ✅ Added randomized invariants for linear XYZ→sRGB transform behavior.
- ✅ Added randomized running-average bounds invariants for accumulation stability.

### 3) Integration + regression coverage (completed)
- ✅ Added small deterministic display-resolve snapshot test with checksum-based regression guard.
- ✅ Added focused integration assertions around resolved history state (`HistoryXYZ`, rejection and weight metrics).

### 4) Performance snapshot tests (completed)
- ✅ Added lightweight throughput snapshot test for matrix/color transform hot path.
- ✅ Throughput is now emitted to test output for before/after trend comparisons during later optimization work.

### 5) Validation snapshot after Phase 4 changes
- ✅ Workspace build passes.
- ✅ `RayTracer.Tests` test run passes: **112/112**.

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

## Phase 0 — Baseline & Safety Net (completed)
1. ✅ Create architecture inventory:
   - ✅ Map projects, modules, and dependencies.
   - ✅ Identify high-risk/high-churn files (starting with `RayTracer.Core/JobSystem.cs`).
2. ✅ Define quality gates:
   - ✅ Build must pass.
   - ✅ Existing tests must pass.
   - ✅ Add smoke tests for app startup and minimal render path.
3. ✅ Capture performance baseline:
   - ✅ Frame time, rays/sec, memory use, GC allocations.

## Phase 1 — Project Structure Cleanup (completed)
1. ✅ Introduce folders by domain in `RayTracer.Core`:
   - ✅ `Rendering/`, `Sampling/`, `Lighting/`, `Geometry/`, `Debug/`, `Diagnostics/`, `Pipeline/`.
2. ✅ Group related types into focused files (one public type per file where practical).
3. ✅ Standardize naming and visibility:
   - ✅ Explicit access modifiers.
   - ✅ Minimize mutable public state (Phase 1-safe scope; broader reductions continue in Phase 2).

## Phase 2 — Decompose `JobSystem` (completed)
1. ✅ Split responsibilities into dedicated components:
   - ✅ `TileScheduler` (jobs/channel/worker orchestration)
   - ✅ `PathTracer` (trace routing and path-trace entrypoint)
   - ✅ `AccumulationBuffer` (accumulation lifecycle/reset orchestration)
   - ✅ `TaaResolver` (history reprojection and blending)
   - ✅ `DisplayResolver` (XYZ→sRGB and buffer writes)
   - ✅ `DebugBufferRenderer` (debug legend/palette/buffer rendering)
2. ✅ Replace shared mutable arrays with structured state objects:
   - ✅ `PerPixelState`
   - ✅ `HistoryState`
   - ✅ `DebugState`
3. ✅ Keep `JobSystem` as orchestration/facade entrypoint for render lifecycle APIs.
4. ✅ Validate behavior continuity with full build + test pass.

## Phase 3 — API & Configuration Improvements (completed)
1. ✅ Convert broad constructor parameters into option records:
   - ✅ `RenderOptions`, `SamplingOptions`, `DenoiseOptions`, `DebugOptions`.
2. ✅ Validate options at boundaries.
3. ✅ Remove dead fields/imports and tighten nullability contracts.

## Phase 4 — Testing Expansion (completed)
1. ✅ Unit tests:
   - ✅ Deterministic tests for sampling/reprojection/filtering and color transforms.
2. ✅ Property-based tests:
   - ✅ Invariants for accumulation bounds and matrix/color conversions.
3. ✅ Integration tests:
   - ✅ Deterministic small-scene/display-resolve snapshot checks.
4. ✅ Regression tests:
   - ✅ Metric-based checksum comparison for selected deterministic resolve inputs.
5. ✅ Performance tests:
   - ✅ Hot-path throughput snapshot tests with logged metrics.

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
1. Start Phase 5 profiling on the decomposed pipeline:
   - `PathTracer.TraceCore`
   - `TaaResolver.ResolveDisplayBufferWithTaa`
   - `DisplayResolver.Render`
2. Compare Phase 4 snapshot metrics against Phase 0 baseline and identify top allocation/throughput deltas.
3. Apply first allocation-focused inner-loop pass where profiling data shows highest ROI.
4. Re-run full build + tests and refresh plan metrics.
