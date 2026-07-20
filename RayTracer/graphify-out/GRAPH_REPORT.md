# Graph Report - .  (2026-07-20)

## Corpus Check
- Large corpus: 256 files · ~947,332 words. Semantic extraction will be expensive (many Claude tokens). Consider running on a subfolder.

## Summary
- 3828 nodes · 7814 edges · 259 communities (242 shown, 17 thin omitted)
- Extraction: 94% EXTRACTED · 6% INFERRED · 0% AMBIGUOUS · INFERRED: 454 edges (avg confidence: 0.8)
- Token cost: 186,022 input · 0 output

## Community Hubs (Navigation)
- CPU Reference Renderer & Phases
- GPU Scene Primitives & Phase1
- Maze Camera & Navigation
- DXR Acceleration Structures
- GPU Phase5 Renderer (D3D12)
- Pinball Colliders & AABB
- Lens Sampling & Camera Optics
- RayTracer Test Suite
- GPU Phase4 Renderer (D3D12)
- GPU Phase3 Renderer (D3D12)
- GPU Phase2 Renderer (D3D12)
- NuGet Lockfile (root)
- NuGet Lockfile (Pinball.Core)
- NuGet Lockfile (Maze.Core)
- Pinball App Rendering Loop
- Geometry Primitives & AABB
- NuGet Lockfile (RayTracer.Core)
- Benchmark Harness
- Ball State & Physics Integration
- Physics Actuators & Active Zones
- Phase5 Reference (Color/Tonemap)
- Maze GPU Launcher & Classic Mode
- Ray Intersection & HitInfo
- Maze Modes & Volumetrics
- Maze Program & Bitmap Output
- Accumulation & Frame Diagnostics
- GPU Device Resources (D3D12)
- Data-Driven Table Definition
- Caustics Options & Tests
- Maze Geometry Packing
- Optics & Thin-Film Shading
- Spectral Color Tests
- Swept Wall Geometry
- Shadow Transmittance & Photons
- GPU Command/Fence Resources
- Community 35
- Community 36
- Community 37
- Community 38
- Community 39
- Community 40
- Community 41
- Community 42
- Community 43
- Community 44
- Community 45
- Community 46
- Community 47
- Community 48
- Community 49
- Community 50
- Community 51
- Community 52
- Community 53
- Community 54
- Community 55
- Community 56
- Community 57
- Community 58
- Community 59
- Community 60
- Community 61
- Community 62
- Community 63
- Community 64
- Community 65
- Community 66
- Community 67
- Community 68
- Community 69
- Community 70
- Community 71
- Community 72
- Community 73
- Community 74
- Community 75
- Community 76
- Community 77
- Community 78
- Community 79
- Community 80
- Community 81
- Community 82
- Community 83
- Community 84
- Community 85
- Community 86
- Community 87
- Community 88
- Community 89
- Community 90
- Community 91
- Community 92
- Community 93
- Community 94
- Community 95
- Community 96
- Community 97
- Community 98
- Community 99
- Community 100
- Community 101
- Community 102
- Community 103
- Community 104
- Community 105
- Community 106
- Community 107
- Community 108
- Community 109
- Community 110
- Community 111
- Community 112
- Community 113
- Community 114
- Community 115
- Community 116
- Community 117
- Community 118
- Community 119
- Community 120
- Community 121
- Community 122
- Community 123
- Community 124
- Community 125
- Community 126
- Community 127
- Community 128
- Community 129
- Community 130
- Community 131
- Community 132
- Community 133
- Community 134
- Community 135
- Community 136
- Community 137
- Community 138
- Community 139
- Community 140
- Community 141
- Community 142
- Community 143
- Community 144
- Community 146
- Community 147
- Community 148
- Community 149
- Community 150
- Community 151
- Community 152
- Community 153
- Community 154
- Community 155
- Community 156
- Community 157
- Community 158
- Community 159
- Community 160
- Community 161
- Community 162
- Community 163
- Community 164
- Community 165
- Community 166
- Community 167
- Community 168
- Community 169
- Community 170
- Community 172
- Community 173
- Community 174
- Community 175
- Community 176
- Community 177
- Community 178
- Community 179
- Community 180
- Community 181
- Community 182
- Community 183
- Community 184
- Community 185
- Community 186
- Community 187
- Community 188
- Community 189
- Community 190
- Community 191
- Community 192
- Community 193
- Community 194
- Community 195
- Community 196
- Community 197
- Community 198
- Community 199
- Community 200
- Community 201
- Community 202
- Community 203
- Community 204
- Community 205
- Community 206
- Community 207
- Community 208
- Community 209
- Community 210
- Community 211
- Community 212
- Community 213
- Community 214
- Community 215
- Community 216
- Community 217
- Community 218
- Community 219
- Community 220
- Community 221
- Community 222
- Community 223
- Community 224
- Community 225
- Community 226
- Community 227
- Community 228
- Community 229
- Community 230
- Community 231
- Community 232
- Community 233
- Community 234
- Community 235
- Community 236
- Community 237
- Community 238
- Community 239
- Community 240
- Community 241
- Community 242
- Community 243
- Community 244
- Community 245
- Community 246
- Community 247
- Community 248
- Community 249
- Community 250
- Community 251
- Community 252
- Community 253
- Community 254
- Community 255
- Community 256
- Community 257

## God Nodes (most connected - your core abstractions)
1. `RayTracer` - 150 edges
2. `MaterialData` - 110 edges
3. `Phase6Renderer` - 99 edges
4. `Program` - 84 edges
5. `Vector3D` - 80 edges
6. `Tracable` - 72 edges
7. `Phase5Renderer` - 61 edges
8. `Phase4Renderer` - 57 edges
9. `Camera` - 56 edges
10. `Phase3Renderer` - 55 edges

## Surprising Connections (you probably didn't know these)
- `Trajectory Comes From Physics (governing principle)` --semantically_similar_to--> `Engine Working Conventions (CPU->reference->HLSL)`  [INFERRED] [semantically similar]
  pinball-plan.md → plan.md
- `Table Element Types (wall-bezier/line, bumper, post, flipper, slingshot, target, light)` --semantically_similar_to--> `Analytic Collider Zoo (Plane/Sphere/Cylinder/Capsule/Quad/Arc/Mesh)`  [INFERRED] [semantically similar]
  tools/table-editor.html → pinball-plan.md
- `Spectral Rendering (wavelength->XYZ, hero sampling)` --conceptually_related_to--> `Spectral / Optical Effects (mirror, dielectric, dispersion, thin-film, Beer-Lambert)`  [INFERRED]
  README.md → plan.md
- `Multiple Debug Views` --conceptually_related_to--> `Renderer Debug Options Plan (F1-F10)`  [INFERRED]
  README.md → debug-options.md
- `Space Cadet RT Table Editor (visual authoring)` --conceptually_related_to--> `Space Cadet Game Design (faithful, code-verified)`  [INFERRED]
  tools/table-editor.html → pinball-plan.md

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Render Data Flow Pipeline Components** — raytracer_core_geometry_bvh_bvh, raytracer_core_rendering_jobsystem_jobsystem, raytracer_core_rendering_pathtracer_pathtracer, raytracer_core_rendering_accumulationbuffer_accumulationbuffer, raytracer_core_rendering_taaresolver_taaresolver, raytracer_core_rendering_displayresolver_displayresolver [EXTRACTED 1.00]
- **Two-Tier Hybrid Rendering in One Dispatch** — pinball_plan_one_dispatch_fact, pinball_plan_static_tier, pinball_plan_dynamic_tier, pinball_plan_dynamic_classification, pinball_plan_temporal_machinery [EXTRACTED 1.00]
- **Physics Fidelity Model Terms** — pinball_plan_rigid_body_6dof, pinball_plan_contact_solver, pinball_plan_spin, pinball_plan_aero_drag, pinball_plan_integration_ccd, pinball_plan_actuators [EXTRACTED 1.00]

## Communities (259 total, 17 thin omitted)

### Community 0 - "CPU Reference Renderer & Phases"
Cohesion: 0.05
Nodes (27): MediumStack, float, int, ISceneTracer, ReadOnlySpan, Vector3, Phase4Reference, float (+19 more)

### Community 1 - "GPU Scene Primitives & Phase1"
Cohesion: 0.06
Nodes (34): aspectTan, ISceneTracer, float, uint, GpuPrimitive, int, Vector3, Vector4 (+26 more)

### Community 2 - "Maze Camera & Navigation"
Cohesion: 0.06
Nodes (24): dx, dy, nextHeading, nextX, nextY, ICameraDriver, float, int (+16 more)

### Community 3 - "DXR Acceleration Structures"
Cohesion: 0.06
Nodes (26): BuildRaytracingAccelerationStructureInputs, nint, Vector3, PackedLights, AutoResetEvent, bool, Format, Func (+18 more)

### Community 4 - "GPU Phase5 Renderer (D3D12)"
Cohesion: 0.06
Nodes (26): AutoResetEvent, bool, float, Format, ID3D12CommandAllocator, ID3D12CommandQueue, ID3D12DescriptorHeap, ID3D12Device5 (+18 more)

### Community 5 - "Pinball Colliders & AABB"
Cohesion: 0.06
Nodes (19): AabbD, ArcCollider, double, BezierWallCollider, CapsuleCollider, CylinderCollider, StaticCollider, MeshCollider (+11 more)

### Community 6 - "Lens Sampling & Camera Optics"
Cohesion: 0.08
Nodes (10): uint, Vector3, LensSampler, float, LensModel, LensOptions, float, TestMethod (+2 more)

### Community 7 - "RayTracer Test Suite"
Cohesion: 0.07
Nodes (7): RayTracer.Tests, RayTracer, Scene, Scene, Scene, Scene, Scene

### Community 8 - "GPU Phase4 Renderer (D3D12)"
Cohesion: 0.06
Nodes (25): AutoResetEvent, bool, float, Format, ID3D12CommandAllocator, ID3D12CommandQueue, ID3D12DescriptorHeap, ID3D12Device5 (+17 more)

### Community 9 - "GPU Phase3 Renderer (D3D12)"
Cohesion: 0.06
Nodes (25): AutoResetEvent, bool, float, Format, ID3D12CommandAllocator, ID3D12CommandQueue, ID3D12DescriptorHeap, ID3D12Device5 (+17 more)

### Community 10 - "GPU Phase2 Renderer (D3D12)"
Cohesion: 0.07
Nodes (22): AutoResetEvent, bool, float, Format, ID3D12CommandAllocator, ID3D12CommandQueue, ID3D12DescriptorHeap, ID3D12Device5 (+14 more)

### Community 11 - "NuGet Lockfile (root)"
Cohesion: 0.04
Nodes (45): contentHash, requested, resolved, type, dependencies, net10.0, net10.0/linux-x64, net10.0/win-x64 (+37 more)

### Community 12 - "NuGet Lockfile (Pinball.Core)"
Cohesion: 0.05
Nodes (42): contentHash, requested, resolved, type, dependencies, net10.0, net10.0/linux-x64, net10.0/win-x64 (+34 more)

### Community 13 - "NuGet Lockfile (Maze.Core)"
Cohesion: 0.05
Nodes (42): contentHash, requested, resolved, type, dependencies, net10.0, net10.0/linux-x64, net10.0/win-x64 (+34 more)

### Community 14 - "Pinball App Rendering Loop"
Cohesion: 0.11
Nodes (13): ballIdx, game, Bitmap, bool, float, int, Matrix3x4, Quaternion (+5 more)

### Community 15 - "Geometry Primitives & AABB"
Cohesion: 0.07
Nodes (28): MethodImpl, Vector3, AABB, float, l1, l2, l3, Vector3 (+20 more)

### Community 16 - "NuGet Lockfile (RayTracer.Core)"
Cohesion: 0.05
Nodes (40): contentHash, requested, resolved, type, dependencies, net10.0, net10.0/linux-x64, net10.0/win-x64 (+32 more)

### Community 17 - "Benchmark Harness"
Cohesion: 0.07
Nodes (38): Benchmarks, net10.0, Microsoft.NET.Sdk, .net, net10.0, Microsoft.NET.Sdk, BenchmarkDotNet, BenchmarkDotNet.Annotations (+30 more)

### Community 18 - "Ball State & Physics Integration"
Cohesion: 0.11
Nodes (11): BallState, double, TestMethod, PhysicsRobustnessTests, DataRow, double, TestMethod, PhysicsValidationTests (+3 more)

### Community 19 - "Physics Actuators & Active Zones"
Cohesion: 0.08
Nodes (16): ActiveZone, IActiveImpulse, IActuator, ICollider, double, int, Contact, double (+8 more)

### Community 20 - "Phase5 Reference (Color/Tonemap)"
Cohesion: 0.16
Nodes (7): ReadOnlySpan, Vector3, Phase5Reference, Phase5Stats, TestMethod, Vector3, GpuPhase5Tests

### Community 21 - "Maze GPU Launcher & Classic Mode"
Cohesion: 0.06
Nodes (20): RayTracer.Gpu, float, int, ClassicMode, CpuLauncher, Func, MazeDecals, Options (+12 more)

### Community 22 - "Ray Intersection & HitInfo"
Cohesion: 0.09
Nodes (10): Vector3, HitInfo, Vector3, Plane, float, TestMethod, BubbleTests, TestMethod (+2 more)

### Community 23 - "Maze Modes & Volumetrics"
Cohesion: 0.11
Nodes (14): MovieProgress, Phase5DebugMode, SmokeMode, VolumetricQuality, AppSettings, CpuCustomSettings, MovieLengthMode, MovieQualityMode (+6 more)

### Community 24 - "Maze Program & Bitmap Output"
Cohesion: 0.14
Nodes (7): Bitmap, bool, double, float, int, STAThread, Program

### Community 25 - "Accumulation & Frame Diagnostics"
Cohesion: 0.08
Nodes (24): AccumulationBuffer, Channel, DebugBufferRenderer, CpuThrottle, FrameDiagnostics, bool, DisplayResolver, float (+16 more)

### Community 26 - "GPU Device Resources (D3D12)"
Cohesion: 0.08
Nodes (18): IDisposable, passed, AutoResetEvent, Format, ID3D12CommandAllocator, ID3D12CommandQueue, ID3D12DescriptorHeap, ID3D12Device5 (+10 more)

### Community 27 - "Data-Driven Table Definition"
Cohesion: 0.10
Nodes (17): JsonSerializerOptions, double, IEnumerable, List, Wall, BallDef, BumperDef, FlipperDef (+9 more)

### Community 28 - "Caustics Options & Tests"
Cohesion: 0.18
Nodes (5): CausticOptions, CausticQuality, TestMethod, Vector3, CausticsTests

### Community 29 - "Maze Geometry Packing"
Cohesion: 0.29
Nodes (3): IReadOnlyList, IReadOnlyList, Quaternion

### Community 30 - "Optics & Thin-Film Shading"
Cohesion: 0.12
Nodes (6): float, Vector3, Optics, TestMethod, Vector3, SpectralFoundationTests

### Community 31 - "Spectral Color Tests"
Cohesion: 0.16
Nodes (7): B, G, R, DataRow, TestMethod, Vector3, SpectralColorTests

### Community 32 - "Swept Wall Geometry"
Cohesion: 0.11
Nodes (14): IEnumerable, float, l1, l2, l3, Vector3, TracableRectangle, Vector3 (+6 more)

### Community 33 - "Shadow Transmittance & Photons"
Cohesion: 0.20
Nodes (3): int, TestMethod, ShadowTransmittanceTests

### Community 34 - "GPU Command/Fence Resources"
Cohesion: 0.10
Nodes (16): AutoResetEvent, bool, Format, ID3D12CommandAllocator, ID3D12CommandQueue, ID3D12DescriptorHeap, ID3D12Device5, ID3D12Fence (+8 more)

### Community 35 - "Community 35"
Cohesion: 0.15
Nodes (11): float, Quaternion, ReadOnlySpan, Vector3, Phase3Reference, DataRow, float, int (+3 more)

### Community 36 - "Community 36"
Cohesion: 0.08
Nodes (26): contentHash, dependencies, requested, resolved, type, BenchmarkDotNet.Annotations, CommandLineParser, Gee.External.Capstone (+18 more)

### Community 37 - "Community 37"
Cohesion: 0.13
Nodes (15): center, float, IEnumerable, List, Quaternion, Vector3, MazeJewels, IReadOnlyList (+7 more)

### Community 38 - "Community 38"
Cohesion: 0.28
Nodes (6): float, List, Quaternion, Vector3, PinballTableScene, Tracable

### Community 39 - "Community 39"
Cohesion: 0.11
Nodes (8): bool, double, Plunger, double, TiltSensor, double, TestMethod, ActuatorTests

### Community 40 - "Community 40"
Cohesion: 0.16
Nodes (8): double, ContactQuery, double, TestMethod, ColliderTests, double, TestMethod, MeshArcColliderTests

### Community 41 - "Community 41"
Cohesion: 0.12
Nodes (13): CheckBox, ComboBox, GroupBox, NumericUpDown, Panel, Preset, RadioButton, int (+5 more)

### Community 42 - "Community 42"
Cohesion: 0.12
Nodes (6): Pinball.Game, Pinball.Tests, Pinball.Physics, Forces, double, PhysicsConstants

### Community 43 - "Community 43"
Cohesion: 0.18
Nodes (10): LightingMode, DataRow, direct, Func, int, TestMethod, total, Vector3 (+2 more)

### Community 44 - "Community 44"
Cohesion: 0.18
Nodes (6): DllImport, MarshalAs, Mode, long, Screensaver, RECT

### Community 45 - "Community 45"
Cohesion: 0.11
Nodes (16): emission, extinction, hitNormal, hitPoint, hitPrimitive, ior, hit, reflectance (+8 more)

### Community 46 - "Community 46"
Cohesion: 0.18
Nodes (11): Bubble, hash, origin, float, IEnumerable, IReadOnlyList, IReadOnlySet, List (+3 more)

### Community 47 - "Community 47"
Cohesion: 0.15
Nodes (10): cauchyB, p0, p1, p2, IReadOnlyList, List, Vector3, GpuScenePacker (+2 more)

### Community 48 - "Community 48"
Cohesion: 0.20
Nodes (10): CellKey, ConcurrentDictionary, float, int, long, uint, Vector3, DiffuseIrradianceCache (+2 more)

### Community 49 - "Community 49"
Cohesion: 0.23
Nodes (5): Vector3, Light, TestMethod, Vector3, SoftShadowTests

### Community 50 - "Community 50"
Cohesion: 0.16
Nodes (9): Photon, Dictionary, float, Func, IReadOnlyList, List, Vector3, PhotonMap (+1 more)

### Community 51 - "Community 51"
Cohesion: 0.13
Nodes (16): BitmapData, Brush, Font, Pen, Bitmap, bool, byte, double (+8 more)

### Community 52 - "Community 52"
Cohesion: 0.22
Nodes (4): TestMethod, AccumulationConvergenceTests, TestMethod, AccumulationSpectralTests

### Community 53 - "Community 53"
Cohesion: 0.15
Nodes (19): GPU DXR Render Phases 1-6, Chrome Ball Money Shots (mirror + contact shadow), Pinball Design Pillars & Non-goals, Dynamic Bit + Cheap Mover Shading Branch, Dynamic Tier (cheap single-bounce hero/RGB movers), Engine <-> Maze Separation (Milestone E), Hybrid Rendering Within One DXR Dispatch, Milestone E - Extract Engine from Maze (DONE) (+11 more)

### Community 54 - "Community 54"
Cohesion: 0.11
Nodes (10): DebugResolveBenchmark, Benchmark, byte, int, Program, TracableRectangles, Benchmark, int (+2 more)

### Community 55 - "Community 55"
Cohesion: 0.11
Nodes (19): net10.0, contentHash, resolved, type, contentHash, resolved, type, contentHash (+11 more)

### Community 56 - "Community 56"
Cohesion: 0.32
Nodes (7): direct, indirect, int, TestMethod, total, Vector3, GpuPhase2Tests

### Community 57 - "Community 57"
Cohesion: 0.18
Nodes (7): Quaternion, Vector3, Camera, int, TestMethod, Vector3, GpuMoverParityTests

### Community 58 - "Community 58"
Cohesion: 0.27
Nodes (5): float, MethodImpl, Vector3, DebugBufferRenderer, JobSystem

### Community 59 - "Community 59"
Cohesion: 0.15
Nodes (8): double, int, maxDiff, meanAbs, withinFraction, RegressionHarness, Func, Variant

### Community 60 - "Community 60"
Cohesion: 0.13
Nodes (18): Engine Design Intent (thin facade, focused hot loops), Render Data Flow Pipeline, Bounce Breakdown Debug (F5), Clamp Heatmap (F4), Edge Disagreement Map (F8), Renderer Debug Options Plan (F1-F10), Hit-id Restart (the ghost-trail fix), pathTouchedDynamic Flag (ball ghost in static specular) (+10 more)

### Community 61 - "Community 61"
Cohesion: 0.14
Nodes (8): Assembly, float, FrozenDictionary, IEnumerable, MaterialsLookup, SpectralData, sce, sci

### Community 62 - "Community 62"
Cohesion: 0.29
Nodes (5): GlobalSetup, Func, DataRow, TestMethod, MazeGeometryBuilderTests

### Community 63 - "Community 63"
Cohesion: 0.20
Nodes (6): Func, TestMethod, Vector3, x, y, AbsorptionTests

### Community 64 - "Community 64"
Cohesion: 0.21
Nodes (5): DataRow, int, TestMethod, Vector3, GpuPhase4Tests

### Community 65 - "Community 65"
Cohesion: 0.23
Nodes (4): HashSet, DataRow, TestMethod, MazeTests

### Community 66 - "Community 66"
Cohesion: 0.24
Nodes (7): float, Func, ISet, List, Vector3, MazeWindows, Options

### Community 67 - "Community 67"
Cohesion: 0.18
Nodes (9): DataRow, direct, Func, indirect, int, TestMethod, total, Vector3 (+1 more)

### Community 68 - "Community 68"
Cohesion: 0.20
Nodes (7): float, int, Vector3, RgbToReflectanceBasis, DataRow, TestMethod, RgbToReflectanceBasisTests

### Community 69 - "Community 69"
Cohesion: 0.21
Nodes (8): Maze, float, Func, HashSet, List, MazeHedges, HashSet, List

### Community 71 - "Community 71"
Cohesion: 0.22
Nodes (10): horizontal, layer, float, gx, gy, IReadOnlySet, List, Vector3 (+2 more)

### Community 72 - "Community 72"
Cohesion: 0.33
Nodes (5): PinballGame, PinballInput, double, TestMethod, PinballGameTests

### Community 73 - "Community 73"
Cohesion: 0.15
Nodes (16): PinballGame, PinballInput, ContactSolver, PhysicsWorld, PinballTable, Actuators (flippers, plunger, bumpers, nudge/tilt), Aerodynamic Drag & Rolling Resistance, Build vs Buy: Custom Analytic Solver (+8 more)

### Community 74 - "Community 74"
Cohesion: 0.15
Nodes (5): Flipper, PhysicsSettings, double, IReadOnlyList, PinballTable

### Community 75 - "Community 75"
Cohesion: 0.18
Nodes (15): dependencies, Microsoft.DiaSymReader, Microsoft.Extensions.DependencyModel, net10.0/linux-x64, net10.0/win-x64, Microsoft.DiaSymReader, Microsoft.Extensions.DependencyModel, contentHash (+7 more)

### Community 76 - "Community 76"
Cohesion: 0.12
Nodes (16): net10.0, contentHash, resolved, type, Microsoft.CodeCoverage, Newtonsoft.Json, raytracer, SonarAnalyzer.CSharp (+8 more)

### Community 77 - "Community 77"
Cohesion: 0.25
Nodes (7): direct, indirect, int, TestMethod, total, Vector3, GpuEmissiveParityTests

### Community 78 - "Community 78"
Cohesion: 0.33
Nodes (6): Func, TestMethod, Vector3, x, y, MirrorReflectionTests

### Community 79 - "Community 79"
Cohesion: 0.18
Nodes (15): dependencies, Microsoft.DiaSymReader, Microsoft.Extensions.DependencyModel, net10.0/linux-x64, net10.0/win-x64, Microsoft.DiaSymReader, Microsoft.Extensions.DependencyModel, contentHash (+7 more)

### Community 80 - "Community 80"
Cohesion: 0.12
Nodes (16): net10.0, contentHash, resolved, type, Microsoft.CodeCoverage, Newtonsoft.Json, raytracer, SonarAnalyzer.CSharp (+8 more)

### Community 81 - "Community 81"
Cohesion: 0.13
Nodes (15): RayTracer.Core Domain Folders, RayTracer Solution Map, Pinball.App Program (entry points), Six-Assembly Target Layout, Diffraction Grating CD/DVD Rainbow (unbuilt), Fluorescence / UV Reactivity (unbuilt), Metamerism Demo (unbuilt), Shadows & Spectral Photon-Map Caustics (+7 more)

### Community 82 - "Community 82"
Cohesion: 0.20
Nodes (5): ulong, DeterministicRandom, double, TestMethod, PhysicsPrimitivesTests

### Community 83 - "Community 83"
Cohesion: 0.15
Nodes (7): Vector3, PhotonTracer, float, FrozenDictionary, int, Vector3, WavelengthLookup

### Community 84 - "Community 84"
Cohesion: 0.24
Nodes (6): JobSystemFactory, DebugOptions, DenoiseOptions, RenderOptions, SamplingOptions, JobSystem

### Community 85 - "Community 85"
Cohesion: 0.18
Nodes (10): CancellationToken, IProgress, Task, PerformanceCalibrator, lights, scene, Task, TestMethod (+2 more)

### Community 86 - "Community 86"
Cohesion: 0.13
Nodes (15): SharpGen.Runtime, Vortice.DXGI, SharpGen.Runtime, Vortice.DXGI, SharpGen.Runtime.COM, Vortice.Direct3D12, contentHash, dependencies (+7 more)

### Community 87 - "Community 87"
Cohesion: 0.23
Nodes (6): Func, TestMethod, Vector3, x, y, GlassRefractionTests

### Community 88 - "Community 88"
Cohesion: 0.20
Nodes (8): DataRow, direct, Func, int, TestMethod, total, Vector3, GpuThinFilmTests

### Community 89 - "Community 89"
Cohesion: 0.28
Nodes (4): Task, TestContext, TestMethod, Phase5PerformanceTests

### Community 90 - "Community 90"
Cohesion: 0.14
Nodes (14): Microsoft.CodeAnalysis.Analyzers, Microsoft.CodeAnalysis.Common, contentHash, dependencies, resolved, type, contentHash, dependencies (+6 more)

### Community 91 - "Community 91"
Cohesion: 0.14
Nodes (14): Microsoft.Diagnostics.NETCore.Client, System.Reflection.TypeExtensions, contentHash, dependencies, resolved, type, contentHash, dependencies (+6 more)

### Community 92 - "Community 92"
Cohesion: 0.14
Nodes (14): Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Primitives, contentHash, dependencies, resolved, type, contentHash, dependencies (+6 more)

### Community 93 - "Community 93"
Cohesion: 0.20
Nodes (8): camera, lights, scene, Action, lights, scene, TestMethod, JobSystemOptionsValidationTests

### Community 94 - "Community 94"
Cohesion: 0.14
Nodes (14): Microsoft.NET.Test.Sdk, Microsoft.Testing.Extensions.CodeCoverage, Microsoft.Testing.Extensions.TrxReport, MSTest.TestAdapter, Microsoft.NET.Test.Sdk, Microsoft.Testing.Extensions.CodeCoverage, Microsoft.Testing.Extensions.TrxReport, MSTest.TestAdapter (+6 more)

### Community 95 - "Community 95"
Cohesion: 0.14
Nodes (14): Microsoft.Testing.Extensions.Telemetry, Microsoft.Testing.Extensions.TrxReport.Abstractions, Microsoft.Testing.Extensions.Telemetry, Microsoft.Testing.Extensions.TrxReport.Abstractions, contentHash, dependencies, resolved, type (+6 more)

### Community 96 - "Community 96"
Cohesion: 0.18
Nodes (8): Vector3, LightCones, float, Matrix3x3, float, MethodImpl, Vector3, JobSystem

### Community 97 - "Community 97"
Cohesion: 0.15
Nodes (3): uint, AccumulationBuffer, JobSystem

### Community 98 - "Community 98"
Cohesion: 0.25
Nodes (7): float, MethodImpl, Span, Vector3, DisplayResolver, JobSystem, ToneMapping

### Community 99 - "Community 99"
Cohesion: 0.14
Nodes (14): Microsoft.NET.Test.Sdk, Microsoft.Testing.Extensions.CodeCoverage, Microsoft.Testing.Extensions.TrxReport, MSTest.TestAdapter, Microsoft.NET.Test.Sdk, Microsoft.Testing.Extensions.CodeCoverage, Microsoft.Testing.Extensions.TrxReport, MSTest.TestAdapter (+6 more)

### Community 100 - "Community 100"
Cohesion: 0.14
Nodes (14): Microsoft.Testing.Extensions.Telemetry, Microsoft.Testing.Extensions.TrxReport.Abstractions, Microsoft.Testing.Extensions.Telemetry, Microsoft.Testing.Extensions.TrxReport.Abstractions, contentHash, dependencies, resolved, type (+6 more)

### Community 101 - "Community 101"
Cohesion: 0.17
Nodes (6): GpuEmitJob, Bounds, IReadOnlyList, Photons, Seed, Vector3

### Community 102 - "Community 102"
Cohesion: 0.15
Nodes (8): CancellationToken, Task, CancellationToken, DisplayResolver, PathTracer, Task, JobSystem, TileScheduler

### Community 103 - "Community 103"
Cohesion: 0.22
Nodes (7): float, MethodImpl, Quaternion, Stopwatch, Vector3, JobSystem, TaaResolver

### Community 104 - "Community 104"
Cohesion: 0.31
Nodes (4): TestContext, TestMethod, Phase4TestingExpansionTests, TestCategory

### Community 105 - "Community 105"
Cohesion: 0.18
Nodes (12): C# Best-Practices Instructions, No-Nullables / Explicit-Usings Convention, CI Build-Test Workflow (.NET 10, windows-latest), BVH Intersect Benchmark Result (30.16 ms), Contributing Build/Test/Run Guide, Analytic Collider Zoo (Plane/Sphere/Cylinder/Capsule/Quad/Arc/Mesh), AABB, BVH (+4 more)

### Community 106 - "Community 106"
Cohesion: 0.35
Nodes (4): Color, Action, Graphics, PropTextures

### Community 107 - "Community 107"
Cohesion: 0.26
Nodes (6): Gh, Grid, Gw, MazeMinimap, TestMethod, MazeMinimapTests

### Community 108 - "Community 108"
Cohesion: 0.17
Nodes (12): net10.0-windows7.0, Qowaiv.Analyzers.CSharp, raytracer, SharpGen.Runtime, contentHash, requested, resolved, type (+4 more)

### Community 109 - "Community 109"
Cohesion: 0.17
Nodes (12): RayTracer, Vortice.Direct3D12, Vortice.Dxc, RayTracer, Vortice.Direct3D12, Vortice.Dxc, pinball.core, raytracer.gpu (+4 more)

### Community 110 - "Community 110"
Cohesion: 0.32
Nodes (3): GameState, TestMethod, GameStateTests

### Community 111 - "Community 111"
Cohesion: 0.17
Nodes (12): Microsoft.Testing.Platform, Microsoft.Testing.Platform, contentHash, dependencies, resolved, type, contentHash, dependencies (+4 more)

### Community 112 - "Community 112"
Cohesion: 0.17
Nodes (7): float, Vector3, Ray, Dictionary, int, Vector3, BvhSceneTracer

### Community 113 - "Community 113"
Cohesion: 0.17
Nodes (12): net10.0-windows7.0, Qowaiv.Analyzers.CSharp, raytracer, SharpGen.Runtime, contentHash, requested, resolved, type (+4 more)

### Community 114 - "Community 114"
Cohesion: 0.17
Nodes (12): RayTracer, Vortice.Direct3D12, Vortice.Dxc, RayTracer, Vortice.Direct3D12, Vortice.Dxc, raytracer.gpu, raytracer.maze.core (+4 more)

### Community 115 - "Community 115"
Cohesion: 0.17
Nodes (12): Microsoft.Testing.Platform, Microsoft.Testing.Platform, contentHash, dependencies, resolved, type, contentHash, dependencies (+4 more)

### Community 116 - "Community 116"
Cohesion: 0.20
Nodes (5): GlobalSetup, Random, float, List, MazeGeometryBuilder

### Community 117 - "Community 117"
Cohesion: 0.27
Nodes (10): dependencies, net10.0/linux-x64, net10.0/win-x64, contentHash, resolved, type, Gee.External.Capstone, Gee.External.Capstone (+2 more)

### Community 118 - "Community 118"
Cohesion: 0.18
Nodes (11): Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Options, contentHash, dependencies, resolved, type, Microsoft.Extensions.Logging (+3 more)

### Community 119 - "Community 119"
Cohesion: 0.18
Nodes (7): Button, Form, ProgressBar, Bitmap, Label, PictureBox, MovieProgressForm

### Community 120 - "Community 120"
Cohesion: 0.20
Nodes (8): nx, ny, opposite, bool, Span, MazeCell, Wall, wall

### Community 121 - "Community 121"
Cohesion: 0.27
Nodes (10): dependencies, net10.0-windows7.0/linux-x64, net10.0-windows7.0/win-x64, Vortice.Dxc.Native, Vortice.Dxc.Native, Vortice.Dxc.Native, version, contentHash (+2 more)

### Community 122 - "Community 122"
Cohesion: 0.25
Nodes (6): double, int, maxDiff, meanAbs, withinFraction, TableRegression

### Community 123 - "Community 123"
Cohesion: 0.18
Nodes (11): PinballTableScene, TableDefinition, Emissive-Surface Shading (new, largest renderer add), P0 - Static Table Render (no gameplay), P1 - Dynamic Classification + Cheap Mover Branch, P2 - Hit-id Restart (ghost-trail fix), P3 - Mover TAA Stencil, Temporal Machinery: Keep for Static, Bypass for Movers (+3 more)

### Community 124 - "Community 124"
Cohesion: 0.42
Nodes (3): double, TestMethod, BezierColliderTests

### Community 125 - "Community 125"
Cohesion: 0.20
Nodes (11): Classic Preset (60 fps), RTX 3070 Performance Budget & Presets, RT Showcase Preset (30-60 fps), Camera Lens Models (pinhole/phone/simple/cine), Outdoor Garden Half (O0-O10), RayTracer Remaining Work (single source of truth), Screensaver <-> Windowed-App Unification, Water Pools High Tier (ripples + caustics) (+3 more)

### Community 126 - "Community 126"
Cohesion: 0.18
Nodes (11): Microsoft.Testing.Extensions.VSTestBridge, Microsoft.Testing.Platform.MSBuild, MSTest.TestFramework, Microsoft.Testing.Extensions.VSTestBridge, Microsoft.Testing.Platform.MSBuild, MSTest.TestFramework, contentHash, dependencies (+3 more)

### Community 127 - "Community 127"
Cohesion: 0.45
Nodes (3): TestMethod, Vector3, SphereTests

### Community 128 - "Community 128"
Cohesion: 0.27
Nodes (10): dependencies, net10.0-windows7.0/linux-x64, net10.0-windows7.0/win-x64, Vortice.Dxc.Native, Vortice.Dxc.Native, Vortice.Dxc.Native, version, contentHash (+2 more)

### Community 129 - "Community 129"
Cohesion: 0.18
Nodes (11): net10.0-windows7.0, raytracer, SharpGen.Runtime, Vortice.Mathematics, type, contentHash, resolved, type (+3 more)

### Community 130 - "Community 130"
Cohesion: 0.36
Nodes (10): float, int, uint, CausticEmitConstants, GpuCausticPhoton, GpuEmitJob, ReduceConstants, ResolveConstants (+2 more)

### Community 131 - "Community 131"
Cohesion: 0.27
Nodes (10): dependencies, net10.0-windows7.0/linux-x64, net10.0-windows7.0/win-x64, Vortice.Dxc.Native, Vortice.Dxc.Native, Vortice.Dxc.Native, version, contentHash (+2 more)

### Community 132 - "Community 132"
Cohesion: 0.18
Nodes (11): Microsoft.Testing.Extensions.VSTestBridge, Microsoft.Testing.Platform.MSBuild, MSTest.TestFramework, Microsoft.Testing.Extensions.VSTestBridge, Microsoft.Testing.Platform.MSBuild, MSTest.TestFramework, contentHash, dependencies (+3 more)

### Community 133 - "Community 133"
Cohesion: 0.27
Nodes (5): BVHBenchmarks, Benchmark, GlobalSetup, int, Vector3

### Community 134 - "Community 134"
Cohesion: 0.27
Nodes (6): FlatNode, int, List, Vector3, BVH, FlatNode

### Community 135 - "Community 135"
Cohesion: 0.27
Nodes (3): IEquatable, double, QuaternionD

### Community 136 - "Community 136"
Cohesion: 0.27
Nodes (10): GameState, Game State & Scoring (ranks/missions/replay/tilt), Mission System (select -> accept -> complete, 17 missions), P7 - Game State, Missions, Scoring, Replay, Tilt, P8 - RT Showcase Preset + Spectral Polish + Bloom, P9 - Audio + Attract Mode + Finish, Nine-Rank Starfleet Career Ladder, Replay Free-Ball Save (marquee mechanic) (+2 more)

### Community 137 - "Community 137"
Cohesion: 0.22
Nodes (7): float, gx, gy, IEnumerable, List, Vector3, MazeWater

### Community 138 - "Community 138"
Cohesion: 0.29
Nodes (5): TestMethod, Vector3, x, y, GiNeutralityTests

### Community 139 - "Community 139"
Cohesion: 0.31
Nodes (6): lights, scene, Task, TestContext, TestMethod, Phase0SmokeTests

### Community 140 - "Community 140"
Cohesion: 0.39
Nodes (9): System.CodeDom, System.Management, System.Management, System.Management, contentHash, dependencies, resolved, type (+1 more)

### Community 141 - "Community 141"
Cohesion: 0.33
Nodes (4): SmokeBenchmarks, Benchmark, int, Vector3

### Community 142 - "Community 142"
Cohesion: 0.22
Nodes (9): SharpGen.Runtime.COM, Vortice.Mathematics, SharpGen.Runtime.COM, Vortice.Mathematics, Vortice.DirectX, contentHash, dependencies, resolved (+1 more)

### Community 143 - "Community 143"
Cohesion: 0.22
Nodes (9): Microsoft.CodeCoverage, Microsoft.TestPlatform.TestHost, Microsoft.CodeCoverage, Microsoft.TestPlatform.TestHost, contentHash, dependencies, resolved, type (+1 more)

### Community 144 - "Community 144"
Cohesion: 0.22
Nodes (9): Microsoft.TestPlatform.ObjectModel, Newtonsoft.Json, Microsoft.TestPlatform.ObjectModel, Newtonsoft.Json, contentHash, dependencies, resolved, type (+1 more)

### Community 146 - "Community 146"
Cohesion: 0.22
Nodes (7): CellKey, CellState, float, int, uint, Vector3, IrradianceCacheEntry

### Community 147 - "Community 147"
Cohesion: 0.22
Nodes (9): SharpGen.Runtime.COM, Vortice.Mathematics, SharpGen.Runtime.COM, Vortice.Mathematics, Vortice.DirectX, contentHash, dependencies, resolved (+1 more)

### Community 148 - "Community 148"
Cohesion: 0.22
Nodes (9): SharpGen.Runtime.COM, Vortice.Mathematics, SharpGen.Runtime.COM, Vortice.Mathematics, Vortice.DirectX, contentHash, dependencies, resolved (+1 more)

### Community 149 - "Community 149"
Cohesion: 0.28
Nodes (3): IReadOnlyList, List, Vector3

### Community 150 - "Community 150"
Cohesion: 0.22
Nodes (9): Microsoft.CodeCoverage, Microsoft.TestPlatform.TestHost, Microsoft.CodeCoverage, Microsoft.TestPlatform.TestHost, contentHash, dependencies, resolved, type (+1 more)

### Community 151 - "Community 151"
Cohesion: 0.22
Nodes (9): Microsoft.TestPlatform.ObjectModel, Newtonsoft.Json, Microsoft.TestPlatform.ObjectModel, Newtonsoft.Json, contentHash, dependencies, resolved, type (+1 more)

### Community 152 - "Community 152"
Cohesion: 0.25
Nodes (8): BenchmarkDotNet, contentHash, dependencies, requested, resolved, type, Microsoft.VisualStudio.DiagnosticsHub.BenchmarkDotNetDiagnosers, BenchmarkDotNet

### Community 153 - "Community 153"
Cohesion: 0.25
Nodes (6): Count, float, uint, GpuSphere, PackedScene, Start

### Community 154 - "Community 154"
Cohesion: 0.25
Nodes (6): level, phase, gx, gy, IEnumerable, List

### Community 155 - "Community 155"
Cohesion: 0.25
Nodes (8): Vortice.Dxc.Native, Vortice.Dxc.Native, Vortice.Dxc, contentHash, dependencies, requested, resolved, type

### Community 156 - "Community 156"
Cohesion: 0.25
Nodes (8): Vortice.DXGI, Vortice.DXGI, Vortice.Direct3D12, contentHash, dependencies, requested, resolved, type

### Community 157 - "Community 157"
Cohesion: 0.32
Nodes (4): Func, Vector3, CausticGrid, CausticReference

### Community 158 - "Community 158"
Cohesion: 0.25
Nodes (8): Vortice.Dxc.Native, Vortice.Dxc.Native, Vortice.Dxc, contentHash, dependencies, requested, resolved, type

### Community 159 - "Community 159"
Cohesion: 0.39
Nodes (4): ISet, List, Vector3, MazeMirrors

### Community 160 - "Community 160"
Cohesion: 0.25
Nodes (8): Vortice.Dxc.Native, Vortice.Dxc.Native, Vortice.Dxc, contentHash, dependencies, requested, resolved, type

### Community 161 - "Community 161"
Cohesion: 0.25
Nodes (8): Vortice.DXGI, Vortice.DXGI, Vortice.Direct3D12, contentHash, dependencies, requested, resolved, type

### Community 163 - "Community 163"
Cohesion: 0.29
Nodes (7): Microsoft.Extensions.Logging, contentHash, dependencies, resolved, type, Microsoft.Diagnostics.NETCore.Client, Microsoft.Extensions.Logging

### Community 164 - "Community 164"
Cohesion: 0.29
Nodes (7): Pragmastat, Perfolizer, contentHash, dependencies, resolved, type, Pragmastat

### Community 165 - "Community 165"
Cohesion: 0.29
Nodes (7): SharpGen.Runtime, SharpGen.Runtime, SharpGen.Runtime.COM, contentHash, dependencies, resolved, type

### Community 166 - "Community 166"
Cohesion: 0.29
Nodes (7): Vortice.DirectX, Vortice.DirectX, Vortice.DXGI, contentHash, dependencies, resolved, type

### Community 167 - "Community 167"
Cohesion: 0.29
Nodes (7): Microsoft.ApplicationInsights, Microsoft.ApplicationInsights, contentHash, dependencies, resolved, type, Microsoft.Testing.Extensions.Telemetry

### Community 168 - "Community 168"
Cohesion: 0.29
Nodes (7): MSTest.Analyzers, MSTest.Analyzers, contentHash, dependencies, resolved, type, MSTest.TestFramework

### Community 169 - "Community 169"
Cohesion: 0.29
Nodes (7): Vortice.DirectX, Vortice.DirectX, Vortice.DXGI, contentHash, dependencies, resolved, type

### Community 170 - "Community 170"
Cohesion: 0.33
Nodes (3): ID3D12Resource, ReadOnlySpan, ResourceStates

### Community 172 - "Community 172"
Cohesion: 0.29
Nodes (7): SharpGen.Runtime, SharpGen.Runtime, SharpGen.Runtime.COM, contentHash, dependencies, resolved, type

### Community 173 - "Community 173"
Cohesion: 0.29
Nodes (7): Vortice.DirectX, Vortice.DirectX, Vortice.DXGI, contentHash, dependencies, resolved, type

### Community 174 - "Community 174"
Cohesion: 0.29
Nodes (7): Microsoft.ApplicationInsights, Microsoft.ApplicationInsights, contentHash, dependencies, resolved, type, Microsoft.Testing.Extensions.Telemetry

### Community 175 - "Community 175"
Cohesion: 0.29
Nodes (7): MSTest.Analyzers, MSTest.Analyzers, contentHash, dependencies, resolved, type, MSTest.TestFramework

### Community 178 - "Community 178"
Cohesion: 0.40
Nodes (3): ID3D12Resource, ReadOnlySpan, ResourceStates

### Community 179 - "Community 179"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, AsyncFixer

### Community 180 - "Community 180"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, BenchmarkDotNet.Annotations

### Community 181 - "Community 181"
Cohesion: 0.40
Nodes (5): RayTracer, RayTracer, raytracer.maze.core, dependencies, type

### Community 182 - "Community 182"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, DotNetProjectFile.Analyzers

### Community 183 - "Community 183"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, IDisposableAnalyzers

### Community 184 - "Community 184"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.CodeAnalysis.NetAnalyzers

### Community 185 - "Community 185"
Cohesion: 0.40
Nodes (5): Qowaiv.Analyzers.CSharp, contentHash, requested, resolved, type

### Community 186 - "Community 186"
Cohesion: 0.40
Nodes (5): SerilogAnalyzer, contentHash, requested, resolved, type

### Community 187 - "Community 187"
Cohesion: 0.40
Nodes (5): SonarAnalyzer.CSharp, contentHash, requested, resolved, type

### Community 189 - "Community 189"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, AsyncFixer

### Community 190 - "Community 190"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, DotNetProjectFile.Analyzers

### Community 191 - "Community 191"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, IDisposableAnalyzers

### Community 192 - "Community 192"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.CodeAnalysis.NetAnalyzers

### Community 193 - "Community 193"
Cohesion: 0.40
Nodes (5): SerilogAnalyzer, contentHash, requested, resolved, type

### Community 194 - "Community 194"
Cohesion: 0.40
Nodes (5): SonarAnalyzer.CSharp, contentHash, requested, resolved, type

### Community 195 - "Community 195"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, AsyncFixer

### Community 196 - "Community 196"
Cohesion: 0.40
Nodes (5): RayTracer, RayTracer, pinball.core, dependencies, type

### Community 197 - "Community 197"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, DotNetProjectFile.Analyzers

### Community 198 - "Community 198"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, IDisposableAnalyzers

### Community 199 - "Community 199"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.CodeAnalysis.NetAnalyzers

### Community 200 - "Community 200"
Cohesion: 0.40
Nodes (5): Qowaiv.Analyzers.CSharp, contentHash, requested, resolved, type

### Community 201 - "Community 201"
Cohesion: 0.40
Nodes (5): SerilogAnalyzer, contentHash, requested, resolved, type

### Community 205 - "Community 205"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, AsyncFixer

### Community 206 - "Community 206"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, DotNetProjectFile.Analyzers

### Community 207 - "Community 207"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, IDisposableAnalyzers

### Community 208 - "Community 208"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.CodeAnalysis.NetAnalyzers

### Community 209 - "Community 209"
Cohesion: 0.40
Nodes (5): Qowaiv.Analyzers.CSharp, contentHash, requested, resolved, type

### Community 210 - "Community 210"
Cohesion: 0.40
Nodes (5): SerilogAnalyzer, contentHash, requested, resolved, type

### Community 211 - "Community 211"
Cohesion: 0.40
Nodes (5): SonarAnalyzer.CSharp, contentHash, requested, resolved, type

### Community 212 - "Community 212"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, AsyncFixer

### Community 213 - "Community 213"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, DotNetProjectFile.Analyzers

### Community 214 - "Community 214"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, IDisposableAnalyzers

### Community 215 - "Community 215"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.CodeAnalysis.NetAnalyzers

### Community 216 - "Community 216"
Cohesion: 0.40
Nodes (5): SerilogAnalyzer, contentHash, requested, resolved, type

### Community 217 - "Community 217"
Cohesion: 0.40
Nodes (5): SonarAnalyzer.CSharp, contentHash, requested, resolved, type

### Community 219 - "Community 219"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, AsyncFixer

### Community 220 - "Community 220"
Cohesion: 0.40
Nodes (5): RayTracer, RayTracer, raytracer.maze.core, dependencies, type

### Community 221 - "Community 221"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, DotNetProjectFile.Analyzers

### Community 222 - "Community 222"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, IDisposableAnalyzers

### Community 223 - "Community 223"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.CodeAnalysis.NetAnalyzers

### Community 224 - "Community 224"
Cohesion: 0.40
Nodes (5): Qowaiv.Analyzers.CSharp, contentHash, requested, resolved, type

### Community 225 - "Community 225"
Cohesion: 0.40
Nodes (5): SerilogAnalyzer, contentHash, requested, resolved, type

### Community 226 - "Community 226"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, CommandLineParser

### Community 227 - "Community 227"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.CodeAnalysis.Analyzers

### Community 228 - "Community 228"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.DotNet.PlatformAbstractions

### Community 229 - "Community 229"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Primitives

### Community 230 - "Community 230"
Cohesion: 0.50
Nodes (4): Pragmastat, contentHash, resolved, type

### Community 231 - "Community 231"
Cohesion: 0.50
Nodes (4): System.Reflection.TypeExtensions, contentHash, resolved, type

### Community 233 - "Community 233"
Cohesion: 0.50
Nodes (4): Vortice.Mathematics, contentHash, resolved, type

### Community 234 - "Community 234"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.ApplicationInsights

### Community 235 - "Community 235"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.DiaSymReader

### Community 236 - "Community 236"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.DependencyModel

### Community 237 - "Community 237"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Testing.Platform

### Community 238 - "Community 238"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.TestPlatform.ObjectModel

### Community 239 - "Community 239"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, MSTest.Analyzers

### Community 240 - "Community 240"
Cohesion: 0.50
Nodes (3): float, GpuLight, LightPacker

### Community 242 - "Community 242"
Cohesion: 0.50
Nodes (3): float, uint, Phase1Constants

### Community 243 - "Community 243"
Cohesion: 0.50
Nodes (4): Vortice.Mathematics, contentHash, resolved, type

### Community 244 - "Community 244"
Cohesion: 0.50
Nodes (3): int, Mode, RECT

### Community 245 - "Community 245"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.ApplicationInsights

### Community 246 - "Community 246"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.DiaSymReader

### Community 247 - "Community 247"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.DependencyModel

### Community 248 - "Community 248"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Testing.Platform

### Community 249 - "Community 249"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.TestPlatform.ObjectModel

### Community 250 - "Community 250"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, MSTest.Analyzers

### Community 252 - "Community 252"
Cohesion: 0.67
Nodes (3): PhysicsConstants, BallState (6-DOF), 6-DOF Ball Rigid-Body Model (double precision)

## Knowledge Gaps
- **740 isolated node(s):** `net10.0`, `DotNetProjectFile.Analyzers.Sdk`, `Microsoft.NET.Sdk`, `net10.0`, `BenchmarkDotNet` (+735 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **17 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `RayTracer` connect `RayTracer Test Suite` to `CPU Reference Renderer & Phases`, `GPU Scene Primitives & Phase1`, `Community 256`, `Maze Camera & Navigation`, `GPU Phase5 Renderer (D3D12)`, `Community 130`, `Community 134`, `Lens Sampling & Camera Optics`, `GPU Phase4 Renderer (D3D12)`, `GPU Phase3 Renderer (D3D12)`, `GPU Phase2 Renderer (D3D12)`, `Geometry Primitives & AABB`, `Community 146`, `Phase5 Reference (Color/Tonemap)`, `Maze GPU Launcher & Classic Mode`, `Ray Intersection & HitInfo`, `Maze Modes & Volumetrics`, `Community 153`, `Accumulation & Frame Diagnostics`, `Caustics Options & Tests`, `Community 157`, `Optics & Thin-Film Shading`, `Community 35`, `Community 37`, `Community 43`, `Community 47`, `Community 177`, `Community 49`, `Community 50`, `Community 54`, `Community 57`, `Community 58`, `Community 188`, `Community 61`, `Community 68`, `Community 202`, `Community 203`, `Community 204`, `Community 83`, `Community 84`, `Community 85`, `Community 96`, `Community 97`, `Community 98`, `Community 102`, `Community 103`, `Community 232`, `Community 107`, `Community 112`, `Community 240`, `Community 241`, `Community 242`, `Community 116`, `Community 244`, `Community 120`, `Community 253`, `Community 255`?**
  _High betweenness centrality (0.114) - this node is a cross-community bridge._
- **Why does `Camera` connect `Community 57` to `CPU Reference Renderer & Phases`, `GPU Scene Primitives & Phase1`, `Maze Camera & Navigation`, `DXR Acceleration Structures`, `GPU Phase5 Renderer (D3D12)`, `Lens Sampling & Camera Optics`, `GPU Phase4 Renderer (D3D12)`, `GPU Phase3 Renderer (D3D12)`, `GPU Phase2 Renderer (D3D12)`, `Community 139`, `Pinball App Rendering Loop`, `Maze GPU Launcher & Classic Mode`, `Maze Modes & Volumetrics`, `Accumulation & Frame Diagnostics`, `Caustics Options & Tests`, `GPU Command/Fence Resources`, `Community 35`, `Community 37`, `Community 43`, `Community 56`, `Community 64`, `Community 67`, `Community 77`, `Community 84`, `Community 85`, `Community 88`, `Community 93`?**
  _High betweenness centrality (0.048) - this node is a cross-community bridge._
- **Why does `MaterialData` connect `Swept Wall Geometry` to `GPU Scene Primitives & Phase1`, `Community 137`, `Community 138`, `Pinball App Rendering Loop`, `Geometry Primitives & AABB`, `Community 149`, `Ray Intersection & HitInfo`, `Community 153`, `Caustics Options & Tests`, `Maze Geometry Packing`, `Optics & Thin-Film Shading`, `Community 159`, `Shadow Transmittance & Photons`, `Community 37`, `Community 38`, `Community 43`, `Community 46`, `Community 47`, `Community 49`, `Community 61`, `Community 62`, `Community 63`, `Community 64`, `Community 66`, `Community 67`, `Community 69`, `Community 71`, `Community 77`, `Community 78`, `Community 87`, `Community 88`, `Community 127`?**
  _High betweenness centrality (0.045) - this node is a cross-community bridge._
- **What connects `net10.0`, `DotNetProjectFile.Analyzers.Sdk`, `Microsoft.NET.Sdk` to the rest of the system?**
  _740 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `CPU Reference Renderer & Phases` be split into smaller, more focused modules?**
  _Cohesion score 0.05112347969490827 - nodes in this community are weakly interconnected._
- **Should `GPU Scene Primitives & Phase1` be split into smaller, more focused modules?**
  _Cohesion score 0.06274509803921569 - nodes in this community are weakly interconnected._
- **Should `Maze Camera & Navigation` be split into smaller, more focused modules?**
  _Cohesion score 0.0649692712906058 - nodes in this community are weakly interconnected._