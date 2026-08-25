# Lighting, Sky & Post-Processing — phase notes

**To understand this, start by reading
[`LightingTuning.cs`](unity/Assets/RF/Scripts/Core/LightingTuning.cs),
[`ViewStack.cs`](unity/Assets/RF/Scripts/Core/ViewStack.cs) and
[`PostTuning.cs`](unity/Assets/RF/Scripts/Core/PostTuning.cs).** The first is what a scene is
lit by, the second is why there are now twice as many cameras, and the third is what happens
to the frame afterwards. Everything else here follows from those three.

This is item 1 of [MASTER_PLAN.md](MASTER_PLAN.md). It is done.

---

## What was actually wrong

The plan said the volume profile was Unity's untouched template with every effect at zero, and
it was. It missed the bigger half:

**No camera in the project had ever been told to run post-processing.** URP's
`renderPostProcessing` defaults to `false` and nothing set it — there was not even a
`UniversalAdditionalCameraData` component in `Sandbox.unity`. So the profile was inert twice
over: neutral values that no camera was asking for. Tuning bloom without fixing that would
have produced a byte-identical screenshot.

Three smaller corrections to the plan's premises, all confirmed by reading the assets:

- **HDR was already on** (`m_SupportsHDR: 1`) on both `PC_RPAsset` and `Mobile_RPAsset`, so
  bloom was never blocked.
- **SSAO already existed** as a renderer feature on `PC_Renderer` (intensity 0.4, radius 0.3).
  It was already in the "before" screenshots. The plan listed it as unconfirmed and expected to
  add it. `Mobile_Renderer` still has no renderer features at all — see [Known gaps](#known-gaps).
- **Metal already had a reflection source.** `m_DefaultReflectionMode: 0` is Skybox, so the
  `METAL` palette was reflecting Unity's default procedural sky all along. Plan item 4 (bake a
  reflection probe) mostly collapses into item 5: retuning the sky *is* the metal fix, and since
  levels are built from JSON at runtime a baked probe was never really on the table.

## The three open questions the plan left

1. **Gameplay camera** — `TopDownCameraRig`: perspective, FOV 50°, pitch 58°, distance 34 m,
   far clip 500 m, up to four split-screen viewports. That pins the vignette (drawn once per
   viewport, so it has to stay weak) and the shadow distance.
2. **HDR** — on, both tiers. Answered above.
3. **Level extents** — `LevelBounds.HalfExtent` defaults to 120, so 240 m across, which
   `iron-channel` uses. The editor accepts any value ≥ 1; there is no cap.

## The thing worth knowing about this game's sky

At pitch 58° with a 50° field of view, the *top* of the frame still points 33° **below** the
horizon. A player never sees sky. That is not a reason to leave it alone — it is the reason it
is tuned the way it is, because the sky turns up in two other places:

- **Reflection.** The scene's reflection source is the skybox, so `SkyTint`/`SkyExposure` are
  what barrels, rails and gun metal reflect.
- **The edge of the world.** A level's sea is a slab exactly `2 × HalfExtent` across. A camera
  looking down past its rim — which happens from any coast, well inside the visible range — sees
  the skybox's *lower* hemisphere through the gap. That is why `SkyGround` is a sea colour and
  not a ground colour. It is paint on a hole, and it is the one sky value a player reliably sees.

This is also why the plan's cheaper option was the right one: two colours and an exposure is all
this game's sky has to be, and Unity's procedural skybox already exposes exactly that. No custom
shader was written.

---

## Decisions

| Decision | Why |
|---|---|
| **Neutral tone mapping, not ACES** | The flat hand-picked palette *is* the homage. ACES shifts hue and pulls saturation out of exactly the colours `blender/rf/palette.py` fixed. Neutral does the job that was needed — stopping a 3.4 emissive clipping to white — and leaves the palette alone. One enum value to flip if you disagree. |
| **Interface on stacked cameras** | Both canvases are `ScreenSpaceCamera`, so post-processing lands on them. The obvious fix — a screen-space *overlay* canvas — is the one this project cannot take: `EditorUi` documents that an overlay canvas appears in nothing rendered to a texture, and every screenshot this project reviews itself with is rendered to a texture. A camera stack satisfies both. |
| **SMAA rather than MSAA** | Every edge here is a hard silhouette of an untextured primitive against a flat colour. SMAA costs a fraction of turning MSAA on in the pipeline assets and is good at exactly this content. |
| **Shadow distance 120 m, in the pipeline assets** | Sized to what the camera can see (≈53 m down the middle, ≈100 m at the corners of a wide split), not to the map. It lives in the two URP assets because URP reads its own `shadowDistance` and ignores `QualitySettings.shadowDistance` entirely — so a per-scene rig setting it would mean the last scene built decides what every other scene ships with. `LightingRig.ShadowDistance` records the number and a test fails if the asset drifts from it. |
| **Volume profile generated from code** | Same arrangement as `GeneratedMaterials`: the settings are a diff of C# rather than a diff of YAML with file IDs in it. The asset is rewritten **in place, never recreated**, because the global settings point at it by GUID. |
| **Sky material per mood** | `RF_Sky_Daylight` and `RF_Sky_Studio`. One shared sky would be a committed file whose contents flip-flopped with whichever scene builder ran last. |

### Where the look is *not* applied, and why

"One look everywhere" has three deliberate exceptions, each for a stated reason:

- **The level editor and the map overview drop the fog.** Both look at the world from 200 m up,
  and a haze tuned for a chase camera 30 m off the ground renders the whole map as one flat wash
  at that range. They stamp `LightingTuning.For(Daylight)` and set `Fog = false` — the
  copy-and-edit that `For` returning a fresh instance exists for.
- **The art preview runs no post-processing at all.** It is a contact sheet: twenty-five assets
  in a grid, laid out to be compared against each other and measured off. A vignette makes the
  same model read darker in the corner than in the middle, and a tone curve moves a colour away
  from the palette value somebody is checking it against. Verified: its backdrop measures
  `(54, 57, 57)` at the centre and at both far corners. It keeps the shared lighting.

---

## Gotchas found the hard way

**URP does not render camera stacks from an offline one-shot render.** This cost the most time.
After the interface moved onto stacked cameras, every still came back as a correctly graded
picture of a game *with no interface in it*, and nothing was logged. Both `Camera.Render()` and
`RenderPipeline.StandardRequest` were tried; neither draws the overlays in batch mode. The
topology was verified correct first — stack populated, overlay active and enabled, culling masks
right, canvases on the right layers, request reported as supported — so this is URP's behaviour,
not a wiring mistake. `CameraCapture` now walks the stack itself: render the world, render the
interface cameras on their own over nothing, composite on the pixels.

**A URP base camera cannot leave the colour it finds alone.** The obvious shortcut for that
composite — a second base camera with `CameraClearFlags.Depth`, which is exactly this in the
built-in pipeline — counts as "uninitialized" in URP. What it produced was a perfect HUD
floating on a sheet of flat blue. Hence the software composite.

**Missing-script sub-assets cannot be removed through the AssetDatabase.** The volume profile
carried seven leftovers from URP's own test suites; `LoadAllAssetsAtPath` returns them as
Unity-null, so `RemoveObjectFromAsset` cannot address them. The generator strips everything it
*can* address, and the orphans were removed once by hand. The asset went from ~800 lines to 165.
`TheVolumeProfileCarriesNothingElse` fails if any come back.

**The "compile error makes `-executeMethod` exit 1 and do nothing" trap bit again**, and this
time the wrapper reported success while the log said `return code 1`. Grep the log for
`error CS` before believing anything rendered — the PNG timestamp is the other tell.

**A test that asserted on a light it created was order-dependent, and the first fix treated the
symptom.** `LightingRig.Sun()` answered with the first directional light `FindObjectsByType`
happened to return, and an EditMode test that opened a generated scene earlier in the run could
still have its own default sun loaded alongside the one this test created.
`ApplyingAConditionPutsItOnTheScene` passed, then failed on a later run with
`Expected 1.5 But was 1.0` — the rig had configured the *other* scene's leftover sun and left
the locally created one at its default. The fix that shipped at the time weakened the test to
assert against whichever light the rig chose, rather than making the choice itself
deterministic — which left the real defect live: `Sun()` had genuinely no defined order, so a
production scene that ever ended up with two directional lights would light a coin flip.

**An adversarial review before commit caught that the fix had never actually landed — and the
first attempt at a real fix was itself wrong.** `Sun()` now sorts by instance ID explicitly
(`FindObjectsSortMode.InstanceID`, not the engine's own unspecified default), which at least
makes the same set of lights return the same answer on every call instead of varying run to
run. The first attempt went further and tried to also break the tie *correctly* when two
directional lights coexist, on the theory that instance ID tracks creation order and "newest"
is always the one meant for whatever is being lit now. Re-running the suite refuted that
outright: `ApplyingAConditionPutsItOnTheScene` failed with the rig picking Unity's own leftover
`Directional Light` over the one the test had just created a line earlier, which only happens
if instance ID does *not* reliably track wall-clock creation order across different allocation
paths (an engine-default object versus a `new GameObject()` from script). Sorted-but-arbitrary
is a real, checkable property; "sorted means newest" was an assumption that looked reasonable
and was not true.

The fix that shipped is more honest about what this method can and cannot know: it is
repeatable, not omniscient. Two directional lights genuinely coexisting in one scene is
ambiguity `Sun()` has no information to resolve — only production's own habit of loading a
fresh scene immediately before calling it avoids the question ever coming up for real. Scene
membership is still checked first, because "is this light even in the scene being lit"
*is* a fact `Sun()` can see and is worth acting on. The test was fixed differently: rather than
asking the rig to guess right under ambient pollution, `ApplyingAConditionPutsItOnTheScene` now
clears every directional light it finds before making its own, so there is only ever one
candidate and nothing to guess about. Anything else testing `RenderSettings` or doing a
scene-wide `FindObjectsByType` without an explicit sort mode still has the general exposure -
unspecified order is not "arbitrary but stable," it can and did vary run to run - and anything
that then tries to break a tie by assuming instance ID means recency should read this paragraph
first.

---

## Measured before/after

Sampled off the map overview, which is the shot `SurfaceTuning`'s documented values came from:

| surface | before | after |
|---|---|---|
| deep water | (33, 39, 50) | (31, 37, 48) |
| grass | (58, 80, 49) | (53, 76, 44) |
| sand | (218, 188, 127) | (187, 168, 116) |
| asphalt | (117, 117, 116) | (115, 115, 114) |

And on the gameplay still, the muzzle blast went from **(255, 255, 255) — clipped flat white —
to (211, 211, 211)**, which is the tone curve doing the job it was added for; it still reads as
hot because the bloom around it is now real.

**Consequence worth flagging:** the tone curve rolls off the top of the range, so the bright end
of the surface ramp measures lower than `SurfaceTuning`'s comments claim — sand is 168 where the
comment says 190. The ordering and the value *gaps* that the ramp is actually argued on all
survive, and asphalt and both waters barely move. A note to that effect is now in
`SurfaceTuning`'s remarks. Re-measuring the whole ramp belongs with a change to those colours,
not with this one.

---

## Known gaps

- **`Mobile_Renderer` has no renderer features**, so the Mobile quality tier has no SSAO while
  PC does. Not fixed here: adding a renderer feature means hand-editing `m_RendererFeatureMap`,
  which is a hash Unity maintains, and the project runs on the PC tier
  (`m_CurrentQuality: 1`). Both tiers *do* now agree on shadow distance and HDR.
- **`SkyGround` has not been measured against a coast.** It is a reasoned estimate — deep
  water's albedo is `(0.035, 0.075, 0.135)`, but the sea is a lit surface and comes out brighter
  than its albedo while the sky is painted straight on. Neither the sandbox still nor the map
  overview frames the sea's outer rim, so nothing here proves the seam is invisible. A still
  taken from a coast looking outward is the check.
- **Vignette strength (0.16) is the setting most worth a second opinion.** On the square map
  overview it darkens the extreme corner about 23% relative to the centre. It is invisible in
  the gameplay still and does what it is meant to. One constant in `PostTuning`.
- **`CameraCapture`'s manual pixel compositor has no test that renders anything.** Flagged by
  an adversarial review before this shipped: `LightingTests.cs` and `ViewStackTests.cs` both
  assert on component state (`renderType`, `renderPostProcessing`, culling masks) and neither
  constructs a camera or reads a pixel back. The private `Composite`/`Mix` methods that do the
  actual alpha-blend - the code this whole pass exists because URP will not draw a camera stack
  in a batch-mode one-shot render - could have their blend formula, their `Color.clear` in
  `RenderInterfaces`, or the interface pass itself silently break and every EditMode/PlayMode
  test would still pass; only a human looking at a rendered still would catch it. Not fixed
  here: a real pixel-level test needs `RenderToPng`'s private surface opened up for testing or
  an EditMode test that renders a camera and reads a `Texture2D` back, either of which is a
  test-infrastructure decision worth making on its own rather than as a side effect of a
  review finding.

---

## File map

**New — runtime** (`unity/Assets/RF/Scripts/Core/`)

| File | What it is |
|---|---|
| `LightingMood.cs` | The enum: `None`, `Daylight`, `Studio`. Shaped so a night-ops mood is a new row, not a rewrite. |
| `LightingTuning.cs` | The table: sun, ambient, fog, sky. `For(mood)` returns a fresh copy so callers can stamp and edit. |
| `LightingRig.cs` | Applies one row to the open scene. Also records `ShadowDistance`, which lives in the pipeline assets. |
| `PostTuning.cs` | Tone curve, bloom, grade, vignette, anti-aliasing. |
| `ViewStack.cs` | Turns post on for a world camera and hangs an ungraded interface camera off it. |

**New — editor**

| File | What it is |
|---|---|
| `ArtPipeline/SceneLighting.cs` | The one line every scene builder calls; knows a mood has a sky named after it. |
| `ArtPipeline/VolumeProfileBuilder.cs` | `Tools > IronFlag > Build Volume Profile`. Rewrites the default profile in place. |

**Renamed**

- `Scripts/UI/HudLayers.cs` → `Scripts/UI/InterfaceLayers.cs`. It no longer only means "HUD":
  interface is now separated from world by whether a tone curve may touch it, and the level
  editor's panels are here too, on Unity's built-in `UI` layer. `CullingMaskFor(slot)` became
  `WorldMask()` — every interface layer, not just the other seats', because a world camera that
  still drew its own player's HUD would draw it twice, once through the grade.

**Changed**

| File | Change |
|---|---|
| `ArtPipeline/CameraCapture.cs` | Walks the camera stack and composites the interface pass. See the gotcha above. |
| `ArtPipeline/GeneratedMaterials.cs` | Generates one sky material per mood. |
| `ArtPipeline/ArtPreviewScene.cs` | Its private lighting block became the `Studio` row; no post-processing. |
| `Gameplay/VehicleSandboxScene.cs` | `ConfigureLighting` is now one line; cameras get post and an interface camera. |
| `Gameplay/LevelEditorScene.cs` | Same, plus fog off and both canvases on the interface camera. |
| `Gameplay/LevelPreview.cs` | Post on the overhead camera, fog off. |
| `Scripts/Core/TopDownCameraRig.cs` | `Viewport` goes through `ViewStack.SetViewport` so the HUD camera resizes with the seat. |
| `Scripts/UI/PlayerHud.cs`, `Scripts/Editing/EditorUi.cs`, `Scripts/Editing/EditorOverlay.cs` | Canvases sit on an interface layer and repaint their subtree after each rebuild. |
| `Scripts/Levels/SurfaceTuning.cs` | Remark that the measured numbers predate the tone curve. |
| `Settings/DefaultVolumeProfile.asset` | Generated. ~800 lines → 165. |
| `Settings/PC_RPAsset.asset`, `Settings/Mobile_RPAsset.asset`, `ProjectSettings/QualitySettings.asset` | Shadow distance 50/40 → 120. |
| `IronFlag.Editor.asmdef`, `IronFlag.Tests.EditMode.asmdef` | URP references, for the volume profile types. |

**Tests** — `LightingTests.cs` (13) and `ViewStackTests.cs` (10) are new. Two existing wiring
tests were updated to assert the new invariant rather than the old one: a canvas now hangs off
the *interface* camera, and the world camera must not draw its layer.

## Verification

- **EditMode 382/382**, **PlayMode 137/137** (`lighting-editmode.xml`, `lighting-playmode.xml`).
  Baseline before this phase was 358 and 136.
- Stills, all regenerated from the same fixed cameras as the previous phase's:
  `lighting-sandbox.png`, `lighting-editor.png`, `lighting-map.png`, `lighting-artpreview.png`.
  Compare against `m9-sandbox.png`, `m9-editor.png`, `m9-map.png`.

Two of those tests are drift guards rather than behaviour tests, and they are the ones most worth
keeping: shadow distance and the volume profile both live in hand-editable assets that nothing in
code reads back, so the way they break is that somebody drags a slider in the inspector and saves.
