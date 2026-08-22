# M0 — Project setup: what exists and why

**To understand this, start by reading `README.md`, then `blender/README.md`, then
`blender/assets/vehicle_jeep.py`.** The jeep is the worked example that every other
asset copies; the two READMEs explain the layout and the pipeline rules around it.

This covers milestone **M0** from the design doc (Unity project, URP config, folder
structure, source control) plus the Blender asset pipeline the asset spec implies.
No gameplay code existed yet at this point — M1 onward has since landed; see
[M1_NOTES.md](M1_NOTES.md) through [M7_NOTES.md](M7_NOTES.md) for what was built, or
the milestone table in [README.md](README.md) for where things currently stand.

---

## What was set up

**Unity 6000.5.9f1 URP project at `unity/`**, seeded from the editor's own
`3d-cross-platform` (Universal 3D) template so the URP render-pipeline assets,
volume profiles and graphics settings are the real ones rather than hand-written
approximations. Package versions were then pinned to what this editor recommends
(URP 17.5.0, Input System 1.20.0, glTFast 6.14.1).

**Blender 5.2 asset pipeline at `blender/`** — a Python package that builds models
from primitives and exports one `.glb` per asset straight into
`unity/Assets/RF/Art/Models/`. All 25 assets in the asset spec are built and
committed; `blender/README.md` has the inventory.

**A bridge between them**: `Tools > IronFlag > Rebuild All Art from Blender` in the
Unity editor shells out to the same `build.py` the command-line wrappers use, then
re-imports. Right-clicking a `.glb` gives a single-asset rebuild.

**A way to look at the result**: `Tools > IronFlag > Build Art Preview Scene`
generates `Assets/RF/Scenes/ArtPreview.unity` containing every model in
`Assets/RF/Art/Models`, laid out in two rows - one per team color - on a ground plane
with a framed camera. It is generated, never hand-edited, so it picks up new assets
automatically. `ArtPreviewScene.RenderToFile` is the same thing driven by
`-executeMethod` with `-previewOutput <path>`, which renders a PNG without opening
the editor (run `-batchmode` but *not* `-nographics` - it needs a graphics device).

---

## Decisions worth knowing

**Content lives under `Assets/RF/`, code namespaces are `IronFlag.*`.** The folder
matches the `RF_` asset-name prefix from the spec; the namespace matches the repo.
Two names for the same project is mildly odd but both are load-bearing, so it is
deliberate rather than an oversight.

**Assets are code, not `.blend` files.** The asset spec's constraints — primitives
only, flat shading, vertex colors, no UVs, fixed palette, three destruction states
per destructible — are much cheaper to satisfy in script than by hand, and a
scripted asset can be diffed and reviewed. `.blend` files are gitignored on purpose:
a stray one is a working file, never a source of truth.

**Materials are a small fixed set of named groups, one child object each.** Nearly
all color is vertex color on `RF_Flat`, so a vehicle is one mesh carrying a dozen
palette colors. A separate material is only added where Unity must change something
a baked color cannot express: `team` (per-team recolor), `lightFront` / `lightRear`
(emission - a vertex color cannot glow), and `muzzle` (a known renderer to anchor
weapon VFX to). Each group is joined into its own child object - `TeamTrim`,
`LightsFront`, `LightsRear`, `Muzzle` - and a group belonging to a moving part hangs
off that part, so the tank's muzzle traverses with the turret. **This is the piece
M1/M4 needs to know about**: spawning a green vs brown vehicle sets one material on
one child renderer, and switching headlights on is the same move.

The first attempt kept the accent as a second material slot on the hull mesh. The
scene graph was provably right - two submeshes, two materials, the team material
bound to submesh 1 - and it still rendered wrong. A renderer whose submeshes mix
glTFast's Shader Graph material with a URP Lit material does not reliably draw the
second one. One object, one material is unambiguous, and it costs one extra draw
call per group on a 400-triangle model.

**Static geometry is joined, animated parts are not.** Wheels, turrets, launchers
and rotors stay separate objects with their origin on their axis of rotation and
are parented to the root; everything else merges into one mesh. The jeep therefore
imports as a root with four `Wheel_*` children already pivoted correctly.

**Exported `.glb` files are committed.** They are build output, but their `.meta`
files carry the GUIDs that prefabs and scenes will reference. Regenerating them per
clone would silently break every reference.

---

## Gotchas

**The workspace-root `.gitignore` is hostile to Unity.** `C:/git/projects/.gitignore`
is the GitHub Visual Studio template, which contains `*.meta` (meant for C++) and
`**/[Pp]ackages/*`. Left alone, those would drop every Unity `.meta` file and the
package manifest. `IronFlag/.gitignore` negates both at the top; the comment there
says so. **Do not remove those negations**, and be careful if similar rules get
added to the root file later.

**Axis and handedness, resolved once.** Blender is Z-up, Unity is Y-up and
left-handed, and the conversion is easy to get subtly wrong:

- Models are authored facing **-Y** in Blender. The exporter maps that to Unity
  **+Z**, i.e. forward. Never rotate an object to "fix" its orientation.
- Facing -Y with +Z up makes `right = forward × up = -X`, so a vehicle's **left**
  side is at **+X** in Blender. glTFast then mirrors X (verified in its source:
  `float3(-min[0], max[1], max[2])`), so +X really does land on Unity's left. The
  first pass of the jeep had its wheel labels mirrored because of exactly this.

**The `.gitkeep` files are load-bearing.** Git does not track empty directories, so
the folder structure would not survive a clone without them. Unity ignores dotfiles,
so they never get `.meta` files of their own.

**Two harmless console warnings on first open**: `IronFlag.Runtime.asmdef` and
`IronFlag.Tests.EditMode.asmdef` "will not be compiled, because [they have] no
scripts associated". That is accurate — they are empty scaffolding — and the
warnings disappear as soon as M1 adds the first script.

**Recoloring in Unity has three separate traps**, all found while getting the preview
to render, all now handled in `ArtPreviewScene.cs`:

- A URP Lit material built with `new Material(Shader.Find("Universal Render
  Pipeline/Lit"))` renders as though it had no base color. It misses the keyword and
  property setup the material inspector applies. **Copy a material Unity already
  made** — the one it puts on `GameObject.CreatePrimitive` — and recolor the copy.
- glTFast's imported materials ignore `baseColorFactor` assigned after import; a
  recolored copy renders white. Use a URP Lit material for flat team color instead.
  Note the property names differ: URP uses `_BaseColor`, glTFast uses
  `baseColorFactor`, and setting the wrong one is a silent no-op.
- The **SRP Batcher** groups by shader and feeds the batch one material's constant
  buffer. Materials created and rendered in the same frame all draw with whichever
  material the batch bound — every URP Lit object comes out one color, which looked
  exactly like "the ground is wearing the team color". Only the headless
  build-and-render-immediately path is affected; a scene opened normally in the
  editor is fine. `RenderToFile` disables the batcher for the duration and restores
  it. The flag lives on the URP asset — setting
  `GraphicsSettings.useScriptableRenderPipelineBatching` does not stick, because the
  pipeline reasserts its own value every frame.

**Blender clamps `material_index` when slots are cleared.** `prune_material_slots`
snapshots every polygon's material index before rebuilding the slot list; reading
them afterwards gives zeroes.

**Keep the project at a short path.** glTFast's materials are Shader Graph assets, and
the deepest generated path inside `Library/PackageCache/com.unity.shadergraph` runs to
about 170 characters from the project root. Move the project somewhere deep and the
Shader Graph importer trips Windows' 260-character `MAX_PATH` limit, fails to build
the shaders, and every imported model renders magenta. `C:/git/projects/IronFlag` has
plenty of headroom; a copy under a long temp path does not.

**First open generates files that must be committed**: every folder's `.meta`, and
`Packages/packages-lock.json`. Commit them in the same commit as this scaffolding if
possible, so the GUIDs are stable from the very first reference.

---

## File map

| Path | What it is |
|---|---|
| `README.md` | Orientation, layout, prerequisites, how to run things |
| `.gitignore` | Unity/Blender ignores **plus** the root-gitignore negations |
| `blender/build.py` | Headless build: discovers assets, name-checks, exports |
| `blender/build.ps1`, `build.sh` | Wrappers that locate Blender automatically |
| `blender/rf/palette.py` | The fixed palette, sRGB hex in, linear RGBA out |
| `blender/rf/primitives.py` | Box, wedge, pyramid, cylinder, cone + join/pivot/parent |
| `blender/rf/material.py` | `RF_Flat` and `RF_TeamAccent`, vertex-color painting |
| `blender/rf/scene.py` | Per-asset scene reset, collections, selection |
| `blender/rf/export.py` | The fixed glTF export settings |
| `blender/rf/naming.py` | `RF_<Category>_<Name>_<State>` validation |
| `blender/assets/vehicle_*.py` | Jeep, tank, ASV, helicopter |
| `blender/assets/structure_*.py` | Bunker, flag tower, fuel and ammo depots |
| `blender/assets/prop_*.py` | Bridge, two buildings, tree |
| `blender/README.md` | Pipeline rules, how to add an asset, asset backlog |
| `unity/Packages/manifest.json` | URP 17.5.0, Input System 1.20.0, glTFast 6.14.1 |
| `unity/ProjectSettings/` | URP settings from the official Universal 3D template |
| `unity/Assets/Settings/` | URP render pipeline assets and volume profiles |
| `unity/Assets/RF/Scenes/Sandbox.unity` | The template scene, renamed; still in build settings |
| `unity/Assets/RF/Input/InputSystem_Actions.inputactions` | Default project-wide actions. M2 renamed this to `IronFlagControls.inputactions` and replaced its `Player` map — see [M2_NOTES.md](M2_NOTES.md) |
| `unity/Assets/RF/Scripts/IronFlag.Runtime.asmdef` | Runtime assembly, namespace `IronFlag` |
| `unity/Assets/RF/Editor/IronFlag.Editor.asmdef` | Editor assembly |
| `unity/Assets/RF/Editor/ArtPipeline/BlenderArtPipeline.cs` | The rebuild-art menu items |
| `unity/Assets/RF/Editor/ArtPipeline/ArtPreviewScene.cs` | Generates and renders the art preview scene |
| `unity/Assets/RF/Tests/EditMode/…asmdef` | Edit-mode test assembly, empty at M0 — now the project's EditMode suite (see M1_NOTES.md onward) |
| `unity/Assets/RF/Tests/PlayMode/…asmdef` | Added later: the play-mode test assembly |

**One asset, one `.glb`.** There is no green copy and brown copy of anything. Each
model exports once; the hull mesh uses `RF_Flat` and each material group is a child
object with a single material, and Unity assigns a team material to the `TeamTrim`
renderer. Adding a third and fourth team is one more material asset each and no art
changes at all; restyling a team is editing one material. Selecting an imported
model's **root** in Unity shows a single material, which is correct - the second one
is on the child.

---

## Verified

- `blender/build.ps1`, `blender/build.sh` and the raw Blender command all build
  `RF_Vehicle_Jeep` (344 tris) and exit 0.
- The exported `.glb` has the intended structure: root node with four wheel
  children, wheel pivots on the axles, `COLOR_0` present, two materials, and a
  bounding box of 1.78 × 1.62 × 3.89 m against the spec's 1.8 × 1.6 × 4.0 m.
- The Unity project imports headless on 6000.5.9f1: packages resolve, glTFast lands
  in the package cache, scripts compile.

## Not done, as of M0

Everything from **M1** on. There is no gameplay code, no prefabs, no vehicles in a
scene — only the structure they will hang off. Only the jeep is built; the rest of
the asset spec's models are still to build.

*All of the above is now done — M1 through M7 have since landed (see the milestone
table in [README.md](README.md)), and every asset in the spec is built and committed
(see [blender/README.md](blender/README.md)'s asset inventory, not a to-build
checklist).*
