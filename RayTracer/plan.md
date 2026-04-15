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

## Phase 5 Execution Status (2026-04-15)

### 1) Hot-path profiling + static analysis (completed)
- ✅ Inspected inner-loop allocation patterns in `PathTracer.TraceCore`, `TaaResolver.ResolveDisplayBufferWithTaa`, and `DisplayResolver.Render`.
- ✅ Identified highest-ROI targets:
  - Per-tile heap allocation (`Tile` class).
  - Per-pixel `Vector3` reconstruction for sample clamping.
  - Per-pixel `Quaternion.Inverse` call in reprojection inner loop.
  - Non-atomic counter increments under worker concurrency.
  - Redundant gamma exponent literal duplication across resolvers.

### 2) Allocation reductions in inner loops (completed)
- ✅ Converted `Tile` to `readonly record struct` — eliminates one heap allocation per dispatched tile.
- ✅ Precomputed `_sampleClampVec` (`Vector3`) in `JobSystem` constructor — single allocation per `JobSystem` lifetime; removed per-pixel `new Vector3(SampleClamp, ...)`.
- ✅ Hoisted `Quaternion.Inverse(previousCameraRotation)` out of per-pixel loop in `TaaResolver` — computed once per resolve call and passed into `TryProjectToPrevPixel`.

### 3) Concurrency correctness (completed)
- ✅ Replaced non-atomic counter increments in `TileScheduler` worker path with `Interlocked.Add` / `Interlocked.Increment` — eliminates lost-update races under parallel workers.

### 4) Lightweight runtime metrics logging (completed)
- ✅ Added `RayTracer.Core/Diagnostics/FrameDiagnostics.cs` — `readonly record struct` capturing per-frame resolve timing, quality metrics (rejection rate, variance, history weight, avg SPP, clamp fraction), and running totals.
- ✅ `TaaResolver.ResolveDisplayBufferWithTaa` now uses `Stopwatch` to measure wall time and populates a `FrameDiagnostics` snapshot at the end of each resolve.
- ✅ `JobSystem` exposes `LastFrameDiagnostics` property — callers can read live diagnostics after each frame without additional allocation.

### 5) Constant consolidation (completed)
- ✅ Extracted `InvGamma` (`1f / 2.2f`) as a shared constant in `JobSystem`; reused in both `TaaResolver` and `DisplayResolver`, removing literal duplication.

### 6) Performance validation tests (completed)
- ✅ Added `RayTracer.Tests/Phase5PerformanceTests.cs`:
  - `Tile_IsValueType` — structural guard that `Tile` remains a value type.
  - `FrameDiagnostics_IsValueType` — structural guard for `FrameDiagnostics`.
  - `FrameDiagnostics_AfterResolve_PopulatesResolveTaaMs` — confirms timing field is non-negative after a resolve.
  - `FrameDiagnostics_AfterResolve_FrameIndexIncrements` — confirms frame counter advances per resolve.
  - `AllocationBaseline_Phase5_Snapshot` — allocation + throughput snapshot for before/after trend comparison.

### 7) Validation snapshot after Phase 5 changes
- ✅ Workspace build passes.
- ✅ `RayTracer.Tests` test run passes: **121/121**.

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

## Phase 5 — Performance & Memory Pass (completed)
1. ✅ Profile hot paths after decomposition.
2. ✅ Reduce allocations in inner loops.
3. ✅ Consider data layout optimizations only when profiling justifies it.
4. ✅ Add lightweight runtime metrics logging for frame diagnostics.

## Completion Audit (2026-04-15)

### Verified against actual codebase

| Check | Status | Detail |
|---|---|---|
| Solution structure matches plan | ✅ | 5 projects + `Benchmark/Benchmarks.csproj` (not in Phase 0 inventory) |
| Domain folders in `RayTracer.Core` | ✅ | `Rendering/`, `Sampling/`, `Lighting/`, `Geometry/`, `Debug/`, `Diagnostics/`, `Pipeline/` all present |
| Decomposition components exist | ✅ | `TileScheduler`, `PathTracer`, `AccumulationBuffer`, `TaaResolver`, `DisplayResolver`, `DebugBufferRenderer` all present as partial-class files |
| State containers exist | ✅ | `PerPixelState`, `HistoryState`, `DebugState` in `RenderState.cs` |
| Option records exist | ✅ | `RenderOptions`, `SamplingOptions`, `DenoiseOptions`, `DebugOptions` in `JobSystemOptions.cs` |
| `FrameDiagnostics` exists | ✅ | `readonly record struct` in `Diagnostics/FrameDiagnostics.cs` |
| `Tile` is value type | ✅ | Confirmed by `Tile_IsValueType` test passing |
| `InvGamma` constant consolidated | ✅ | Single constant in `JobSystem` |
| Build passes | ✅ | `dotnet build` 0 errors |
| All tests pass | ✅ | **122/122** (plan said 121; one test added after plan update) |

### Success criteria gaps

| Criterion | Target | Actual | Gap |
|---|---|---|---|
| No file over ~500 lines | ~500 | `JobSystem.cs` = **1214**, `AccumulationTests.cs` = **536** | ❌ `JobSystem.cs` still 2.4× over limit |
| Stable CI pipeline | Exists | **No CI workflow file** | ❌ Not started |
| Warning count tracked | 433 (Phase 0) | **762 warnings** | ❌ Increased 76% |

### Other observations
- `RayTracer.Tests/Test1.cs` is an **empty file** — dead artifact to remove.
- `Benchmark/Benchmarks.csproj` is in the solution but **missing from Phase 0 architecture inventory**.
- The `partial class` approach in Phase 2 moved methods into separate files, but `JobSystem.cs` still holds all field declarations, properties, constructors, and several orchestration methods.
- `CalibrationForm.cs` (450 lines) and `Program.cs` (452 lines) are close to the 500-line limit but currently within tolerance.

---

## Phase 6 — Tooling & Quality Automation (not started)
1. Enable stricter analyzers and warnings-as-errors (incrementally).
   - Start with `RayTracer.Core` — add `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` after fixing its warnings.
   - Then extend to `RayTracer.Tests`, `RayTracer.App`, `Benchmark`.
2. Add `.editorconfig` for formatting/style enforcement (indentation, naming, using-directive order).
3. Add GitHub Actions CI workflow (`.github/workflows/ci.yml`):
   - Restore → Build → Test → Analyzer checks → Optional benchmark run.
4. Add contributor docs:
   - `ARCHITECTURE.md` — module map, domain folder descriptions, data flow.
   - `CONTRIBUTING.md` — build/test/PR workflow, coding conventions.
5. Clean up dead artifacts:
   - Remove empty `RayTracer.Tests/Test1.cs`.
   - Add `Benchmark` project to Phase 0 architecture inventory.

## Phase 7 — Deep `JobSystem` Slimming (proposed)

**Goal:** Bring `JobSystem.cs` under the ~500-line target. The Phase 2 partial-class extraction moved methods out but left fields, properties, constructors, and mid-level orchestration logic in the main file.

1. Extract field declarations + array allocation into a dedicated `RenderBuffers` or `RenderResources` class:
   - All per-pixel arrays (`AccumXYZ`, `SampleCount`, `WavelengthCounter`, debug arrays, TAA history, etc.)
   - Single allocation site; `JobSystem` holds one `RenderResources` reference.
2. Extract constructor logic into a static factory or builder:
   - `JobSystemBuilder.Create(scene, lights, camera, options)` → `JobSystem`.
   - Moves validation, array sizing, and component wiring out of the main file.
3. Slim down orchestration methods:
   - Move remaining inline logic (e.g., filtering, checkerboard bookkeeping) into the owning component.
   - `JobSystem` becomes a thin facade: setup → dispatch → resolve → render.
4. Reassess `partial class` vs. composition:
   - Current partials are nested classes inside `JobSystem` — evaluate promoting them to top-level classes that receive `RenderResources` directly.
5. Validate: build + 122+ tests pass, no performance regression vs. Phase 5 baseline.

## Phase 8 — Test Suite Improvements (proposed)

**Goal:** Improve test maintainability and coverage gaps.

1. Split `AccumulationTests.cs` (536 lines) into focused test classes by concern:
   - `AccumulationLifecycleTests` (reset, hit/miss transitions)
   - `AccumulationConvergenceTests` (running average, EMA, capping)
   - `AccumulationSpectralTests` (wavelength cycling, color convergence)
2. Split `SpectralColorTests.cs` (335 lines) if it approaches the limit during Phase 7 additions.
3. Remove dead `Test1.cs`.
4. Add missing coverage:
   - `PerformanceCalibrator` — no dedicated tests.
   - `CameraController` — only basic movement tests; add edge cases (bounds, rapid direction changes).
   - `BVH` — add stress tests with degenerate geometry (all-coplanar, zero-volume AABBs).
5. Consider test parallelization configuration in `MSTestSettings.cs` for faster CI runs.

## Phase 9 — Advanced Performance & Modernization (proposed)

**Goal:** Leverage .NET 10 features and pursue deeper performance wins.

1. Evaluate `Span<T>` / `Memory<T>` for buffer passing in hot paths (avoid array-as-interface pattern).
2. Evaluate `Vector3` → `Vector128<float>` / SIMD intrinsics for color math inner loops.
3. Profile TAA resolve + display resolve with the .NET profiler for GC pressure after Phase 7 refactoring.
4. Investigate `System.Threading.Tasks.Dataflow` or `Channel<T>` improvements for tile dispatch.
5. Benchmark struct-of-arrays vs. array-of-structs for per-pixel data (current: parallel arrays, which is already SoA-friendly — validate this is optimal with cache-line profiling).
6. Capture updated performance baseline and compare against Phase 0/Phase 5 snapshots.

## Phase 10 — Documentation & Onboarding (proposed)

**Goal:** Make the project approachable for contributors.

1. Write `ARCHITECTURE.md`:
   - Solution map (which project does what).
   - `RayTracer.Core` domain folder guide (Rendering, Geometry, Lighting, Sampling, Pipeline, Debug, Diagnostics).
   - Data flow diagram: scene → BVH → tile dispatch → path trace → accumulate → TAA → display.
2. Write `CONTRIBUTING.md`:
   - Build prerequisites (.NET 10 SDK, Windows for WinForms app).
   - How to run tests, benchmarks, and the WinForms app.
   - PR checklist (build, tests, no new warnings, formatting).
3. Add inline architecture comments to `JobSystem` facade methods documenting the render pipeline stages.
4. Add `README.md` sections for: quick start, architecture overview, performance baselines.

## Risks & Mitigations
- **Risk:** Behavior drift during extraction.
  - **Mitigation:** Add characterization tests before moving code.
- **Risk:** Performance regressions.
  - **Mitigation:** Keep baseline benchmarks and gate merges on key metrics.
- **Risk:** Large PR complexity.
  - **Mitigation:** Keep changes vertical and small, merge frequently.
- **Risk:** Warning-as-error enablement causes churn.
  - **Mitigation:** Enable per-project, fix warnings in dedicated PRs before flipping the flag.
- **Risk:** Phase 7 `JobSystem` slimming breaks component wiring.
  - **Mitigation:** Each extraction step must pass full 122+ test suite before proceeding.

## Immediate Next Steps
1. **Phase 6.1** — Add `.editorconfig` and fix formatting across the solution.
2. **Phase 6.2** — Triage the 762 build warnings; fix `RayTracer.Core` warnings and enable `TreatWarningsAsErrors` for that project.
3. **Phase 6.3** — Add `.github/workflows/ci.yml` with restore → build → test → analyzer pipeline.
4. **Phase 7.1** — Extract `RenderResources` from `JobSystem.cs` to bring it under ~500 lines.
5. **Phase 8.1** — Remove `Test1.cs` and split `AccumulationTests.cs`.
