# Space Cadet RT — a Spectral Ray-Traced Remake of *3D Pinball for Windows*

The single source of truth for the pinball-remake effort. This is a **planning
document** — no code yet. It sits alongside the engine's own `plan.md` (the
RayTracer remaining-work tracker) and inherits its working conventions verbatim.

---

## 1. Vision

Rebuild *3D Pinball for Windows – Space Cadet* — the Starfleet-career space table
Microsoft bundled from Plus! 95 through XP — as a faithful, playable game that
**reuses the existing DXR 1.1 spectral ray tracer as its rendering foundation**.
The bet is simple and specific to pinball: the camera is **fixed** (or slowly
panning), so the whole static playfield — table surface, ramps, the glass dome,
the neon inserts and wormhole glows — can be rendered by the engine's full
spectral path (`PathTracePhase6.hlsl`: NEE + one-bounce indirect + caustics +
volumetric haze + lens/DoF) and **converge over many frames** to a near-offline
look at real-time cost. That is exactly the workload the engine's accumulation +
TAA machinery was built for; pinball is the mirror image of the maze (there the
scene was static and the *camera* moved — here the camera is static and a few
*parts* move).

The moving parts — the chrome ball, the two flippers, the plunger, gates and
spinners — get a **cheap real-time tier**: single-bounce, hero/RGB, capped
accumulation, deliberately **not** spectral path traced. The two shots that sell
"ray-traced pinball" — the ball as a **mirror of the converged environment** and
its **contact shadow onto the table** — already fall out of the engine's shipped
sphere/`isMover` machinery for free. The result: an authentic Space Cadet you can
play, wrapped in a spectral render nobody expects from a pinball game.

---

## 2. Design pillars & non-goals

### Pillars

1. **Faithful to the Windows table, not the arcade parent.** We rebuild the
   *bundled 3D Pinball* rules (no multiball; "replay" free balls), not the retail
   *Full Tilt! Pinball* rules. Where fan guides describe multiball or
   mission-on-replay resets, the Windows behaviour wins (§3).
2. **Reuse the engine; don't fork it.** The renderer is the shipped Phase 6 path.
   New rendering work is *additive and gated*, landing CPU → C# reference → HLSL,
   and the no-mover path stays byte-identical (§4, §9).
3. **Static playfield = the full RT look; moving parts = a cheap tier.** This is
   the central rendering decision and the reason the game is feasible on a 3070
   in real time (§4).
4. **Spectral-native colour.** Neon, wormholes, the glass dome, anodized ramps,
   oil-slick bumper caps — all get their colour from physics (emission spectra,
   dispersion, thin-film, Beer–Lambert), never hard-coded tints, exactly as the
   engine already does for the maze.
5. **Gate on cost, expose via presets.** A **Classic** preset hits 60 fps; an
   **RT Showcase** preset trades frame rate for the full spectral spread. Lean on
   accumulation/TAA to average noise on the static half.
6. **Determinism where it counts.** Fixed-seed captures for regression; the
   physics runs on a fixed timestep decoupled from the (variable) converge rate.
7. **Physics fidelity is a pillar, on par with the renderer.** We built an
   unreasonably realistic light engine; the physics is held to the same bar —
   actual gravity on a real inclined playfield, a full 3-D 6-DOF ball, real
   spin / friction / restitution, Magnus and drag, all from measured SI constants
   (§6.1). Where physically-honest simulation diverges from the 1995 game's
   hand-tuned feel, **realism wins** (§6.1.9); an optional arcade-feel mode, if
   ever wanted, is a separate layer on top of the honest sim, never a corruption
   of it.

### Non-goals

- **Moving parts are NOT spectral path traced — by design.** Fast movers break
  temporal reuse: a ball crossing a pixel in 2–3 frames can never accumulate
  spectral samples, and forcing the whole frame into motion mode would throw away
  the converged static image that is the entire payoff of reusing this engine.
  Movers therefore ride a bounded temporal window (`MotionSampleCap`) with cheap
  single-bounce shading. This is a firm decision, not a stopgap.
- **No multiball.** The Windows table has none; we do not add it. Only **one**
  fast mover (plus two flippers) is ever live, which is also what makes the cheap
  tier affordable.
- **No rasterizer / deferred G-buffer.** There is none in the repo and we do not
  add one; the hybrid lives *inside the single DXR dispatch* (§4.0).
- **No networked/online play, no table editor, no VR** in scope.
- **No second table** initially — Space Cadet only.
- **No original game assets / `pinball.dat`.** We do not use Microsoft's
  `pinball.dat` (its art, depth maps, strings, table geometry, physics-material
  data) or the original audio. The table is recreated **clean-room** and physics
  uses **measured SI constants** (§6.1); only the MIT reimplementation's *code*
  constants informed §3.

---

## 3. Game design: the original *Space Cadet* (faithful, condensed)

*Reference for the Windows-bundled version — the "Space Cadet" table extracted
from Cinematronics/Maxis **Full Tilt! Pinball** (1995). Where the Windows build
diverges from retail Full Tilt!, the Windows behaviour is authoritative and is
called out. **The scoring, mission and rank constants below are code-verified**
against the MIT-licensed reverse-engineered reimplementation
[`k4zmu2a/SpaceCadetPinball`](https://github.com/k4zmu2a/SpaceCadetPinball)
(`SpaceCadetPinball/control.cpp`, read at full fidelity) — not from fan guides.
Only Microsoft's copyrighted `pinball.dat` / audio / art assets are out of scope
(see §10 for what still derives from that data file).*

### 3.1 Theme & framing — the Space Cadet career

Single-player **Starfleet-style military career**. You start as **Cadet** and are
promoted up a **nine-rank ladder** by flying themed "missions." Rank — not raw
score — is the spine of progression.

| # | Rank | Rank lights lit |
|---|------|-----------------|
| 1 | Cadet | 1 |
| 2 | Ensign | 2 |
| 3 | Lieutenant | 3 |
| 4 | Captain | 4 |
| 5 | Lieutenant Commander | 5 |
| 6 | Commander | 6 |
| 7 | Commodore | 7 |
| 8 | Admiral | 8 |
| 9 | Fleet Admiral (max) | 9 |

The lower-centre carries **two concentric rings around the central Gravity Well**,
which in code are the `middle_circle` and `outer_circle` light groups. The
**`middle_circle` on-count *is* the current rank** (1–9); the **`outer_circle` is
the progress ring**. Each completed mission calls `AddRankProgress(N)`, lighting
**N** progress lights; when the `outer_circle` **fills, it resets and the
`middle_circle` gains one light — a promotion** (hard-capped at rank 9), with a
three-quarter-full "almost there" pulse. Mission awards run **6 → 18** lights, so a
promotion is typically 2–3 missions (min award 6, 18-light ring → ⌈18/6⌉ = 3; the
18 is an assumed `pinball.dat`/community value, the code only checks "ring full", so
the cadence shifts if the true count differs); **Maelstrom's 18-light award fills the ring in
one shot** (instant promotion to Fleet Admiral). The ring holds **18** progress
lights (the community / `pinball.dat` value; the code only requires "ring full",
and Maelstrom's 18 guarantees that).

### 3.2 Missions

With no mission active the status prompts **"Hit Mission Targets to Select
Mission."** Loop: **select** by hitting the 3 **Mission Targets** (cycles/lights
the mission) → **accept** by shooting **up the Launch Ramp** → **complete** the
objective to bank points + progress lights. Missions consume **fuel** (lights
under the ramp); refuel via the Launch Lanes / re-launch.

Every mission's **completion award and progress-light count are exact from
`control.cpp`** (Windows / non-`FullTiltMode`, so no jackpot is folded in):

| Mission | Completion points | Progress lights |
|---------|------------------:|:---------------:|
| Launch Training | 500,000 | 6 |
| Re-entry Training | 500,000 | 6 |
| Target Practice | 500,000 | 6 |
| Bug Hunt | 750,000 | 7 |
| Rescue | 750,000 | 7 |
| Alien Menace | 750,000 | 7 |
| Science | 750,000 | 9 |
| Stray Comet | 1,000,000 | 8 |
| Space Radiation | 1,000,000 | 8 |
| Black Hole Threat | 1,000,000 | 8 |
| Satellite Retrieval | 1,250,000 | 9 |
| Reconnaissance | 1,250,000 | 9 |
| Doomsday Machine | 1,250,000 | 9 |
| Secret Mission | 1,500,000 | 10 |
| Cosmic Plague | 1,750,000 | 11 |
| Time Warp | 2,000,000 | 12 |
| **Maelstrom** (finale) | **5,000,000** | **18** |

**Selecting** a mission also scores, from `mission_select_scores[17]`: **10,000**
for the first four, **20,000** for the next nine, **30,000** for the last four
(by `MissionRcArray` order). The **fuel** gauge is a **12-segment bargraph** fed by
the six fuel rollovers.

**Objectives — exact, mined from each mission controller in `control.cpp`** (the
`liteNN->MessageField` hit-counters, decremented per collision):

| Mission | Objective | Count |
|---------|-----------|:-----:|
| Launch Training | shoot the **Launch Ramp** (`ramp`) | ×3 |
| Re-entry Training | any **re-entry lane** | ×3 |
| Target Practice | hit the **attack bumpers** (`bump1–4`) | ×8 |
| Science | knock down the **Science drop targets** (`target1–9`) | 9 |
| Bug Hunt | hit **any targets** (`target1–22`) | 15 |
| Rescue | arm via the 3 Mission Targets, then the **Hyperspace Kickout** (`kickout2`) | ×1 |
| Alien Menace | arm the lit attack bumper, then hit the **attack bumpers** | ×8 |
| Secret Mission | thread the wormholes **yellow (`sink3`) → red (`sink1`) → green (`sink2`)** | 3 shots |
| Stray Comet | light the **3 Right Hazard targets** (`target19–21`), then the **Hyperspace Kickout** | 3 + 1 |
| Space Radiation | light the **3 Left Hazard targets** (`target16–18`), then **any wormhole** | 3 + 1 |
| Black Hole Threat | upgrade engines (Ramp + Launch lanes → `bump5`), then the **Black Hole Kickout** (`kickout3`) | state-gated |
| Satellite Retrieval | hit the **lit remote bumper** (`bump4`) | ×3 |
| Reconnaissance | **Out / Return / Bonus lanes** | ×15 |
| Doomsday Machine | **either outlane** (`roll4`/`roll8`) | ×3 |
| Cosmic Plague | **flag (spinner) turns** (`flag1`/`flag2`), then a rollover (`roll9`) to bank it | 75 + 1 |
| **Time Warp** | hit the **flipper rebounders** (`rebo1–4`) ×25, then the **fork** (below) | 25 + fork |
| **Maelstrom** (finale) | eight-stage checklist (below) | — |

**Corrections to the earlier fan-guide values:** Alien Menace is **×8** (not 12);
Satellite Retrieval is **×3** (not 9); and the genuine **rank ± fork is Time
Warp**, not Cosmic Plague — `TimeWarpPartTwoController` moves `middle_circle`
**forward on the Launch Ramp (+1 rank)** and **backward on the Hyperspace Kickout
(−1 rank)**, whereas Cosmic Plague just banks 75 flag-turns via a rollover with no
demote.

**Maelstrom's eight stages** (`MaelstromController` → `MaelstromPartTwo…Eight`):
(1) **3** drop targets → (2) **3** spot targets → (3) **5** lanes/rollovers →
(4) the **Fuel Chute** (`roll184`) → (5) the **Launch Ramp** → (6) a **lit flag**
(`flag1`/`flag2`) → (7) **any wormhole** → (8) the **Hyperspace Kickout**
(`kickout2`), which banks **5,000,000 + 18 progress lights**.

### 3.3 Table layout & parts

Three vertical zones: **Drain Area** (below the flippers), **Launch Area** (purple
platform, mid-left), **Re-entry Area** (top).

| Part | Count / location | Role |
|------|------------------|------|
| **Flippers** | 2 lower red flippers (**no upper flipper**) | Sole ball control. |
| **Plunger / Launch chute** | Bottom-right | Hold-release launch; hidden 6-arch **Skill Shot**. |
| **Attack bumpers ("weapons")** | 4 — 3 upper-centre + 1 remote (upper-left, by green wormhole) | Pop bumpers; Target Practice / Alien Menace. |
| **Engine / Launch bumpers** | 3, under the 3 Launch Lanes | "Engines"; upgraded in Black Hole Threat. |
| **Launch Ramp** | Purple, mid-left; overlaps Fuel Chute | Mission-accept + refuel path. |
| **Hyperspace Chute → Kicker/Kickout** | Top-right | Big value; arms Gravity Well; Jackpot ×2; the "lose a rank" fork. |
| **Mission Target bank** | 3 spot targets | Selects the current mission. |
| **Left / Right Hazard banks** | 3 spot targets each | Complete = opens that side's kicker gate. |
| **Fuel Target bank** | Around the remote bumper | Fully lit = refuelled. |
| **Booster drop targets** | Right drop bank | ×2 → Jackpot; ×3 → Bonus. |
| **Medal drop targets** | 3 | All 3 while purple medal light lit = **extra ball**. |
| **Science drop targets** | 9 | The Science mission bank. |
| **Space Warp spot target** | Left of Kickout | 750 pts + activates all 3 wormholes. |
| **Wormholes** | Yellow (mid-right), Red (top-right), Green (top-left) | Enter open one → kicked out, **5,000 + Replay**; Secret threads all three. |
| **Flags (spinners)** | Spinner gates | Change wormhole exit colour; advance the **score multiplier** (1× → 2× → 3× → 5× → **10× max**); Cosmic Plague = 75 turns. |
| **Outlane kickbacks** | Left & right, above outlanes | Outlane save (kickback). |
| **Re-entry lanes** | Top — Out / Return / Bonus | Re-entry training + Recon; Bonus lane collects + refuels. |
| **Gravity Well** | Centre of the light ring | Armed by **5 consecutive Hyperspace Chute (Kickout) shots**; ball at rest = 50,000, then random kickout. |
| **Drain / outlanes / inlanes** | Bottom | Ball loss; outlane drain still scores 20,000. |

### 3.4 Core mechanics

- **Launch.** Hold Space to draw the plunger, release to fire; partial pulls land
  Skill-Shot arches (7,500–75,000).
- **Multiball — NONE.** Retail Full Tilt! locked balls for multiball; Windows 3D
  Pinball replaced it with **Replay free balls + bonus**. Do not add multiball.
- **Replay (the signature save).** A red "Replay" light beside the left flipper;
  also lit by the yellow wormhole while its light is yellow. If lit at ball loss →
  a new ball that **does NOT reset missions/progress** — strictly better than a
  plain extra ball. This is our marquee save mechanic.
- **Extra ball.** Level-3 Commendation; or all 3 Medal targets while the purple
  medal light is lit (repeatable while lit); or an outlane with its extra-ball
  light on.
- **Bonus / Jackpot.** Both are **accumulators**: `BonusScore` starts at **10,000**
  (Booster ×3 lights it; collected via the Bonus lane or on drain if lit; caps at
  5,000,000); `JackpotScore` starts at **20,000** (Booster ×2 arms it; collected by
  shooting the Hyperspace Chute twice). The score **multiplier** ladder is
  `{1, 2, 3, 5, 10}` and scales every award (`AddScore` multiplies by it).
- **Tilt / nudge.** Nudge to steer; over-nudge → "Careful…" then **TILT** (red X
  near the mission lights) which **freezes all table elements** until the ball
  drains. No ball credited back.
- **Ball save.** Outlane kicker/kickback = the physical save; Replay = the
  light-based save. No modern timed grace-period save.

### 3.5 Scoring & progression

- **3 balls** per game; ends after the 3rd drains (plus Replays/extra balls). A
  High Score table records top games.
- **Progression currency = progress lights**, not score. Missions pay both points
  (leaderboard) and progress lights (rank). **Fleet Admiral** (after the
  Admiral-tier missions incl. **Maelstrom**) is the summit.
- **Notable fixed values (code-verified, `control.cpp`):** Skill-Shot arches
  `{15k, 30k, 75k, 30k, 15k, 7.5k}`; Launch Ramp 5,000; attack bumpers
  `{500, 1k, 1.5k, 2k}`, engine bumpers `{1.5k, 2.5k, 3.5k, 4.5k}`; Hyperspace
  Kickout levels `{10k, 0, 20k, 50k, 150k}`; wormhole sink levels
  `{2.5k, 5k, 7.5k}`; Gravity Well kickout 50,000; outlane rollover 20,000; return
  lane `{5k, 25k}`, bonus lane 10,000; flag/spinner `{500, 2,500}`; Space Warp
  target 750; Bonus init 10,000 (cap 5,000,000); Jackpot init 20,000; multiplier
  `{1, 2, 3, 5, 10}`.
- **"Round the clock"** = exceeding 1,000,000,000 points (display rolls over).
- **Risk/reward:** the **Time Warp** fork (Launch Ramp = warp **forward**, +1 rank;
  Hyperspace Kickout = warp **backward**, −1 rank) is the one place the game can
  demote you — a memorable beat to keep. (Cosmic Plague, often credited with this,
  actually just banks 75 flag-turns with no demote.)

### 3.6 Controls

| Action | Original Windows key | Remake — keyboard | Remake — gamepad |
|--------|---------------------|-------------------|------------------|
| Left flipper | **Z** | Z / Left Shift | Left bumper/trigger |
| Right flipper | **/** (slash) | / / Right Shift | Right bumper/trigger |
| Plunger | **Space** (hold-release) | Space (hold-release) | Right stick pull-back / A |
| Nudge right | **X** | X / D | Left-stick flick right |
| Nudge left | **.** (period) | . / A | Left-stick flick left |
| Nudge up | **Up arrow** | Up / W | Left-stick flick up |
| New game | **F2** | F2 / Enter | Start |

Keep the **charge-and-release plunger** and the **hold-nudge → tilt** relationship
— both are core to the original feel. Rebindable, as in the original
(Options ▸ Player Controls).

### 3.7 Art & feel

Dark outer-space playfield, glowing neon plastic inserts, a chrome ball,
metallic/anodized ramps and rails, three coloured glowing wormholes, a glass
cover. Original was pre-rendered 3D (TrueSpace), 640×480. Physics — fast, twitchy,
hand-tuned — by Mike Sandige; music/SFX by Matt Ridgeway (sci-fi synth stings,
laser/zap effects, spoken callouts for mission select/complete, rank promotion,
tilt, replay). The "feel" is a **fast ball**, punchy bumpers, and a real tilt threat — qualitative
targets a realistic inclined-steel sim naturally delivers (punchy bumpers via the
active impulse zones of §6.1.6, the tilt threat via the §6.1.6 tilt bob), while
§6.1.9 governs that the *exact* 1995 hand-tuned feel is explicitly not a target.

---

## 4. Hybrid rendering architecture

### 4.0 The load-bearing fact: Phase 6 is one dispatch, not a G-buffer compositor

`Phase6Renderer.RenderFrame` records exactly **one** trace of
`PathTracePhase6.hlsl:CSMain` (`RecordTrace` issues a single `Dispatch`), then a
resolve, an optional pixelate, and a reduce (`RecordTraceResolveReduce`). The
trace is **inline DXR 1.1** (`RayQuery<>.TraceRayInline` against one `Scene`
TLAS) running as a compute shader. **There is no rasterizer anywhere in the
repo** — no depth/stencil target, no deferred G-buffer. The per-pixel
`HitPointOut`/`NormalOut` outputs exist only to feed TAA reprojection.

That kills the textbook hybrid ("rasterize movers into a G-buffer, composite over
the ray-traced static image by depth") — it would mean bolting a whole raster
pipeline onto a pure compute+DXR engine. The engine gives us something strictly
better.

**The recommended hybrid is one unified DXR trace over a multi-instance TLAS,
where "static playfield" vs "moving part" is a per-primitive *shading budget*
decided inside `ShadeSample`, not a separate render pass.** Occlusion, the ball's
reflection, and the ball's contact shadow all fall out of the single TLAS with no
compositing step — and cross-reflections resolve in the same trace, not by depth compositing. (Two
caveats the sections below must handle, *not* automatic: the **moving** ball
reflected in static chrome/glass ghosts unless soft-capped (§4.3), and self-glowing
inserts need an emissive term that is unbuilt (§5.3).)

### 4.1 The two tiers (both traced in the same dispatch)

Separated by a new per-primitive `Dynamic` classification (a bit on
`GpuPrimitive` / `GpuSphere`, orthogonal to `SurfaceKind`).

**(a) STATIC PLAYFIELD tier — full spectral, converged.** Everything the camera
sees that does not move each frame: table surface, walls, glass dome, ramps,
inserts, bumper bodies, lane guides. Shaded by the **existing, unmodified** Phase
6 path — spectral NEE + one-bounce indirect (`ShadeSample`), specular chains up to
`MAX_SPECULAR_BOUNCES = 8`, GPU photon-map caustic gather, volumetric fog, lens/
DoF. Because the camera is **fixed or slow-panning**, `AccumulationBuffer` +
`TaaResolver` converge these pixels to high effective spp — a near-offline
spectral render at real-time cost. This tier is "cached" implicitly in
`Accum`/`DirectAccum`/`IndirectAccum` + the TAA history ping-pong; it converges
*between* lighting events. A camera move or table rebuild hard-resets it
(`RebuildScene`/`SetLens`/`Resize` already `_forceReset`) — but an insert/light
change **must NOT** route through `_forceReset` (that throws away the expensive
indirect convergence); it needs the in-place, short-window reconverge of
§4.2/§4.4/§5.2 (new work). "Static" here means *geometry*-static: during play the
inserts animate (§3.1/§3.2), so the lighting is not frozen.

**(b) DYNAMIC tier — cheap, single-bounce, hero/RGB, NOT spectral-path-traced.**
Ball, flippers, plunger, gates, spinners. A new short branch in `ShadeSample`
gated on `Dynamic`: no multi-bounce spectral integration (cap specular depth to 1
via a new `MoverSpecularBounces = 1`, skip the caustic gather, skip the sky-dome
indirect bounce); hero-wavelength or RGB **direct** lighting only (one NEE shadow
ray + ambient fill); soft-capped accumulation + TAA exclusion (§4.3). This is a
direct generalization of the **already-shipped `isMover` path** (`CSMain`,
`PathTracePhase6.hlsl` ~lines 2236–2241), which today tags the maze rat's pixels
`isMover=true` and caps them to `MotionSampleCap`. We extend `isMover` from "the
rat billboard" to "any `Dynamic` primitive" and add the cheap shading branch.

**Recommendation.** Hybrid-within-one-dispatch over any raster/compositor. Upside:
exact occlusion, correct cross-reflections, zero new pipeline, reuses
`UpdateSpheres`/`isMover`/`TraceSpecularRadiance`/`TraceTransmittance` verbatim,
and honours "movers not spectral" precisely. Downside: movers still cost primary
rays in the one dispatch (negligible — a few thousand pixels with 1 reflection ray
each) and force a per-frame acceleration-structure refit (the real cost — §4.6,
§10).

### 4.2 Geometry: how movers enter the TLAS

The TLAS is already multi-instance. `BuildAccelerationStructures` builds a
triangle BLAS (instance 0) and, when spheres exist, a procedural-AABB sphere BLAS
(instance 1); `UpdateSpheres` refits the sphere BLAS + TLAS **in place** every
frame while leaving the static triangle BLAS untouched.

- **The ball** = one `GpuSphere` with `Surface = SURFACE_MIRROR`, `Dynamic = 1`,
  on the exact `UpdateSpheres` path that today drives the drifting maze bubbles.
  Gameplay hands the renderer the ball centre each frame; `UpdateSpheres` rewrites
  the AABB and refits. **This path already exists and is validated.**
- **Flippers / plunger / gates** = rigid triangle meshes. Add a **third instance —
  a "dynamic triangle BLAS"** built once, then moved each frame by writing its
  **TLAS instance transform** (`RaytracingInstanceDescription.Transform`, today
  hardcoded to identity) and rebuilding **only the TLAS**. This is the correct DXR
  idiom for rigid motion (no geometry rebuild). The transform-update *idiom* exists
  (`TlasInputs`/`_instanceBuffer`), but it is more than one edit: the instance list
  and TLAS scratch/result buffers are sized for 1–2 instances and `UpdateSpheres`
  hardcodes `TlasInputs(2)` (`Phase6Renderer.cs:1384`), so a third instance also
  means growing the instance buffer, re-sizing TLAS scratch/result, and replacing
  the hardcoded counts. A new host API `SetDynamicPose(int instance, Matrix3x4)`
  sits alongside `UpdateSpheres` (built in P4).
- **Bumpers** barely move; their "pop" is an **emissive flash**. Caveat: emissive
  *surfaces* are not implemented in the tracer today (§5.3) — the pop is driven by a
  new in-place `UpdateEmissive(materialId, level)` (mirroring `UpdateSunLight`: no
  buffer recreation, no `_forceReset`) plus a short local reconverge (§4.4). No
  geometry/AS work, but *not* free — frequent pops keep a short-window term on the
  budget (§4.6).

### 4.3 Temporal machinery: KEEP for static, BYPASS for movers

| Machinery | Static playfield | Movers |
|---|---|---|
| `AccumulationBuffer` running mean | **KEEP** (converges, fixed camera) | **BYPASS** — soft-cap to `MotionSampleCap` (short sliding window) |
| TAA reprojection (`TaaResolver`/`ResolvePhase6`) | **KEEP** — identity reprojection under a still camera → perfect history reuse | **BYPASS** — write `TaaNextValid=false` (a mover stencil) so no stale history blends on |
| Neighborhood min/max clamp | KEEP | Helps, insufficient alone for fast movers |
| `SoftResetAccumulation` (whole-frame cap) | Only when the camera actually pans | Not needed — the per-object `isMover` cap handles it |

**The single biggest correctness fix.** The per-pixel restart is
`restart = reset || (LastHit[ix] != hitMask)` and `hitMask` is a **1-bit
hit/miss** flag. Ball and ramp both report `hitMask=1`, so when a fast ball slides
off a pixel the ramp behind it blends into the ball's stale mean — a **ghost
trail** that only decays over `MotionSampleCap` frames. Fix: store a per-pixel
**dynamic-vs-static id** (or a small instance id) in place of / alongside the
1-bit `LastHit`, and restart when the id changes; a ball→ramp transition then
forces a clean 1-sample restart and the trail vanishes. Lands CPU-first
(`PathTracer.cs` / `Phase2Reference.cs`) then HLSL, and **must keep the no-mover
goldens bit-identical** (a scene with no `Dynamic` primitives writes the same
`hitMask` it does today).

**The inverse — the ball reflected/refracted in *static* specular surfaces.** The
money-shot chrome rails (static `Mirror`, §4.5) and glass dome (static `Dielectric`)
reflect the moving ball, but their *primary* hit is the static surface, so the
hit-id never flips: the pixel never restarts or soft-caps, and the reflected ball
bakes into an unbounded mean as a frozen ghost — worse than the primary trail. Fix:
OR-propagate a `pathTouchedDynamic` flag out of `TraceSpecularRadiance` /
`TraceTransmittance` / `ShadeSample`, and apply the same soft-cap + TAA stencil to
any static-primary pixel whose path touched a `Dynamic` primitive. This is
materially more than promoting the 1-bit `LastHit` to an id (see re-scoped P2).

### 4.4 The ball specifically (the chrome money shots)

Both are **already shipped**, needing only a cost cap:

1. **Mirror reflection of the converged environment.** A primary ray hitting a
   `SURFACE_MIRROR` sphere runs `TraceSpecularRadiance(camPos, primaryDir, hero,
   rng)` (`ShadeSample`, ~line 1811), which `reflect()`s into the static TLAS and
   terminates on a diffuse surface via
   `MirrorDiffuseScalar = refl·(ambient+direct[+indirect])`. For the ball: cap the
   chain to **1 bounce** (`MoverSpecularBounces`), skip the caustic gather and
   sky-dome indirect. Hero-only means a touch of spectral noise the
   `MotionSampleCap` window averages — and a chrome ball wants a neutral mirror,
   not dispersion, so hero-only is *ideal* and cheap.
2. **Contact/hard shadow onto the table.** Already free: an opaque sphere commits
   as a shadow occluder in `TraceTransmittance`'s candidate loop
   (`q.CommitProceduralPrimitiveHit(t)`, ~line 783), so the playfield's NEE shadow
   ray passing the ball drops a hard contact shadow with no new code — the ball
   just has to be `Dynamic` and opaque (Mirror qualifies).

**Ball caustic: no by default.** Caustics come from `BuildCausticsGpu`/
`RunCausticBuild`, which does two full `WaitForGpu` stalls and a whole-scene
forward photon trace — a **once-at-table-load** cost, not per-frame. A chrome ball
is reflective, not refractive, so it would throw only a faint glint. Keep the
photon pass for the static dome/gem/ramps; the ball casts none except as an
optional High-tier localized effect.

**The moving contact shadow perturbs the converged table — handle it
explicitly.** The shadow sweeping across converged table pixels is a
*direct-lighting* change baked into their converged mean, and those pixels do not
restart (no hit-mask flip), so the shadow lags. Because the engine keeps
`DirectAccum`/`IndirectAccum` as separate running means, the buildable fix is to
**soft-cap the direct-lighting accumulation** (short window) in a cheap
"dynamic-influence" region under the ball while the indirect/spectral means keep
their long convergence — the shadow tracks the ball, the spectral look stays
converged. Simplest first cut: run direct lighting on a short *global* window (harmless, since
a fixed-camera static direct term reconverges instantly), and reserve the
region-scoped version for polish. **Generalise beyond the ball's shadow:** any
region whose incident lighting changed this frame — a bumper pop, an insert/flasher
toggling (§4.2) — needs the same short window; and because an insert's *glow* on its
neighbours travels through the one-bounce **indirect**/NEE term as well as the
direct term, the window must cover whichever term carries the change, not the
direct term alone.

### 4.5 Spectral showpieces that survive on STATIC parts (near-zero runtime cost)

Most effects the table wants are a **shipped `SurfaceKind`** on static geometry that
converges under the fixed camera. The **emissive** inserts are the exception: that
SurfaceKind is an enum with *no tracer implementation* (§5.3), so it is new work,
not reuse:

| Table element | Tier | Technique | Spectral? |
|---|---|---|---|
| Table surface / walls / lane guides | Static | Full Phase 6 spectral NEE + 1-bounce indirect, converged | Yes |
| Glass dome | Static | `Dielectric`: Fresnel + refraction + dispersion + Beer–Lambert; casts static caustic | Yes |
| Plastic ramps | Static | `Dielectric` / glossy + Beer–Lambert tint | Yes |
| Anodized metal ramps & rails | Static | `Mirror` (roughened) specular chain | Yes (hero) |
| Neon inserts, rank/progress lights, wormhole glow | Static | **`Emissive` — NEW (unbuilt, §5.3):** emissive-surface term + co-located NEE `PackedLight` | Yes (once built) |
| Dispersion gem / "fire" insert | Static | `Dielectric` + dispersion + prism/gem caustic | Yes |
| Oil-slick bumper cap | Static | `ThinFilm` iridescence | Yes |
| Volumetric table haze | Static (screen) | Fog ray-march composited in resolve | Yes |
| DoF / vignette / bloom\* | Static (camera/post) | `LensOptions`; bloom = new post-pass\* | Yes |
| **Chrome ball** | **Dynamic** | Analytic `Mirror` sphere; **1** reflection ray into static TLAS; hard contact shadow | **No (hero/RGB)** |
| **Flippers** | **Dynamic** | Rigid TLAS instance-transform; single-bounce diffuse/glossy NEE, direct-only | **No (RGB)** |
| **Plunger** | **Dynamic** | Rigid instance-transform; diffuse NEE, direct-only | **No (RGB)** |
| **Gates / spinners** | **Dynamic** | Rigid transform; diffuse direct-only | **No** |
| Bumper body "pop" | Static geom + animated emission | In-place `UpdateEmissive` (NEW, §5.2/§5.3) + short reconverge | Spectral flash, short-window cost |
| Ball caustic | Off / opt High | none by default (reflective, not refractive) | No |

\*Bloom / veiling glare around the neon and lane lights is **the one unbuilt
showpiece** — a new post-pass that slots in exactly where `PixelatePhase6` already
sits (after the resolve, in-place on `_outputTexture`).

**Note — the ball in static specular.** Because the anodized rails are static
`Mirror` and the dome static `Dielectric`, the *moving* ball appears in them and
must ride the `pathTouchedDynamic` soft-cap/stencil (§4.3) or it ghosts on exactly
the surfaces sold as the payoff; roughened rails blur the ghost but do not remove
it.

### 4.6 Performance budget & presets (RTX 3070)

Budget **16.6 ms (60 fps)** for Classic, **33 ms (30 fps)** acceptable for RT
Showcase. Where the frame goes:

- **Base trace (1 spp)** — the engine dispatches a **full-screen** 1-spp trace every
  frame regardless of convergence (no converged-pixel early-out — §5.4), same order
  as the maze's per-frame trace (~640–800 px). Classic targets 1280×720 NEE (~2× the
  pixels), so **measure the full-screen 1-spp cost at the real Classic
  resolution/quality on the 3070 before committing to 60 fps.** The fixed camera
  buys effective spp via accumulation, not a per-frame ray cut.
- **Movers** — a few thousand primary-hit pixels × 1 reflection ray. Negligible.
- **Per-frame AS refit — the real cost.** `UpdateSpheres` and any TLAS rebuild
  currently sit behind their own `WaitForGpu` (full GPU drains). At 60 fps this
  must be fixed: **fold the mover AS refit into the trace command list (one
  submission, one fence wait)** and use **TLAS instance-transform updates** for
  rigid flippers/plunger (cheap) rather than AABB rewrites. The merged single
  submission **must retain the existing `ResourceBarrierUnorderedAccessView(_tlas)`**
  (already emitted by `UpdateSpheres`) before the trace so the build→read hazard
  stays covered; only the redundant intervening `WaitForGpu` is dropped. Sign off on
  a **measured** 3070 build+trace cost, not the rebuild time alone.
- **Caustics / photon map — off the frame budget** (built once at table load,
  rebuilt only on a lighting change).

**Presets** (extend `RenderPreset`, which already carries `Lighting`,
`Volumetrics`, `Caustics`, `Realism`, `SampleClamp`, `MotionSampleCap`):

- **Classic / fast** (≈ `Playable`/`Medium`): `LightingMode.Direct` or `NEE`,
  caustics off, fog low/off, ball = 1 reflection bounce, no DoF, optional retro
  `PixelSize` for a CRT feel. 60 fps at ~1280×720 internal.
- **RT Showcase** (≈ `High`/`Ultra`): `NEE` + one-bounce indirect + static
  caustics + volumetric haze + `LensModel.Cine` DoF + Filmic tone map, ball = 2
  reflection bounces. 30–60 fps at ~1600×900 internal, presented upscaled via
  `ResolutionScale` + host `Zoom` (the engine's only upscaler — there is no
  DLSS/FSR; the fixed-camera convergence is what buys the quality).

**Interactive vs spectator.** Fast-ball *play* targets **Classic at 60 fps**; RT
Showcase's 30 fps floor is for spectator / instant-replay / photo use — movers carry
no motion blur (§4.3 deliberately kills accumulation smear), so a 5–10 m/s ball at
30 fps (~170–330 mm/frame) judders. If Showcase is to be played, hold ≥60 fps.

**Upscale posture:** render internal below display and let the host
`PictureBox.Zoom` upscale, exactly as the maze does. The fixed camera lets the
static half reach high effective spp, so a higher internal resolution is
affordable than a fully-dynamic renderer would allow — the movers are the only
pixels genuinely recomputed from scratch each frame.

---

## 5. Engine reuse & gaps (honest RTX 3070 assessment)

The renderer covers most of what this game needs; the gap is largely
**gameplay-side** (physics, game loop, input, state), plus real renderer work to
admit movers cleanly (§5.2) and **one sizeable new feature — emissive-surface
shading (§5.3)** — that the neon/insert look and the ball's reflection of it depend
on.

### 5.1 Reuse as-is (no change)

| Capability | Where | Note |
|---|---|---|
| Inline DXR 1.1 path tracer, single dispatch | `Phase6Renderer`, `PathTracePhase6.hlsl` | The shipping render path. |
| Spectral shading (mirror/glass/dispersion/thin-film/absorption) | `Optics/`, `MaterialsLookup`, references | All built **except `Emissive`** — that enum has no tracer term (→ §5.3). |
| NEE + one-bounce indirect GI | `ShadeSample` | Static playfield lighting. |
| Accumulation + TAA convergence | `AccumulationBuffer`, `TaaResolver`, `ResolvePhase6.hlsl` | Built for static-scene convergence — exactly the pinball case. |
| Analytic sphere as procedural-AABB mover | `GpuSphere`, `UpdateSpheres` | The chrome ball, verbatim. |
| `isMover` bounded temporal window | `CSMain` (~2236–2241) | The mover cheap-tier seed. |
| Mirror specular reflection | `TraceSpecularRadiance` | Money shot #1. |
| Opaque-occluder contact shadow | `TraceTransmittance` (~783) | Money shot #2. |
| GPU spectral photon-map caustics | `BuildCausticsGpu`/`RunCausticBuild` | Static dome/gem/ramps only, baked at load. |
| Volumetric fog | `VolumetricIntegration`, `VolumetricOptions` | Static table haze. |
| Camera lens / DoF / vignette | `LensOptions`, `LensSampler` | RT-Showcase glamour. |
| Presets & quality gating | `RenderPreset` (+`Realism`/`Caustics`/`Volumetrics`) | Extend, don't replace. |
| Debug views + stats readback | `DebugBufferRenderer`, reduce pass | Dev/tuning. |
| Resize / device-removed recovery | `Phase6Renderer` | Productization already done. |
| WinForms host, ConfigForm, `.scr` | `Program.cs`, `ConfigForm.cs`, `Screensaver.cs` | Host shell to adapt (§7). |

### 5.2 Adapt (small, gated renderer changes — CPU → reference → HLSL)

1. **`Dynamic` bit** on `GpuPrimitive`/`GpuSphere` + the cheap mover shading branch
   in `ShadeSample` (cap to `MoverSpecularBounces=1`, direct-only, hero/RGB).
2. **Hit-id restart + `pathTouchedDynamic`** (§4.3) — restart / soft-cap / TAA-stencil
   any pixel whose *primary or any bounce* touched a `Dynamic` primitive (kills the
   ball trail *and* the ball's ghost in static chrome/glass). Threading the flag
   through the specular / transmittance chains is materially more than a 1-bit→id
   promotion.
3. **Mover TAA stencil** (`TaaNextValid=false` for movers) in
   `ResolvePhase6.hlsl` / `TaaResolver`.
4. **Rigid TLAS instance-transform API** (`SetDynamicPose`) + fold the mover AS
   refit into the trace command list (kill the extra `WaitForGpu`).
5. **Moving-shadow direct-window** refinement (§4.4).
6. **Bloom post-pass** (the one unbuilt showpiece), slotted where `PixelatePhase6`
   sits.
7. **Preset additions:** Classic / RT-Showcase pinball presets.
8. **In-place emissive/material update** — `UpdateEmissive(materialId, level/spectrum)`
   that re-maps only the affected material-buffer rows (mirroring `UpdateSunLight`;
   no buffer recreation, no `BuildAccelerationStructures`, no `_forceReset`), so an
   insert / bumper flash never routes through `RebuildScene`. Pairs with the
   short-window reconverge of §4.4.

Every one of these must keep `--phase6-selftest` and `--phase6-regress` green with
no `Dynamic` primitives in the scene, and must respect the **`TraceConstants`
cbuffer overflow trap** (a fixed 512-byte buffer already ~320 B full; new mover
flags pack into existing pad fields or a second cbuffer — overflow silently drops
to 0 on the GPU with no error).

### 5.3 Genuinely new (all gameplay-side)

- **Rigid-body physics** for the ball (§6) — nothing in `Geometry/` or `Pipeline/`
  drives per-frame rigid transforms today.
- **Game loop / fixed-timestep simulation** decoupled from the converge rate.
- **Input handling** for flippers/plunger/nudge/tilt (the WinForms host only reads
  camera-walk keys today).
- **Game state & scoring** — ranks, missions, progress lights, replay, tilt, high
  scores.
- **Emissive-surface shading** — `SurfaceKind.Emissive` exists only as an enum; the
  tracer has no emission term (the emissive AOV is literally zero,
  `DebugBufferRenderer.cs:25`). Neon inserts / wormholes / bumper-pops need a real
  emissive term — an emission contribution in `ShadeSample`'s diffuse terminal, in
  the indirect bounce, and as an NEE emitter — plus a `SURFACE_EMISSIVE` branch in
  the reference and HLSL, landed CPU→reference→HLSL. Cheap NEE of the *cast* light
  can additionally use a co-located analytic `PackedLight` per insert. **This is a
  prerequisite for P0 to look right** and is the largest single renderer add.
- **Table geometry authoring** — the Space Cadet playfield authored **clean-room**
  from one source-of-truth table description that emits BOTH the render triangle
  meshes AND the analytic/mesh **colliders** (§6.1.2) co-registered, so the two
  representations cannot drift (they must agree to sub-ball-radius ≈ 13.5 mm or the
  ball clips / floats / tunnels), plus the dynamic instances (ball sphere,
  flipper/plunger meshes) and a collider-vs-render debug overlay. The main content
  cost (§8, §10).
- **Audio** (note only, §6.5) — the engine has none.

### 5.4 Honest 3070 real-time assessment

**Feasible, with the fixed camera doing the heavy lifting.** The 3070 already runs
the maze's Phase-6 trace real-time at ~640–800 px internal *while the camera
moves and the whole frame is recomputing*. Pinball is easier on the GPU in the
steady state: the camera is fixed. The GPU still dispatches a **full-screen** 1-spp
trace every frame (there is no converged-pixel skip — `Phase6Renderer.cs`), so
per-frame cost ≈ the maze's full trace; what the fixed camera buys is that each
frame only *adds* one cheap sample to the running mean of the expensive spectral
shading (amortised convergence) and keeps valid TAA history — image quality, not a
ray cut. The converged look holds between lighting events; frequent insert
animation keeps a short-window term (§4.4) on the budget.
The genuine risks are not ray throughput but (a) the per-frame AS refit stalls
(fixable by folding into the command list — §4.6, §10), (b) the ghost-trail
correctness bug (§4.3), and (c) the moving-shadow lag (§4.4). None are
throughput-bound; all are architectural fixes we can land CPU-first. Expect
**Classic at a solid 60 fps** and **RT Showcase at 30–60 fps** at the internal
resolutions in §4.6.

---

## 6. Systems, physics & simulation

The renderer is ~95% ready (§5); the gameplay stack is almost entirely new. Two
pieces dominate: a **physically-based 3-D simulation** of the ball — the second
fidelity pillar (§6.1) — and the **game loop** that feeds it into the converging
renderer (§6.2). Input (§6.3), game state (§6.4) and audio (§6.5) sit on top.

### 6.1 Physics — realistic 3-D rigid-body simulation (the second fidelity pillar)

*Held to the same standard as the spectral light transport. Full model, SI
constants, impulse equations, spin/Magnus/drag, CCD, actuators, build-vs-buy and
the closed-form validation suite follow; its internal cross-references use the
§6.1.N numbering.*

#### 6.1.0 Governing principle — "trajectory comes from physics"

This engine already refuses to fake light: colour comes from spectral physics, never a hard-coded tint. The physics model adopts the exact analogue as a non-negotiable working convention:

> **Trajectory comes from physics — gravity, inertia, friction, restitution, spin, drag — and never from a hard-coded feel-tweak.**

Every coefficient in the model is a *measured material property in SI units* (steel-on-clearcoat restitution, steel-on-rubber friction, real ball mass and inertia, real playfield incline), sourced from literature or measurement, not a knob dialled to reproduce the 1995 game's arcade feel. Where the simulation ends up feeling different from the original *Space Cadet*, that divergence is the accepted, honest consequence of simulating for real. The model is validated the same way the renderer is: **deterministic, fixed-seed unit tests that assert against closed-form analytic results** (§6.1.8). Realism is a first-class pillar on par with the spectral light transport, and it wins over "feel" wherever the two conflict.

This is feasible precisely because pinball is the easy case of rigid-body dynamics: **one dynamic body** (the ball) against a large **static** world and a **handful of kinematic** actuators. That shape lets us run an exact analytic single-sphere solver in double precision rather than a general iterative constraint solver, and it is what makes full 3D fidelity — airborne balls, ramps, spin — cheap enough to over-solve.

#### 6.1.1 Rigid-body model (6-DOF ball)

New namespace `RayTracer.Core/Physics/*`, pure C#, deterministic, `double`-precision internally (see §6.1.5 on why not `float`). The dynamic ball carries full 6-DOF state:

```csharp
// RayTracer.Core/Physics/RigidBody.cs
public struct BallState
{
    public Vector3D Position;        // m,   world space (playfield frame)
    public QuaternionD Orientation;  // unit quaternion, ball body frame
    public Vector3D LinearVelocity;  // m/s
    public Vector3D AngularVelocity; // rad/s (world axes), ω
}
```

`Vector3D`/`QuaternionD` are `double` value types (the render side's `System.Numerics.Vector3` is single-precision; physics needs the extra mantissa for stable contact and reproducibility). Orientation is a **quaternion** integrated as `q̇ = ½ ω_quat ⊗ q` and renormalised each step to kill drift. Orientation is not dynamically important for a uniform sphere (its inertia tensor is isotropic) but is tracked so the renderer can spin the ball's surface texture/reflection and so we can later attach non-uniform features.

**Real constants** (SI), stored as data in `Physics/PhysicsConstants.cs`:

| Quantity | Symbol | Value | Source |
|---|---|---|---|
| Diameter | d | 27.0 mm (1-1/16″ = 26.99 mm) | ball spec |
| Radius | r | 0.0135 m | — |
| Mass | m | 0.0806 kg (80.6 g, carbon steel) | ball spec |
| Solid-sphere inertia | I = ⅖ m r² | 5.876 × 10⁻⁶ kg·m² | — |
| Gravity | g | 9.80665 m/s² | standard |
| Playfield incline | α | 6.5° (range 6.0–7.0°) | Pinside/VPX |
| Air density (20 °C) | ρ | 1.204 kg/m³ | standard |
| Cross-section | A = π r² | 5.726 × 10⁻⁴ m² | — |

The inertia tensor is the scalar `I` times identity, so `I⁻¹` is just `1/I` — no tensor rotation needed, a real simplification a sphere buys us.

**Gravity on the inclined playfield (the reason this is genuinely 3D).** Model the world with `+Y` up (true vertical) and gravity `g_world = (0, −g, 0)`. The playfield is a plane tilted by `α` about the table's `X` axis, with unit normal `n_pf`. Do **not** pre-project gravity into a 2D slope acceleration — apply full 3D `g_world` to the body and let contact with the tilted playfield plane produce the normal reaction. Decomposed at a resting/rolling contact:

- Normal load: `N = m g cos α` → 9.744 m/s² × m = 0.786 N (holds the ball on the wood).
- Down-slope drive: `g sin α` = 1.110 m/s² (the constant pull toward the flippers).

Because gravity is applied in true 3D and the playfield is one collider among many, the ball **naturally climbs ramps** (a ramp is just a steeper plane/mesh; the ball trades kinetic energy for height, `½mv² → mgh`, and rolls back if it runs out), **goes airborne** off a ramp lip or a hard slingshot (it simply loses contact — `N` would have to be negative to hold it, so the solver releases it and it follows a ballistic + drag + Magnus arc until the next collision), and lands and resumes rolling. None of that is special-cased; it falls out of solving `F = ma` in 3D. A 2D-on-a-slope model cannot express any of it.

#### 6.1.2 Contact & collision

**Collision primitives a table needs**, all implemented as double-precision analytic colliders. Each is a thin geometric object exposing a signed-distance / closest-point query against the ball's sphere:

| Collider | Models | Narrowphase test |
|---|---|---|
| `PlaneCollider` | playfield, glass ceiling, flat walls, apron | point–plane distance vs `r` |
| `HalfSpace`/`QuadCollider` | bounded wall panels, slingshot faces | closest point on convex quad |
| `CylinderCollider` | posts, bumpers, ball guides (finite/infinite height) | distance to axis vs `r` + cap tests |
| `SphereCollider` | rollover bumper caps, captured balls, jewels | centre distance vs `r₁+r` |
| `CapsuleCollider` | rubbers, rails, wireforms (swept segment) | distance point→segment vs `R_rubber+r` |
| `ArcCollider` (swept-arc) | curved lane guides, horseshoes | distance to circular arc in a plane vs `r` |
| `MeshCollider` (triangles) | ramps, sculpted plastics, habitrails | closest point on triangle soup vs `r`, via BVH leaves |

A pinball world is almost entirely these; a "sphere vs X" closest-feature query is closed-form for every one of them, which is why an analytic solver (§6.1.7) is viable.

**Broadphase reuses the existing `BVH`** (`RayTracer.Core/Geometry/BVH.cs`). The BVH is built once from a static `Tracable[]` and offers `FindClosest(Ray)` plus AABB culling via `AABB.Intersects(origin, invDir, tMax)` — exactly a static-world accelerator. The plan:

- Make each static collider implement the existing `Tracable` interface (`Bounds`, `Intersect(Ray)`). They drop into the current `BVH` unchanged; the shading fields of `HitInfo` are simply unused for physics. Post/rail/ramp colliders return their geometric `AABB` and a ray hit — the BVH's midpoint-split build and iterative traversal (that flat `FlatNode[]`, `MaxStackDepth = 64`) work as-is.
- **Swept-sphere broadphase query:** to move the ball from `p` to `p+Δ`, query the tree with the AABB of the swept sphere (start ∪ end, inflated by `r`). This is a small new `BVH.OverlapSweep(aabbOfSweptSphere)` returning candidate leaves via the same `AABB.Intersects` slab test — a geometry-only traversal (no `HitInfo` allocation). (A centre ray through `BVH.FindClosest` is **not** usable: it returns a single closest hit, and a centre ray running parallel to a wall misses a genuine inflated-sphere contact — exactly the wall-hugging fast ball CCD exists to catch.) The query/collider AABBs are inflated by a small ε before the float slab test so the single-precision BVH stays a strictly conservative, never-miss culler (§6.1.9).
- **Precision caveat, stated honestly:** the BVH is single-precision `float`. That is fine for *broadphase candidate culling* (a conservative over-set of colliders is always safe), but every **narrowphase** distance/impulse computation is redone in `double` against the exact analytic collider. Broadphase in `float`, narrowphase and integration in `double`.
- **Kinematic actuators are not in the static BVH.** Flippers, the plunger tip, and any moving gate are a handful of bodies; test the ball against them directly each substep (O(1)). This matches pinball's "1 dynamic + static world + few kinematic" structure perfectly and sidesteps rebuilding the tree per frame.

**Contact manifold (single sphere).** Sphere-vs-primitive contact is a **single point** (spheres don't form edge/face manifolds), which removes the hardest part of general contact solving. Generation: closest point `c` on the primitive to the ball centre `P`; penetration depth `δ = r − |P − c|`; contact normal `n = (P − c)/|P − c|` (pointing from surface into ball); contact point `x = P − r n`. If `δ > 0` there is a contact. In tight lanes the ball may touch several primitives in one step — collect **all** contacts with `δ > −ε` (a small speculative margin) into a small list and solve them together (§6.1.5), which is what stops a fast ball in a narrow channel from jittering between two walls.

**Restitution + Coulomb friction impulse (the core solver).** Let `r_c = x − P = −r n` be the arm from centre to contact. Relative velocity of the ball's material point at the contact against a static/kinematic surface (surface velocity `u`, e.g. a moving flipper):

```
v_c   = (v + ω × r_c) − u          // contact-point velocity, includes spin
v_n   = v_c · n                     // normal component (v_n < 0 ⇒ approaching)
v_t   = v_c − v_n n                 // tangential slip velocity
```

*Normal impulse* with restitution `e`. For a sphere the normal passes through the centre, so `r_c × n = 0` — the normal impulse is **torque-free** and its effective mass is just `1/m`:

```
j_n = −(1 + e) · v_n / (1/m)  =  −(1 + e) m v_n        (clamped ≥ 0)
J_n = j_n n
```

A consequence worth stating: **normal bounces never create spin** on a sphere. All spin comes from the tangential (friction) impulse. That is physically correct and analytically clean.

*Friction impulse* (Coulomb, static vs kinetic). The tangential effective mass for a solid sphere works out to a constant:

```
K_t = 1/m + r²/I = 1/m + r²/(⅖ m r²) = 7/(2m)   ⇒  tangential reduced mass = 2m/7
```

The impulse that would exactly arrest slip is `j_t* = |v_t| / K_t = (2m/7)|v_t|`, applied opposite the slip direction `t̂ = v_t/|v_t|`. Coulomb caps it by the friction cone:

```
j_t   = min( j_t*, μ · j_n )            // μ = μ_s while gripping, μ_k while sliding
J_t   = −j_t t̂
```

Apply both impulses and the induced torque (only friction contributes torque):

```
v  += (J_n + J_t) / m
ω  += I⁻¹ (r_c × J_t)                    // = (1/I)(r_c × J_t)
```

**Rolling vs sliding, and the transition.** The single number that decides everything is the contact-point velocity `v_c = v + ω × r_c`:

- If `j_t* ≤ μ_s j_n`, static friction can kill the slip this step → the ball **grips**, `v_t → 0`, and it settles into **rolling-without-slipping** (`v = ω × r` at the contact, contact-point velocity zero). This is the natural attractor for a ball on the playfield.
- If `j_t* > μ_s j_n`, friction saturates at `μ_k j_n` → the ball **slides**, and the capped tangential impulse simultaneously *decelerates the slip and spins up the ball* (a struck-flat ball skids then catches into a roll — the skid-to-roll transition emerges, it is not scripted).

**Rolling resistance** (a real, separate loss from Coulomb friction — hysteresis of steel on coated wood). Applied only while in rolling contact, as a torque opposing `ω` about the contact, magnitude `τ_rr = C_rr · N · r`, equivalently a small force `C_rr·N` opposing motion. `C_rr ≈ 0.001–0.005` for hard steel on a hard surface; the playfield clearcoat/Mylar puts us at the low end. This is what makes a rolling ball eventually coast to a stop rather than roll forever — it sets *coast distance*. It is **not** why balls drain: draining is driven by gravity's down-slope component (`g·sinα`, §6.1.1); rolling resistance *opposes* that motion (at most `C_rr·g·cosα` ≈ 0.05 m/s², ~23× weaker than the 1.110 m/s² down-slope drive). It is load-bearing for pacing — how far a slow ball rolls before settling — not cosmetic.

#### 6.1.3 Spin, explicitly

Spin is a first-class state (`ω`) and the main skill dimension of pinball, so it gets explicit treatment.

**How spin is generated.** Entirely through the **tangential friction impulse** of §6.1.2. Three regimes:
1. *Rolling on the playfield* continuously enforces `ω = (n × v)/r` (rolling constraint), so a ball moving across the field always carries the matching roll spin.
2. *Glancing hits on posts/walls/rubbers* — when `v` is not along `n`, there is slip `v_t`, so friction fires `J_t` and torques the ball: `Δω = (1/I)(r_c × J_t)`. A ball clipping a post leaves spinning.
3. *Flipper strikes* — the flipper surface moves (`u = ω_flip × r_flip`), so even a "dead" ball gets a large `v_t` against a swinging flipper → large friction impulse → large `Δω`. This is the mechanism behind flipper spin control (§6.1.6).

**How spin changes outgoing angle (tangential friction coupling).** On a post/wall bounce the friction impulse is tangential, so it deflects the outgoing velocity *sideways* relative to a spin-free mirror bounce, by roughly `Δv_t = J_t/m`. A ball with spin `ω` arrives with contact-slip `v_t = v_tangential + (ω × r_c)`; the sign of the pre-existing spin adds to or subtracts from the wall-induced slip, so **backspin/topspin/sidespin change the rebound angle**. Quantify the deflection cap: the maximum sideways velocity change is `μ (1+e)|v_n|` (friction cap ÷ m, with `j_n = (1+e)m|v_n|`), i.e. for `μ = 0.2, e = 0.5`, up to `0.3|v_n|` of the incoming normal speed is convertible to lateral speed. This is the physical basis of "using spin to make a shot" and it is entirely emergent from the impulse law.

**Airborne Magnus force.** For an airborne, spinning ball:

```
F_Magnus = ½ ρ C_L A (ω̂ × v) |v|          // lift ⟂ to both spin axis and velocity
```

with a spin-parameter model for the lift coefficient: `S = r|ω|/|v|` (dimensionless spin ratio), `C_L ≈ min(k_L · S, C_L,max)` with `k_L ≈ 0.25` and `C_L,max ≈ 0.35` (consistent with spinning-sphere/baseball data, where `C_L` rises roughly linearly with spin parameter and saturates).

**Is Magnus actually noticeable? The honest answer: barely.** Worked magnitudes for our 80.6 g steel ball (weight `mg = 0.791 N`):

- Rolling-rate spin (`S = 1`) at `v = 5 m/s`, `C_L ≈ 0.25`: `F_Magnus ≈ 2.1 × 10⁻³ N` ≈ **0.27 % of gravity**; lateral accel ≈ 0.027 m/s²; over a 0.3 s hop the sideways deflection is ≈ **1 mm**.
- Extreme spin from a sharp glancing flipper hit (`|ω| ≈ 2000 rad/s`, `S ≈ 3.4`) at `v = 8 m/s`, `C_L ≈ 0.35`: `F_Magnus ≈ 7.7 × 10⁻³ N`, lateral accel ≈ 0.095 m/s², deflection over 0.3 s ≈ **4 mm**.

So for a dense steel ball at pinball speeds Magnus is real but small (a few mm at most on a typical airborne hop), because steel's high mass makes the aerodynamic-to-inertial ratio tiny. **Decision:** implement it, physically correctly, as a fully tunable term (`C_L` model in data). Keep it *on* for fidelity — it is the physically honest choice and it produces a small, correct curve — but do not oversell it as a gameplay feature. The tests still assert it produces the *predicted* deflection (§6.1.8), because "correct and tiny" is the standard, not "impressive."

#### 6.1.4 Aerodynamic drag & resistance

**Quadratic drag** on the ball's centre of mass:

```
F_drag = −½ ρ C_D A |v| v          // opposes velocity
```

with `C_D ≈ 0.47` for a smooth sphere. Reynolds number at `v = 5 m/s` is `Re = ρ v d/μ ≈ 9 × 10³` (using `μ_air = 1.81 × 10⁻⁵`), well below the ~2 × 10⁵ drag-crisis, so a constant `C_D ≈ 0.47` is valid across the whole pinball speed range.

**Honest magnitude:** at `v = 5 m/s`, `F_drag ≈ 4.0 × 10⁻³ N` ≈ **0.5 % of the ball's weight**, giving a deceleration of ≈ 0.05 m/s². The drag-only terminal velocity would be `v_t = √(2mg / (ρ C_D A)) ≈ 70 m/s` — an order of magnitude beyond any pinball speed — which is the clean way to say **air drag is negligible at this scale**. It is included because it is physically correct and cheap, and because the terminal-velocity test (§6.1.8) needs it, but it is a small correction term, not a gameplay lever. Same story as Magnus: correct, present, honestly minor.

**Rolling resistance** (§6.1.2) is the *dominant* non-Coulomb loss for a ball on the playfield and is the term that actually shapes gameplay pacing (how far a slow ball coasts), unlike air drag. Keep the two conceptually separate: air drag acts on airborne/rolling translation via `v²`; rolling resistance acts only in rolling contact via `C_rr N`.

#### 6.1.5 Integration & CCD

**Fixed-timestep semi-implicit (symplectic) Euler**, decoupled from the render/converge cadence. The renderer converges a *static* scene with TAA/accumulation; the physics advances on its own clock and the renderer samples the latest ball pose (interpolated) when it (re)builds/updates the ball primitive. Per substep:

```
// semi-implicit (symplectic) Euler — update velocity first, then position
v  += (F_gravity + F_drag + F_magnus)/m * h + Σ(contact impulses)/m
ω  += I⁻¹ (Σ contact torques) − damping
P  += v * h
q   = normalize(q + ½ (ω⊗q) * h)
```

Symplectic Euler is chosen over RK4 because it does not pump energy into oscillatory/resting contact (bounded energy error), which is exactly the stability property a resting/captured ball needs.

**Timestep:** fixed `h = 1 ms` (**1000 Hz**) base rate, matching the practice of serious pinball simulators (VPX runs its physics near 1000 Hz for the same tunnelling reasons). The render frame consumes an accumulator and runs `⌈Δt_render / h⌉` substeps; leftover time is carried, never stretched, so `h` is *always* fixed (determinism). Optionally adaptive **sub-stepping within CCD**: when a swept query reports a time-of-impact `τ < h`, advance to `τ`, resolve, and continue the remaining `h − τ` — but the *outer* clock stays fixed.

**Why CCD is mandatory (not optional).** A live ball reaches 5–10 m/s. At 1000 Hz it travels 5–10 mm per step — comparable to the ball's own diameter and *larger than* a thin wall/rail's thickness. Discrete collision (test only endpoints) would let the ball **tunnel** straight through posts, rails, and ramp walls at high speed. No fixed step small enough to prevent this is affordable (you'd need ~10 kHz+ and still fail on the fastest shots). CCD is therefore load-bearing.

**Swept-sphere / conservative advancement**, reusing the BVH broadphase:

1. Broadphase: query `BVH` with the AABB of the swept sphere (start sphere ∪ end sphere) → candidate colliders (§6.1.2).
2. Narrowphase TOI: for each candidate, compute the earliest `τ ∈ [0, h]` where the moving sphere first touches it — closed-form for plane/sphere/cylinder/capsule (solve the quadratic in `τ` for centre-distance = `r + R`), and **conservative advancement** for the mesh/arc cases: repeatedly step the ball forward by `separation/|v_rel|` until separation < ε, which cannot overshoot a contact.
3. Advance the ball to the earliest `τ` across all candidates, resolve that contact (impulse), then re-sweep the remaining `h − τ`. Cap iterations (e.g. 8/step) to bound worst-case multi-contact, then fall back to a speculative-contact resolve so it can never infinite-loop.
4. **Speculative contacts** for the resting/tight case: also gather contacts within a small margin `ε` ahead and solve them in the same impulse batch, which keeps a ball wedged between two rails stable instead of alternating penetration/ejection.

**Determinism discipline** (to match the repo's fixed-seed culture): fixed `h`; a single fixed contact-processing order (sort contacts by a stable key — collider id, then contact position — before solving); all narrowphase/integration in `double`; any stochastic term (VPX-style "scatter" on bounce angle) drawn from a **seeded** PRNG threaded through the sim, so a given (seed, input timeline) reproduces bit-for-bit. This is what makes the §6.1.8 tests meaningful and repeatable, exactly as the renderer's fixed-seed captures are.

#### 6.1.6 Actuators

**Flippers — kinematic rigid bodies with prescribed, torque-limited angular motion.** A flipper is *not* a free rigid body; it is driven toward a target angle and treated as infinite-mass-along-its-constraint when it hits the ball (the coil/EOS holds it), which is both physically reasonable (the solenoid is far stronger than the ball) and numerically stable.

- State: pivot point, current angle `θ`, angular velocity `ω_flip`, min/max stops.
- Drive: a torque-limited controller accelerates `θ` toward the target (up when energised, rest position when not), clamped by a `MaxAngularAccel` derived from coil strength; a **coil ramp-up** (≈25 ms) so light taps produce partial swings; an **EOS holding torque** at full extension; a weaker **return** spring (return/forward ratio ≈ 0.05–0.09, VPX-typical) so cradle/live-catch tricks are physically possible.
- Ball coupling: the flipper is a moving `CapsuleCollider` (its rubber-clad bat) with **surface velocity** `u = ω_flip × r_flip` at the contact. Feeding `u` into the §6.1.2 impulse law transfers **both linear and angular impulse**: the ball gets pushed (normal impulse from the advancing surface) *and* spun (tangential friction against the sweeping surface). The tangential term is the whole skill mechanic — where on the bat and at what phase of the swing you strike sets the outgoing spin and thus the shot. Nothing here is scripted; it is the same friction impulse as a wall, with a non-zero `u`.

**Plunger — spring launch.** A 1-DOF kinematic body along the shooter lane axis. Pull-back compresses a spring (Hooke, `F = −k x`, with the real plunger's `k` and max travel as data); release integrates the spring + the ball's reaction until separation, imparting `v = ` (spring energy → ball KE, minus lane friction). Model it as a moving `PlaneCollider`/cap on the lane axis so the same contact solver launches the ball — the ball leaves when the plunger decelerates below the ball's speed. An auto-plunger is the same with a fixed impulse.

**Bumpers / kickers / slingshots — active impulse zones.** Geometrically a `CylinderCollider` (pop bumper) or angled `QuadCollider` (slingshot). On a contact whose approach speed exceeds a trigger threshold, in addition to the passive restitution they inject a **scripted outward impulse** `J_active = s · n` (coil strength `s` as data), giving the ball more energy than it arrived with. This is the one place energy is *added* — physically justified (the coil does work) — and it is a measured impulse magnitude, not a feel knob. Restitution boost and the trigger threshold are per-device data.

**Nudge / tilt.** A nudge applies an **impulse to the ball** (`Δv = J_nudge/m` in the nudge direction) *and* momentarily displaces the whole table frame (so static colliders shift, which is what actually saves a drain). A **tilt-bob sensor** integrates accumulated nudge energy over a short window; crossing a threshold trips TILT. Model the bob as a tiny pendulum/leaky-integrator with a seeded, deterministic threshold so nudge strength trades against tilt risk exactly as on a real machine.

#### 6.1.7 Build vs buy — recommendation

**Recommendation: build the custom single-ball analytic solver** in `RayTracer.Core/Physics/*`. This is the correct call given the "one dynamic ball + static world + a few kinematic actuators" shape and, decisively, the repo's determinism-and-testability culture. Judged on the three axes that matter here:

| Axis | Custom analytic solver | BepuPhysics v2 (pure-C#) |
|---|---|---|
| **Physical control** | Total: we own the exact impulse law, spin coupling, Magnus, drag, rolling resistance as first-class terms | Good general rigid-body/CCD/friction, but Magnus/drag/rolling-resistance are custom force callbacks bolted onto a general solver; less direct |
| **Determinism** | Deterministic by construction — fixed `h`, fixed contact order, `double`, seeded PRNG; reproducible bit-for-bit **within a pinned build/toolchain** (transcendentals are not bit-portable across runtime/arch) | Fast and deterministic *within a build*, but iterative multi-body solver, SIMD paths, and internal ordering make cross-platform bit-reproducibility and analytic-tolerance assertions harder to guarantee |
| **Testability** | Every term maps to a closed-form test (§6.1.8); a sphere-vs-primitive contact has an analytic answer we can assert on | Tests observe an opaque general solver; asserting "energy conserved to 1e-9" or "exact Magnus deflection" fights the engine's own soft-constraint tolerances |
| **Fit / weight** | ~one collider zoo + one impulse solver; reuses the existing `BVH`; no dependency | Mature, heavier dependency built for many-body scenes we will never have (thousands of contacts, stacking) |
| **Effort** | We write the collider narrowphase + CCD ourselves (real work, but bounded — sphere-vs-primitive is closed-form) | Less code to write for general dynamics, but integration + determinism/tuning work is non-trivial |

**Why custom wins here:** pinball never needs general N-body dynamics, stacking, or articulated constraints — the things a library like Bepu exists to provide. It *does* need exactly the terms a general engine treats as second-class (analytic spin coupling, Magnus, drag, rolling resistance) and exactly the guarantees a general engine makes hardest (closed-form-testable determinism). A single sphere against analytic primitives is genuinely simple to solve *exactly*, so we get more physical fidelity and better tests with less-suited machinery, not more.

**The alternative, honestly:** if scope ever grew to multi-ball with ball-ball collisions *and* complex simultaneous multi-contact under load, or if custom CCD robustness proved a time sink, **BepuPhysics v2** is the right fallback — it is pure C#/.NET (no native dependency, fits the CPU-first rule), fast, and has mature CCD, friction, and angular dynamics. Ball-ball contact is itself analytic (two-sphere impulse), so even multiball does not by itself force a library. We would only reach for Bepu if the numerical-robustness work (§6.1.9) outgrew the payoff of owning it. Design the `PhysicsWorld` API so the solver is swappable behind an interface, hedging that risk without paying for it now.

#### 6.1.8 Validation — deterministic physics unit tests

These are how realism is *proven*, in the repo's fixed-seed style. Each asserts against a closed-form result within a tolerance scaled to `h`. (Note on semi-implicit Euler: velocity is exact at step boundaries for constant acceleration, `v(t) = a·t`; position carries a bounded `+½ a t h` offset — so velocity-vs-time tests are tight, distance tests use an `O(a·h·t)` tolerance.)

1. **Frictionless incline reaches analytic speed.** Ball released from rest on a frictionless plane at incline `α`; after time `t`, assert `|v| = g sin α · t` (tight), and after sliding slope-distance `L`, `|v| = √(2 g sin α · L)` (tolerance `O(a h)`). At `α = 6.5°`, `a = 1.110 m/s²`.
2. **Energy conservation (frictionless).** With drag/friction/rolling-resistance off, total mechanical energy `½m|v|² + ½I|ω|² + m g h_height` stays constant across a long roll/bounce trajectory to within a bounded drift (symplectic Euler gives *bounded*, non-growing energy error); assert `max |E(t) − E₀|/E₀ < tol` with no secular growth over e.g. 10 s.
3. **Rolling-without-slipping steady state.** Ball placed on the flat playfield with mismatched `v` and `ω`; after settling, assert the rolling constraint `|v| = |ω| r` and contact-point velocity `≈ 0`. On the incline, assert the rolling ball's steady acceleration is `5⁄7 · g sin α = 0.793 m/s²` (the solid-sphere factor), *not* `g sin α`.
4. **Backspin Magnus lateral curve.** Launch an airborne ball with pure backspin (spin axis ⟂ velocity, horizontal); integrate the ballistic arc; assert the lateral deflection matches the Magnus prediction `≈ ½ (F_Magnus/m) t²` for the modelled `C_L(S)` — i.e. ~1 mm at rolling spin / a few mm at extreme spin over a 0.3 s hop (§6.1.3). The test asserts *the predicted small value*, confirming the term is correct and correctly minor.
5. **Drag terminal velocity.** Drop the ball in free fall with gravity + drag only; assert `|v|` asymptotes to `√(2mg/(ρ C_D A)) ≈ 70 m/s` and that at pinball speeds (≤10 m/s) the drag deceleration is `≈0.05 m/s²` (i.e. the honest "negligible" claim is a *tested* fact, not an assertion).
6. **Restitution bounce-height decay.** Drop from height `h₀` onto a plane with restitution `e`; assert successive apex heights follow `hₙ = e^{2n} h₀` (energy scales by `e²` per bounce) within tolerance, for a few material `e` values.
7. **No-tunnelling stress test (CCD).** Fire the ball at max speed (10 m/s, and an over-speed 20 m/s guard) directly at a wall thinner than one substep's travel (e.g. 3 mm thick at `h = 1 ms` → 10 mm/step). Assert the ball ends up on the *incoming* side with a correctly reflected velocity, never past the wall. This is the test that justifies CCD's existence.
8. **Determinism/regression.** Run a fixed input timeline from a fixed seed twice on one build; assert bit-identical `BallState` streams (valid within a machine/build). The *committed* golden-trajectory regression compares the pose stream within an `h`-scaled tolerance (matching tests 1–7), **not** a bit-hash — transcendentals (flipper `sin/cos`, `atan2` narrowphase, tilt bob) are not bit-identical across .NET runtime/architecture and CI runs on hardware other than the golden-generating box (§9.7). (If an exact hash is wanted, pin generation + verification to one toolchain and keep `Math` transcendentals off the per-step hot path.)

Supporting material data for the tests lives as SI constants so the expected values are computed, not hard-coded magic numbers.

#### 6.1.9 Honest risk & tuning

Consistent with the governing principle, the hard problems here are **numerical**, not "how much to fake." We are not trading realism against feel; we are fighting the finite-precision, finite-timestep reality of simulating real dynamics.

**What is genuinely hard:**
- **Stable resting / captured-ball contact.** A ball held in a saucer/kicker or resting in a cradle must sit *still* — no jitter, no slow sink, no energy creep. This needs a **restitution velocity threshold** (set effective `e→0` when the approach speed `|v_n|` is below a few cm/s, so the per-step gravity-injected `v_n` is absorbed rather than reflected — otherwise `j_n = −(1+e)m·v_n` micro-bounces a resting ball forever; keep the threshold well below the §6.1.8 #6 launch speeds), speculative contacts, a small penetration slop with Baumgarte-style position correction (bounded, so it doesn't inject energy), and sleeping (freeze a body whose `|v|,|ω|` stay below a threshold for N steps). Symplectic Euler + resting-contact stabilisation is the crux.
- **Fast multi-contact in tight lanes.** A fast ball pinging down a narrow channel touches two walls in one step; naive sequential resolution can eject it or lose energy asymmetrically. Solve the small contact set together with a stable ordering, and cap CCD sub-iterations with a speculative fallback so it terminates.
- **CCD robustness.** Time-of-impact solves must be conservative (never miss a contact) yet not stall (conservative advancement can crawl near grazing contacts). Needs iteration caps, grazing-angle handling, and a guaranteed-terminating fallback. This is the single biggest source of "impossible" bugs (ball through a wall, ball stuck in geometry).
- **Symplectic stability at stiff contacts.** Very high effective stiffness (hard steel on hard clearcoat, `e` near steel-on-steel 0.6) plus a 1 ms step can ring; the impulse (not penalty-force) formulation avoids stiffness blow-up, but restitution/friction ordering still needs care.
- **Precision & determinism across machines.** `double` narrowphase, fixed operation order, and a seeded PRNG are required to keep the §6.1.8 tests reproducible; the `float` broadphase BVH must stay strictly a conservative culler so its lower precision can never change the physical result.

**Coefficients are measured data, not feel knobs.** Every tunable is a physical property to *source or measure*, stored as SI data in `Physics/PhysicsConstants.cs` and per-material `PhysicsMaterial` records. Starting values from literature (to be refined by measurement against real machines):

| Parameter | Symbol | Typical value | Note / source |
|---|---|---|---|
| Restitution, steel↔steel | e | ≈ 0.6 | posts, metal guides |
| Restitution, steel↔clearcoat wood | e | ≈ 0.2–0.35 | playfield (VPX uses ~0.25) |
| Restitution, steel↔rubber | e | ≈ 0.8–0.9, falls off with speed | rubbers/flippers (VPX elasticity ≈ 0.88, with falloff) |
| Friction, steel↔coated playfield | μ | ≈ 0.1–0.25 | clearcoat/Mylar lowers raw steel-on-wood (~0.5); VPX playfield ~0.075–0.25 |
| Friction, steel↔rubber | μ | ≈ 0.3–0.6 | grip that enables spin/backhands |
| Rolling resistance | C_rr | ≈ 0.001–0.005 | steel on hard surface |
| Drag coefficient | C_D | ≈ 0.47 | smooth sphere, Re ≈ 10⁴ (sub-crisis) |
| Magnus lift model | k_L, C_L,max | ≈ 0.25, 0.35 | spin-parameter fit (spinning-sphere/baseball data) |
| Restitution falloff / scatter | — | small, seeded | VPX-style velocity-dependent `e` and bounded angular scatter, deterministic |

**The accepted trade-off, stated plainly.** Simulating for real means the game will *not* exactly reproduce the 1995 *Space Cadet*'s hand-authored feel — that game's physics were a 2D approximation with tuned constants, not a 3D rigid-body sim. Divergence from its feel is the honest, accepted consequence of the realism pillar, the same way physically-based spectral rendering doesn't reproduce a 1995 palette. Where we tune, we tune *toward measured reality* (real restitution/friction/incline), never toward nostalgia. If a future product decision wants the old feel back, that becomes an explicit, separate "arcade mode" layer on top of the honest simulation — never a corruption of it.

### 6.2 Fixed timestep, decoupled from converge

This is the key systems decision, and it maps cleanly onto the engine.

- **Physics runs on a fixed timestep** (**1000 Hz** base with CCD
  sub-stepping for the fast ball — see §6.1.5) — deterministic, independent
  of frame rate, the standard pinball approach.
- **The renderer runs its own converge loop** at whatever rate the 3070 delivers.
  Between physics ticks, the static half keeps accumulating; the mover instances
  are updated from the latest physics state each rendered frame via `UpdateSpheres`
  (ball) and `SetDynamicPose` (flippers/plunger).
- **The handoff is the new interface:** gameplay produces, per rendered frame, a
  ball centre + flipper/plunger poses; the renderer consumes them into the TLAS.
  When a mover changes, only that mover's pixels restart (§4.3 hit-id); the
  converged static half is untouched. This is why decoupling works: the expensive
  render state is stable, and only cheap mover updates cross the boundary each
  frame.
- **Determinism:** with a fixed seed and fixed dt, a scripted input sequence
  replays identically — the basis for regression captures (§9) and for tuning the
  "feel" reproducibly.

### 6.3 Input

- Poll input at the physics rate, not the render rate, so fast flips register
  regardless of frame timing.
- Flippers are **hold-state** (pressed = raised), plunger is **charge-and-release**
  (Space held builds impulse), nudge is **impulse-on-press** with a tilt
  accumulator. Preserve these relationships (§3.6) — they define the feel.
- Rebindable, keyboard + gamepad (§3.6 table).

### 6.4 Game state & scoring

- **State machine:** attract → ball-launch (skill shot) → in-play → drain →
  (replay/extra-ball?) → next ball / game-over → high-score entry.
- **Progression model:** rank ladder (9), progress-light ring (assumed 18 → promotion; §3.1),
  mission select/accept/complete loop, fuel, replay (mission-preserving),
  tilt-freeze. Encode mission definitions as **data** (objective, reward points,
  reward progress lights) so tuning the tables never touches code. The values are
  already pinned to the code-verified constants in §3 (from `control.cpp`).
- **Scoring** is data-driven off those same tables — the §3 awards, select scores,
  per-switch values and the `{1,2,3,5,10}` multiplier are exact, not placeholders.
- **Presentation of state** = the emissive inserts and light rings on the **static**
  side — toggling a `SurfaceKind.Emissive` value is a cheap, spectral, converging
  update, not a UI overlay.

### 6.5 Audio (note only)

The engine has **no audio**. Faithful Space Cadet needs the Matt Ridgeway synth
stings, laser/zap SFX, and spoken callouts (mission select/complete, promotion,
tilt, replay). Recommend a small, separate audio subsystem (e.g. a
WinForms-friendly audio library) driven by the game-state machine's events —
explicitly out of the renderer. Scoped as its own late milestone; assets to be
sourced/recreated separately (licensing of the original audio is an open question,
§10).

---

## 7. Engine ↔ maze separation, and project structure

The pinball game must sit on a **clean engine seam**. Today the maze content is
woven through both `RayTracer.Core` and the `RayTracer.Gpu` app, so before (or as
an early parallel track to) the pinball work we **extract the reusable renderer
from the maze**. This is **Milestone E** in the roadmap (§8) and a prerequisite
for the game living beside the maze on the same foundation; it also has standalone
value regardless of pinball.

### 7.1 What leaves `RayTracer.Core` (the engine)

The core is *nearly* clean already — only three real leaks of maze content into the
engine, all small and contained:

1. **`Pipeline/CameraController.cs` hard-depends on `MazeNavigator`** (holds a
   `MazeNavigator _nav`; ctor `(MazeNavigator, cellSize, eyeHeight)`). Extract a
   generic camera-driver seam (an `ICameraDriver` / waypoint interface) in Core;
   the maze's navigator-driven controller moves to `RayTracer.Maze`. Pinball wants
   a **fixed / nudge** camera driver, not a walker — so this split pays off directly.
2. **A leaked content constant, `MazeGeometryBuilder.CellSize`**, is used as a
   world-scale / biome-size value inside the integrator — `Rendering/PathTracer.cs:173`
   (`biomeWorldSize = MazeGeometryBuilder.CellSize * biomeSizeCells`) and
   `Gpu/Phase4Reference.cs:196`. Promote world scale to an engine parameter (a field
   on `VolumetricOptions` / `RealismOptions`, or a `SceneScale`) passed in by the
   app — no content constant in the tracer.
3. **`Rendering/LensOptions.cs:28`** references `CellSize` only in a doc comment
   (cosmetic; reword to "1 world unit = N metres").

Plus three files that are content sitting in engine folders — pure moves to
`RayTracer.Maze`: `Geometry/Maze.cs`, `Geometry/MazeGeometryBuilder.cs`,
`Geometry/MazeNavigator.cs`, and `Gpu/MazeMinimap.cs`. The generic primitives
(`AABB`, `BVH`, `HitInfo`, `Ray`, `Tracable`, `TracableRectangle`, `Plane`,
`Sphere`, `BrickRectangle`, `CeilingTileRectangle`, `DecalRectangle`) stay.

### 7.2 Splitting `RayTracer.Gpu` (reusable backend vs maze app)

The **renderers are almost maze-free** — `Phase6Renderer.cs` has ~6 maze
references. The entanglement is concentrated in the **app host and config UI**:
`Program.cs` (~361 maze/classic refs) and `ConfigForm.cs` (~67). So the real work
of the extraction is splitting those two into a **reusable host-shell + config
framework** vs **maze-specific screens / wiring** — not touching the trace path.

- **Stays (reusable DXR backend):** `Phase1–6Renderer.cs`, `GpuRayTracer.cs`,
  `ShaderCompiler.cs`, `RegressionHarness.cs`, the HLSL shaders, `CpuRenderForm.cs`,
  `MovieProgressForm.cs`, and the generic shell / config framework factored out of
  `Program.cs` / `ConfigForm.cs`.
- **Moves to `RayTracer.Maze`:** `MazeBubbles`, `MazeHedges`, `MazeJewels`,
  `MazeMirrors`, `MazeOilSlicks`, `MazeProps`, `MazeThreshold`, `MazeWater`,
  `MazeWindows`, `ClassicMode`, `PropTextures`, `Screensaver`, and the
  maze-specific parts of `Program.cs` / `ConfigForm.cs`.

### 7.3 Target project layout (six assemblies)

| Assembly | Role | Depends on |
|---|---|---|
| **`RayTracer.Core`** | Pure engine — CPU integrator + pure-C# GPU `*Reference.cs` replicas. **No maze, no pinball.** | — |
| **`RayTracer.Gpu`** | Reusable DXR 1.1 backend + generic host-shell / config framework. **No maze content.** | Core |
| **`RayTracer.Maze`** *(new, extracted)* | The existing maze / screensaver app, lifted out of `RayTracer.Gpu`. | Core, Gpu |
| **`Pinball.Core`** *(new)* | Pure, testable gameplay: `Physics/*` (§6.1), `Game/*` (rank / mission state, scoring), `Table/*` (colliders + part→instance mapping), input abstraction. No rendering, no WinForms. | Core |
| **`Pinball.App`** *(new)* | The pinball WinForms game host + fixed-timestep loop, wiring input → physics → the backend's `UpdateSpheres` / `SetDynamicPose`. | Core, Gpu, Pinball.Core |
| **`Pinball.Tests` / `RayTracer.Tests`** | Physics determinism + scoring tests; CPU↔GPU parity for the mover branch; no-mover byte-identity. | all |

`RayTracer.Maze` and `Pinball.App` both consume the **same** `RayTracer.Gpu`
backend — building the pinball app on the exact seam the maze app uses is the proof
the extraction is real.

### 7.4 Guardrails / acceptance

The extraction is a **pure move-and-reparameterize with no behaviour change**. Its
acceptance test is conservative and is exactly the repo's existing discipline:
`--phase6-selftest` stays **700/700**, `--phase6-regress` stays **19/19 bit-exact**,
and the **maze goldens do not move**. The CPU→`*Reference.cs`→HLSL parity is
untouched (the reference replicas move or stay wholesale, never rewritten).

## 8. Phased roadmap / milestones

Each phase: **goal → deliverable → verification.** Renderer changes follow CPU →
C# reference → HLSL, keep the no-mover path byte-identical, and stay green on
`--phase6-selftest` / `--phase6-regress`. GPU self-tests run **only on the local
RTX 3070**. Two tracks run in **parallel**: the renderer mover-support chain
(P1–P4) and the physics core (P5, which depends only on `RayTracer.Core`); Milestone
E and table authoring (§5.3) feed both.

**Milestone E — Extract the engine from the maze (prerequisite, precedes P0).**
Goal: a clean engine seam the game can sit on (§7). Deliverable: break the three
`RayTracer.Core` maze leaks (generic `ICameraDriver`; world-scale as an engine
parameter; move `Maze*` / `MazeMinimap` out); split `RayTracer.Gpu` into the
reusable backend + a new `RayTracer.Maze` app (host-shell / config framework
factored out of `Program.cs` / `ConfigForm.cs`). Verify: **pure move-and-
reparameterize** — `--phase6-selftest` 700/700, `--phase6-regress` 19/19
bit-exact, maze goldens unmoved; the maze runs from `RayTracer.Maze` on the
extracted backend.

*Status: DONE and CI-green (build + full test suite, windows).* The seam is six
assemblies:
- **`RayTracer.Core`** (net10.0) — pure engine, now **maze-free**. The three leaks
  are gone: engine-owned `SceneScale.WorldUnitsPerCell` for world scale, a generic
  `ICameraDriver` seam, `LensOptions` doc de-mazed.
- **`RayTracer.Maze.Core`** (net10.0, new) — pure maze logic
  (`Maze`/`MazeGeometryBuilder`/`MazeNavigator`/`MazeMinimap`/`CameraController`),
  lifted out of `Core`. Kept a plain net10.0 lib (not folded into the app) so the
  net10.0 `RayTracer.Tests`/`Benchmark` keep using the maze geometry as fixtures
  without dragging in WinForms/DXR. Mirrors the `Pinball.Core`/`Pinball.App` split.
- **`RayTracer.Gpu`** (net10.0-windows) — reusable DXR backend `Library`, maze-free
  (6 renderers + `GpuRayTracer` + `ShaderCompiler` + `MovieProgressForm` + shaders).
  `Phase6Renderer`'s last maze refs severed: world scale → `SceneScale`; the prop
  decal atlas is host-injected via `Phase6Renderer.DecalAtlasProvider`; the rat
  billboard layer is a local `RatDecalLayer` constant.
- **`RayTracer.Maze`** (net10.0-windows, new) — the maze/screensaver WinForms app
  (`Program`/`ConfigForm`/`Screensaver`/`Phase*Scene`/all `Maze*` drawers/
  `RegressionHarness` + the 19 goldens), on the extracted backend.
- **`RayTracer.Tests`** (net10.0) — unchanged TFM; refs `Core` + `Maze.Core`.

`InternalsVisibleTo` (`Core`→`Maze`/`Maze.Core`) kept every move byte-for-byte.
Reference graph is acyclic and `Gpu` does **not** reference `Maze.Core`. **Only
remaining, 3070-only:** run `--phase6-selftest` (700/700) and `--phase6-regress`
(19/19 bit-exact, maze goldens unmoved) on the RT GPU — hosted CI compiles the DXR
host but cannot dispatch it.

**P0 — Static table render (no gameplay).**
Goal: prove the Space Cadet playfield renders and converges as a static spectral
scene. Deliverable (two of these are weighty sub-milestones, not one bullet): **(i)
emissive-surface shading** (§5.3, new — prerequisite for the intended look); **(ii)
clean-room table geometry + co-registered colliders from a single source** (§5.3);
(iii) materials (neon `Emissive`, glass `Dielectric` dome, `Mirror`/`ThinFilm`
accents), a fixed camera, built via `RebuildScene`; a fixed-seed still capture. Verify: the still converges to a clean
spectral image; a golden still is added to the regress harness; existing goldens
unmoved.

**P1 — Dynamic classification + cheap mover shading branch.**
Goal: admit movers into the trace as a capped, non-spectral tier. Deliverable:
`Dynamic` bit on `GpuPrimitive`/`GpuSphere`; `MoverSpecularBounces`; the
single-bounce hero/RGB direct-only branch in `PathTracer.cs` → mirrored in
`Phase2Reference.cs` → ported to `PathTracePhase6.hlsl`; a `GpuPhase*Tests` parity
test. Verify: a chrome `Dynamic` sphere shows a 1-bounce mirror of the static
table + a hard contact shadow; **no-mover goldens bit-identical**; CPU↔GPU parity
passes.

**P2 — Hit-id restart (the ghost-trail fix).**
Goal: kill the mover trail. Deliverable: per-pixel dynamic/instance id replacing/
augmenting the 1-bit `LastHit`, restart-on-id-change, CPU-first then HLSL. Verify:
a scripted ball sweep leaves **no trail**; no-mover `hitMask`/goldens unchanged.

**P3 — Mover TAA stencil.**
Goal: no history smear on movers. Deliverable: `TaaNextValid=false` for mover
pixels in `ResolvePhase6.hlsl`/`TaaResolver`. Verify: fast mover has crisp edges;
static half still reaches high effective spp; goldens bit-identical.

**P4 — Rigid mover poses + AS refit without stalls.**
Goal: real-time movers at 60 fps. Deliverable: `SetDynamicPose(instance,
Matrix3x4)` for the dynamic triangle BLAS (flippers/plunger); ball via
`UpdateSpheres`; **AS refit folded into the trace command list**, extra
`WaitForGpu` removed. Verify: on the 3070, a scripted animation of ball + flippers
holds 60 fps at Classic-preset internal resolution.

**P5 — Physics core (`Pinball.Core`) — parallel track, can start alongside P0/P1.**
Goal: a physically honest ball. Depends only on `RayTracer.Core`'s existing BVH, not
on P1–P4; only the final integration check needs P4's pose interface. Front-load the
CCD-robustness and resting-contact spikes of §6.1.9 — they are the true critical path. Deliverable: fixed-timestep integrator (1000 Hz +
CCD, §6.1.5), analytic colliders, flippers/bumpers/kickers/spinners
responders, nudge/tilt, decoupled from the converge loop. Verify: unit tests for
determinism (fixed seed + input replay → identical trace) and for each responder;
a playable ball on the P0 table via the P4 pose interface.

**P6 — Game host, input, loop (`Pinball.App`).**
Goal: playable end-to-end. Deliverable: WinForms game window + fixed-timestep loop
wired input → physics → renderer; ConfigForm pinball presets/controls; new-game/
ball-launch/skill-shot. Verify: launch, flip, hit bumpers, drain, next ball; on
the 3070 at Classic 60 fps.

**P7 — Game state, missions, scoring, replay, tilt.**
Goal: the Space Cadet career. Deliverable: (a) the core progression state machine +
scoring / multiplier / replay / extra-ball / tilt-freeze — reward, select and
per-switch *values* are data (pinned to §3), the control flow is code; (b) the **~17
bespoke per-mission controllers** (arming sequences, the Time Warp rank fork, Black
Hole state-gating, the Maelstrom 8-stage checklist), sized proportional to
`control.cpp`; (c) high-score entry/persistence. Emissive inserts reflect state via
the §5.2 `UpdateEmissive` path. Verify: a full run
Cadet → a promotion; missions select/accept/complete; replay and tilt behave per
§3; scoring matches the (pinned) constants.

**P8 — RT Showcase preset + spectral polish + bloom.**
Goal: the marquee look. Deliverable: RT-Showcase preset (NEE + indirect + static
caustics on dome/gem + volumetric haze + Cine DoF + Filmic), moving-shadow
direct-window refinement, the new **bloom post-pass** around neon/lane lights.
Verify: Showcase at 30–60 fps on the 3070; Classic still 60; goldens for the
no-mover scenes unchanged.

**P9 — Audio + attract mode + finish.**
Goal: shippable. Deliverable: audio subsystem (stings/SFX/callouts) driven by
game events; attract mode; balance / feel tuning against the original. Verify: full playtest to Fleet Admiral incl. Maelstrom; feel matches the
original within tuning tolerance.

---

## 9. Working conventions carried over

These are the engine's standing disciplines, and they bind the pinball work too:

1. **CPU → C# reference → HLSL.** Every renderer change lands first in
   `PathTracer.cs` (CPU), then the pure-C# `*Reference.cs` (the contract), then
   `PathTracePhase6.hlsl` as a line-for-line port. CI has no GPU; it pins
   reference↔CPU parity.
2. **Byte-identical no-effect path.** A scene with **no `Dynamic` primitives** must
   render bit-identically to today. New work is gated behind the `Dynamic` flag /
   presets; `--phase6-selftest` (self-tests) and `--phase6-regress` (golden images)
   are the guardrails and stay green.
3. **Spectral-native.** Colour comes from physics on the static side; movers take a
   deliberately non-spectral hero/RGB branch (that is the point, not a shortcut).
4. **Gate on cost, expose via presets.** Extend `RenderPreset`
   (`Lighting`/`Volumetrics`/`Caustics`/`Realism`/`MotionSampleCap`) — Classic vs
   RT-Showcase. Lean on accumulation/TAA to average noise on the converging static
   half.
5. **Fixed-seed determinism.** Physics runs a fixed timestep + fixed seed so input
   replays reproduce exactly; regression captures fix the seed (per MEMORY:
   headless captures without `--seed` randomize and are incomparable).
6. **cbuffer discipline.** `TraceConstants` is a fixed 512-byte buffer (~320 B
   full); new mover flags pack into existing pad fields — overflow silently drops
   to 0 on the GPU.
7. **GPU self-tests only on the local RTX 3070.** The DXR self-tests and golden
   harness run on this box; CI covers the C# reference only.
8. **Trajectory comes from physics.** The physics analogue of spectral-native
   rendering: gravity, inertia, friction, restitution, spin and drag decide the
   ball's path — never a hard-coded feel-tweak. Every coefficient is a measured
   material property in SI units (§6.1), and the model is proven by deterministic,
   fixed-seed tests that assert against closed-form results (§6.1.8), exactly as
   the renderer is proven by golden images.

---

## 10. Open questions & risks

**Game-design fidelity — now code-verified.**
- **Resolved from source** (`k4zmu2a/SpaceCadetPinball`, `control.cpp`, MIT): Bug
  Hunt = **15 targets** (the "7" was the progress-light reward); Cosmic Plague =
  **1,750,000 + 11 lights** (not 2,000,000 + 12). The full 17-mission award table,
  select scores, per-switch base scores, the `{1,2,3,5,10}` multiplier ladder and
  the `middle_circle`/`outer_circle` rank mechanic in §3 are read directly from the
  code, not fan guides.
- **Still derived from the copyrighted data file** (`pinball.dat`, out of scope to
  ship): the exact `outer_circle` light total (used here as **18** — the code only
  requires "ring full"), and the rank / mission **display strings**
  (`STRING161–193`). The *mechanic and numeric awards* are from code; only the
  string text and light-group geometry live in the data file.
- Keep pinned to the **Windows** rules — the reimplementation's non-`FullTiltMode`
  path is authoritative (no multiball; each mission pays its base score with no
  jackpot folded in; missions award points + progress lights + replays).

**Physics fidelity & tuning.**
- Coefficients are **measured physical data to source or measure** (steel↔
  clearcoat / rubber restitution & friction, ball mass / inertia, real incline),
  not feel knobs — starting values and citations are in §6.1.9. The original's
  exact ball tuning has no published constants; we tune toward *reality*, and any
  divergence from the 1995 feel is the accepted consequence of the realism pillar.
- The hard risks are **numerical, not "how much to fake"**: stable resting /
  captured-ball contact, fast multi-contact in tight lanes, and CCD robustness at
  5–10 m/s (§6.1.9). CCD is mandatory — a fast ball tunnels thin geometry at any
  affordable fixed step (§6.1.5).
- **Full 3-D 6-DOF (not 2.5-D):** ramps, airborne hops and the ball's arc fall out
  of `F = ma`; wormhole / Gravity-Well *captures* stay scripted teleport-and-
  kickout events layered on top of the sim (§6.1, §6.4).

**Renderer correctness/perf.**
- **Ghost trail** (1-bit `LastHit`) — the single biggest correctness risk; fixed by
  the hit-id restart (P2). Must not perturb no-mover goldens.
- **AS refit stalls** — `UpdateSpheres`/`RunCausticBuild` each `WaitForGpu`; the
  mover refit must fold into the trace command list, prefer TLAS instance-transform
  over AABB rewrites (P4). This is the main thing between us and 60 fps.
- **Moving contact shadow lag** — direct-lighting change on converged pixels that
  don't restart; needs the direct-window mitigation (§4.4, P8). Under-solving →
  a shadow that trails the ball.
- **Mover TAA smear** — needs the stencil (P3); the no-mover goldens must stay
  bit-identical.
- **cbuffer overflow** — new mover flags must fit existing pad; silent zeroing on
  overflow.
- **No bloom/upscaler today** — bloom is a new post-pass (P8); there is no
  DLSS/FSR, so headroom comes from the fixed camera converging the static half,
  not from upscaling.
- **The pose interface (`SetDynamicPose`) does not exist yet** — it is built
  alongside `UpdateSpheres` (P4) and is the contract the physics/game loop feeds.
- **Emissive surfaces are unbuilt** — `SurfaceKind.Emissive` is an enum with no
  tracer term (§5.3); the neon/insert look *and* the ball's reflection of it depend
  on building it. The single largest renderer add.
- **Animated-insert reconvergence** — inserts/flashers change lighting mid-play, but
  there is no in-place emissive update or targeted reconverge today (needs §5.2
  `UpdateEmissive` + the §4.4 window). Without them an insert change either
  full-resets the frame or lags the accumulation window.
- **Ball ghost in static specular** — the moving ball reflected in static
  chrome/glass never restarts under the primary-hit id; needs the
  `pathTouchedDynamic` flag (§4.3, re-scoped P2).

**Product.**
- **Audio assets & licensing** — the original Ridgeway audio is out of scope to
  ship as-is; plan to source/recreate SFX and callouts (§6.5).
- **Table geometry authoring effort** — the playfield must be authored **twice**
  (render meshes in P0, analytic colliders in P5) and kept co-registered to
  sub-ball-radius precision; generate both from **one source-of-truth description**
  (§5.3) so they cannot drift. The main content cost — it deserves its own weighted
  sub-track, not two scattered bullets.

**Engine extraction (Milestone E, prerequisite).**
- The maze is woven through the app host: `Program.cs` (~361 refs) and
  `ConfigForm.cs` (~67) must split into a reusable shell + maze-specific screens —
  the bulk of the effort. The renderers themselves are nearly maze-free.
- Acceptance is a **pure move with no behaviour change**: `--phase6-selftest`
  700/700 and `--phase6-regress` 19/19 bit-exact stay green and the maze goldens
  do not move (§7).
