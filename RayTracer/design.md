# Spectral Maze Screensaver – Design Document

A recreation of the classic Windows 3D Maze screensaver, rebuilt as a physically-based
spectral path tracer. The project progresses in two major arcs: first a faithful
recreation of the original screensaver, then an extension with advanced volumetric and
atmospheric effects.

---

## Current State of the Project

The engine core is already functional:

| Component | Status | Notes |
|---|---|---|
| `Ray` (spectral) | ✅ Done | Carries wavelength (nm) and intensity |
| `Tracable` interface | ✅ Done | Returns intersection location, UV, spectral reflectance |
| `Plane` / `TracableRectangle` | ✅ Done | Infinite plane and bounded quad |
| `Camera` | ✅ Done | Position, rotation, FOV, aspect ratio |
| `Light` | ✅ Stub | Has position, color, ambient — not yet wired into shading |
| `WavelengthLookup` | ✅ Done | CIE XYZ 1931 2° observer, hero wavelength cycling |
| `MaterialsLookup` | ✅ Done | Real spectral reflectance from embedded CSV data |
| `Matrix3x3` / `LightCones` | ✅ Done | XYZ → sRGB conversion |
| `JobSystem` | ✅ Done | Tiled multi-threaded rendering, running-average accumulation |
| WinForms viewer | ✅ Done | Real-time 120 Hz preview with bitmap blitting |
| Test suite | ✅ Done | Spectral color pipeline, accumulation, matrix correctness |
| Benchmarks | ✅ Done | Intersection performance baseline |

---

## Arc 1 — Classic Maze Screensaver Recreation

### Phase 1: Maze Generation

Build a 2D maze on a grid that can be converted into 3D geometry.

- [ ] **1.1** Create a `Maze` class that stores a 2D grid of cells with wall flags
      (North, South, East, West) and visited state.
- [ ] **1.2** Implement a maze generation algorithm (recursive backtracker / DFS) that
      carves passages through the grid, producing a perfect maze (exactly one path
      between any two cells).
- [ ] **1.3** Support configurable maze dimensions (e.g. 16×16 for the classic feel)
      and a seed for reproducibility.
- [ ] **1.4** Add unit tests that verify maze connectivity (flood-fill reaches every
      cell) and wall consistency (if cell A has no East wall, cell B to its east has
      no West wall).

### Phase 2: 3D Maze Geometry

Convert the 2D maze into a scene of `TracableRectangle` quads.

- [ ] **2.1** Create a `MazeGeometryBuilder` that walks the maze grid and emits wall
      quads. Each cell maps to a world-space square (e.g. 2×2 units). Walls are
      vertical rectangles at cell boundaries where a wall flag is set.
- [ ] **2.2** Emit a single floor plane and a single ceiling plane spanning the entire
      maze.
- [ ] **2.3** Assign materials to walls, floor, and ceiling. Use existing spectral
      materials from `MaterialsLookup` (e.g. a warm-toned material for brick walls,
      a neutral grey for the floor, a darker material for the ceiling).
- [ ] **2.4** Integrate the generated `Tracable[]` scene into the `JobSystem` and
      verify it renders correctly from a static top-down or first-person viewpoint.
- [ ] **2.5** Optimize intersection: replace the linear `foreach` loop in
      `JobSystem.Trace` with a spatial acceleration structure (uniform grid or BVH)
      so large mazes remain interactive.

### ~~Phase 3: Lighting & Shading~~ — Skipped

> **Not needed for the classic recreation.** The original maze screensaver was
> *unlit* — surfaces simply displayed their texture color at full brightness with
> no diffuse/specular calculation. Depth was conveyed entirely by perspective
> projection. The current `Trace` method already does exactly this: it returns
> `CIE_XYZ(λ) × material_reflectance`, which is the spectral equivalent of
> fullbright rendering. Direct illumination (Lambertian shading, shadow rays,
> specular) is deferred to **Arc 2, Phase 8** where it becomes necessary for
> torches, sky light, and volumetric effects.

### Phase 3: First-Person Camera & Navigation

Animate the camera to walk through the maze autonomously.

- [ ] **3.1** Implement a first-person camera controller: the camera sits at eye
      height (≈0.5× wall height), looks forward along its heading, and uses the
      existing `Camera.Position` and `Camera.Rotation`.
- [ ] **3.2** Implement smooth forward movement at a fixed speed with time-based
      interpolation (cell-center to cell-center).
- [ ] **3.3** Implement smooth 90° turns with rotation interpolation (slerp on
      `Quaternion`).
- [ ] **3.4** Implement a maze navigation algorithm. The original screensaver uses
      a right-hand (or left-hand) wall-following rule. At each junction, decide the
      next direction and queue the turn.
- [ ] **3.5** Handle accumulation reset on camera movement — the `JobSystem` already
      resets on hit-state change; extend this so moving the camera clears or
      fast-decays the accumulation buffer so the image converges quickly after each
      step.
- [ ] **3.6** Add a simple start/goal marker (e.g. colored floor patch at the maze
      exit) so it feels like the original.

### Phase 4: Screensaver Mode

Make it behave like a proper Windows screensaver.

- [ ] **4.1** Add a fullscreen borderless mode to the WinForms app (or replace with a
      lightweight Win32/SDL window).
- [ ] **4.2** Exit on any mouse movement or key press, matching screensaver behavior.
- [ ] **4.3** Support `/s` (run), `/c` (configure), `/p` (preview) command-line
      switches so it can be registered as a `.scr` screensaver.
- [ ] **4.4** Add a simple settings dialog for maze size, walk speed, and material
      choice.

### Phase 5: Polish & Parity

Close remaining gaps to match the original screensaver's look and feel.

- [ ] **5.1** Tune wall height, cell size, FOV, and walk speed to match the classic
      proportions.
- [ ] **5.2** Add the classic floating objects (rat, OpenGL logo) as simple colored
      geometry in the maze — or defer to Arc 2.
- [ ] **5.3** Ensure the renderer hits ≥30 FPS at 1080p on a modern CPU by profiling
      hot paths (intersection, shading, accumulation).
- [ ] **5.4** Add an FPS/convergence overlay toggle for development.

---

## Arc 2 — Smoke & Mirrors

With the base screensaver working, layer in physically-based atmospheric and optical
effects that benefit from the spectral renderer. This arc introduces proper light
transport — the unlit fullbright approach from Arc 1 is replaced with shading,
shadow rays, and emissive light sources.

### Phase 6: Mirrors

- [ ] **6.1** Add a reflective material type with wavelength-dependent reflectance
      (e.g. silver or aluminium spectral curves).
- [ ] **6.2** Implement recursive ray tracing: on hitting a mirror surface, spawn a
      reflected ray and accumulate the returned radiance, up to a configurable bounce
      limit.
- [ ] **6.3** Place mirror panels on select maze walls and verify correct reflections
      (including spectral dispersion on metallic surfaces).

### Phase 7: Direct Illumination & Torches

This is where proper light transport enters the engine — deferred from Arc 1 because
the original screensaver was unlit.

- [ ] **7.1** Wire up the existing `Light` class. Implement Lambertian (diffuse)
      shading: at the hit point, cast a shadow ray toward each light; modulate the
      spectral reflectance by `max(0, dot(N, L))` and inverse-square attenuation.
- [ ] **7.2** Add surface normals to the `Tracable.Intersect` return value so
      shading can compute `dot(N, L)`.
- [ ] **7.3** Define a spectral emission profile for fire/torchlight (Planckian
      blackbody at ~1800 K, or a measured flame spectrum).
- [ ] **7.4** Place torch point lights at fixed positions along maze walls.
- [ ] **7.5** Implement light falloff so torchlight creates warm pools with dark
      corridors between them.
- [ ] **7.6** (Optional) Add a simple animated flicker by modulating torch intensity
      with low-frequency noise.
- [ ] **7.7** (Optional) Add specular highlights for floor reflections — simple
      Blinn-Phong on the floor plane to give a glossy-floor look.

### Phase 8: Open Roof & Sky

- [ ] **8.1** Mark select maze cells as "roofless" in the `Maze` grid.
- [ ] **8.2** Remove the ceiling quad for roofless cells so rays can escape upward.
- [ ] **8.3** Implement a procedural sky model: at minimum a uniform blue hemisphere;
      ideally a Preetham or Hosek-Wilkie sky model that returns spectral radiance for
      a given direction.
- [ ] **8.4** Natural light from the sky should illuminate the open cells and bleed
      into adjacent corridors via indirect light (even a single bounce adds a lot).

### Phase 9: Rayleigh Scattering & Blue Sky

- [ ] **9.1** Implement wavelength-dependent Rayleigh scattering
      (`intensity ∝ 1/λ⁴`) for atmospheric perspective inside the maze — distant
      walls shift toward blue.
- [ ] **9.2** Combine with the sky model so the spectral renderer naturally produces
      a blue sky from white sunlight, without hard-coded colors.
- [ ] **9.3** Add a sun direction parameter that drives both the sky model and
      directional shadowing in open-roof cells.

### Phase 10: Volumetric Fog / Smoke

- [ ] **10.1** Implement ray marching through a participating medium: at each step
      along the ray, evaluate extinction (absorption + out-scattering) and
      in-scattering from lights.
- [ ] **10.2** Support a uniform fog density per cell (some corridors are foggy,
      others are clear).
- [ ] **10.3** Add spectral extinction coefficients so fog color is physically
      derived (e.g. thin smoke is bluish, thick smoke is grey-brown).
- [ ] **10.4** (Optional) Use 3D Perlin noise to vary fog density for a more organic
      smoke look.

### Phase 11: Day / Night Cycle

- [ ] **11.1** Parameterize the sun elevation angle over time, driving the sky model,
      directional light color, and intensity.
- [ ] **11.2** As the sun sets, the sky shifts from blue → orange → dark via the
      spectral sky model; torches become the dominant light source indoors.
- [ ] **11.3** At night, open-roof cells show a dark sky (optionally with simple
      star points).
- [ ] **11.4** Smoothly interpolate all lighting parameters so the transition is
      gradual over several minutes of screensaver runtime.

### Phase 12: Indirect Illumination (Stretch Goal)

- [ ] **12.1** Implement one-bounce diffuse global illumination so light bleeds
      around corners (color bleeding from tinted walls).
- [ ] **12.2** Evaluate performance; consider caching irradiance on a coarse voxel
      grid to keep frame times acceptable.

---

## Architecture Notes

```
RayTracer.Core
├── Scene
│   ├── Maze.cs              — 2D grid + generation
│   ├── MazeGeometryBuilder.cs — Grid → Tracable[] quads
│   └── MazeNavigator.cs     — Wall-following walk logic
├── Geometry
│   ├── Tracable.cs           (exists)
│   ├── Plane.cs              (exists)
│   ├── TracableRectangle.cs  (exists)
│   └── BVH.cs               — Acceleration structure
├── Shading
│   ├── Light.cs              (exists, extend)
│   ├── Sky.cs               — Procedural spectral sky
│   └── VolumetricFog.cs     — Ray marching medium
├── Spectral
│   ├── WavelengthLookup.cs   (exists)
│   ├── MaterialsLookup.cs    (exists)
│   └── BlackbodySpectrum.cs — Planckian emission curves
├── Camera.cs                  (exists)
├── Ray.cs                     (exists)
├── JobSystem.cs               (exists)
└── Matrix.cs                  (exists)

RayTracer.App
├── Program.cs / RayForm.cs    (exists, extend for screensaver mode)
└── SettingsForm.cs           — Configuration dialog

RayTracer.Tests                (exists, extend per phase)
Benchmark                      (exists, extend per phase)
```

## Guiding Principles

1. **Spectral-first** — Every material, light, and atmospheric effect is defined by
   its spectral properties. The renderer converts to sRGB only at the final display
   step.
2. **Incremental** — Each phase produces a visually verifiable result. No phase
   depends on features from a later phase.
3. **Testable** — Maze generation, geometry building, and shading math each get unit
   tests before integration.
4. **Profile before optimizing** — Use the existing `Benchmark` project to measure
   before adding acceleration structures or caching.
