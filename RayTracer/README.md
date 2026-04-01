# RayTracer

Overview

This repository contains a compact spectral ray-tracer with a small application, a core rendering library, unit tests and micro-benchmarks.

Features

The renderer currently implements the following features:

- Spectral rendering: wavelength-to-XYZ conversion, hero wavelength sampling and companion wavelengths to reduce spectral noise.
- Tile-based multithreaded job system: bounded channel of tiles, worker tasks and per-tile tracing for parallel rendering.
- BVH acceleration structure: fast scene traversal with closest-hit and occlusion queries.
- Physically-inspired lighting: ambient term, direct lighting with Next-Event Estimation (NEE) and stochastic shadow rays.
- One-bounce indirect estimator: cheap Monte-Carlo single-bounce for visible indirect lighting.
- Temporal anti-aliasing (TAA): reprojection-based history blending with disocclusion rejection and history weight tracking.
- Spatial filtering: optional edge-aware bilateral filter with configurable radius for motion denoising.
- Motion-aware rendering: checkerboard rendering while moving, motion sample caps and temporal blending controls.
- Sub-pixel jittering: deterministic per-pixel jitter to convert aliasing to noise that the accumulator averages away.
- Sample clamping: per-sample XYZ clamping to suppress fireflies, with heatmap/debugging counters.
- Accumulation & statistics: running mean accumulation, Welford variance estimation, history weight and debug metrics.
- Multiple debug views: beauty, sample count, variance, history weight, clamp heatmap, depth, albedo, normals and lighting buffers.
- sRGB conversion and display pipeline: XYZ-to-sRGB matrix conversion and proper gamma handling for display output.


Project layout

- `RayTracer/` (application)
  - `Program.cs` - application entry point for the UI app.
  - `CalibrationForm.cs` - UI for calibration controls and runtime settings.
  - other UI glue code and resources for the app.

- `RayTracer.Core/` (core rendering library)
  - `JobSystem.cs` - tile-based worker system, accumulation, TAA, filtering, debug views and the main tracing loop.
  - `BVH.cs` - bounding volume hierarchy for scene intersection acceleration.
  - `AABB.cs` - axis-aligned bounding box utilities used by `BVH`.
  - `Ray.cs` - ray data structure used for tracing.
  - `Matrix.cs` / `Camera.cs` / `CameraController.cs` - camera & projection math and helpers.
  - `WavelengthLookup.cs` - spectral to XYZ conversions and wavelength sampling utilities.
  - `Tracable.cs`, `TracableRectangle.cs`, `Plane.cs`, `TracableRectangle.cs` - scene primitives and their intersection logic.
  - `Light.cs`, `LightCones.cs` - light definitions and helpers.
  - `MaterialsLookup.cs` - material property lookup for primitives.
  - `LightingMode.cs` - enumeration describing lighting modes supported by the tracer.
  - `Maze*.cs` (e.g. `Maze.cs`, `MazeNavigator.cs`, `MazeGeometryBuilder.cs`) - utility code used by some scenes/tests/benchmarks.
  - other support classes used by the rendering pipeline.

- `RayTracer.Tests/` (unit tests)
  - tests that exercise BVH, maze geometry and other logic: e.g. `BVHTests.cs`, `MazeTests.cs`, `AccumulationTests.cs`, `SpectralColorTests.cs`, `CameraControllerTests.cs`.

- `Benchmark/` (benchmarks)
  - micro-benchmarks and experimental harnesses, e.g. `BVHBenchmark.cs`, `TracableRectangle.cs`, and a small `readme.md` describing benchmark usage.

- `ConsoleApp1/` - small console app (sample/test harness).

Key files and what they do (short)

- `RayTracer.Core/JobSystem.cs`
  - Manages tile generation, worker tasks, tracing samples per pixel, temporal anti-aliasing (TAA), accumulation buffers and debug visualisations.

- `RayTracer.Core/BVH.cs`
  - Builds a BVH over scene `Tracable` primitives and exposes `FindClosest` and occlusion queries used by the integrator.

- `RayTracer.Core/WavelengthLookup.cs`
  - Maps integer wavelengths to CIE XYZ and provides sampling helpers for spectral rendering.

- `RayTracer.Core/Camera.cs` and `CameraController.cs`
  - Camera data (position/rotation/projection) and interactive camera control code used by the application.

- `RayTracer/Program.cs` and `CalibrationForm.cs`
  - App startup and the form used to tweak render settings at runtime.

- `RayTracer.Tests/*` and `Benchmark/*`
  - Unit tests and simple benchmarking tools for validating and measuring performance of the core systems.

Building and running

- Build everything: `dotnet build` from the repository root.
- Run unit tests: `dotnet test`.
- Run the UI application in Visual Studio (open solution and use the IDE). The `RayTracer.App` project is the application project (see `RayTracer\RayTracer.App.csproj`).
- Run benchmarks: `dotnet run --project Benchmark\Benchmarks.csproj` (or open the `Benchmark` project in your IDE).

Notes

- The core tracer targets modern C# and .NET (C# 13 / .NET 9). Use a recent SDK / Visual Studio 2024+ or `dotnet` that supports the target frameworks.
- The repository contains many helper and test files; if you want a focused README section for any particular subsystem (BVH, spectral renderer, TAA), tell me which one and I will expand that section.
