# UI Visual Identity

**To understand this, start by reading
[`HudPalette.cs`](unity/Assets/RF/Scripts/UI/HudPalette.cs) — it is still the one file that
decides what the interface looks like, and everything below is something it now hands out.
Then the three generated graphics it hands out,
[`HudPlate.cs`](unity/Assets/RF/Scripts/UI/HudPlate.cs),
[`HudBracket.cs`](unity/Assets/RF/Scripts/UI/HudBracket.cs) and
[`HudGlyph.cs`](unity/Assets/RF/Scripts/UI/HudGlyph.cs), and
[`HudMotion.cs`](unity/Assets/RF/Scripts/UI/HudMotion.cs), which is all of twenty lines of
arithmetic and is why nothing on this interface snaps any more.** Everything else is call
sites.

This is [MASTER_PLAN.md § 9](MASTER_PLAN.md#9-ui-visual-identity), all five items, with the
first one answered in the opposite direction from the way the plan wrote it.

| Plan item | What shipped |
|---|---|
| 1. TMP migration | **Not done, deliberately.** Stayed on legacy `Text`. See below |
| 2. Typeface | Saira Condensed SemiBold, with Saira Stencil One for three headlines |
| 3. Tactical-HUD visual language | Corner brackets, a vignetted scanlined plate, four drawn marks |
| 4. Motion/juice | Eased gauges, an alarm pulse, a fade-and-rise between menu screens |
| 5. Editor UI parity | Same face, same glass; scanlines deliberately off |

**Look at it**: `ui-hud.png`, `ui-menu.png` and `ui-editor.png`, or rebuild them with
`-executeMethod IronFlag.Editor.Gameplay.VehicleSandboxScene.RenderToFile -sandboxOutput <path>`,
`MainMenuScene.RenderToFile -menuOutput <path>` and
`LevelEditorScene.RenderToFile -editorOutput <path>`.

---

## The TextMeshPro decision, which went the other way

The plan's step 1 was "import TextMeshPro (Essentials)" and it is the one thing here that was
not built. The reason is worth writing down, because it is the third time this project has had
the same argument and the first time the answer came out differently from the plan.

TextMeshPro's runtime shaders are **not** in `com.unity.ugui`. The package ships them as
`Package Resources/TMP Essential Resources.unitypackage`, four megabytes of `.asset` and
`.shader` files that have to be imported into `Assets/`. That is exactly the thing this project
has refused twice already — a serialised node graph in
[GROUND_WATER_NOTES.md](GROUND_WATER_NOTES.md), a particle asset in
[VFX_NOTES.md](VFX_NOTES.md) — under the same sentence: *an asset nobody can review in a diff*.

Then the benefits were checked one at a time, and two of the three turned out not to need TMP
at all:

- *"an actual chosen typeface instead of an Arial fallback"* — legacy `Text` takes any
  `Font`. This is a `.ttf` in `Resources`, not a text stack.
- *"better small-text legibility"* — legacy dynamic fonts rasterise at the real device pixel
  size, scaled by the canvas scale factor. At the sizes this HUD uses they are sharp; SDF is
  what wins at sizes this HUD never reaches.
- *"real outlines"* — genuinely TMP's. Every string in this game is on a dark plate with a
  vignette holding the world back behind it, so there is nothing for an outline to do.

So: no import, no migration, and the whole of item 9 landed on the text stack the project
already had. **What it cost is letter-spacing.** Tracked-out condensed capitals are half of
what makes a panel read as an instrument, and legacy `Text` cannot do it: the fix is a
`BaseMeshEffect` that shifts each glyph quad, and `TextGenerator`'s vertex stream does not map
one-to-one onto characters — invisible characters emit degenerate quads, so the effect has to
guess which quad is which. A subtly wrong tracking on every label in the game is a worse trade
than no tracking, so there is none. That is the honest cost of this decision and the one thing
that would justify revisiting it.

## The typeface

**Saira Condensed SemiBold** for everything readable and **Saira Stencil One** for headlines.
Both OFL, both from the same superfamily, committed with their licences at
`unity/Assets/RF/Resources/Fonts/`.

Condensed because almost every string here is a word in capitals or a pair of numbers with a
slash between them, and a condensed face fits those in a column narrow enough to leave the game
visible behind it. SemiBold because this is white type on dark glass over a sunlit map, where a
regular weight goes thin and grey — the secondary hierarchy is carried by `FadedInk` rather than
by a second weight, which is one asset fewer. The numerals decided it against the alternatives:
three of the four gauges are a number over another number, and Saira's figures are lining, even
in width, and have a slashed zero.

The stencil is used in exactly **three** places — VICTORY/DEFEAT, the game's name on the main
menu, and PAUSED — and `HudPalette` enforces that socially rather than technically: it is a
separate `Headline()` call rather than an argument to `Label()`, so the three call sites are
three things somebody can find with a search.

Two things about where they live:

- `unity/Assets/RF/Resources/` is the **first `Resources` folder in the project**, and it exists
  because the two classes that need a font — `HudPalette` and `EditorTheme` — are static, and a
  static class cannot be handed an asset by a scene. It holds nothing but the two fonts and the
  two licences, which ship with the game because the OFL says they must.
- `HudPalette.Load` **falls back to the built-in face rather than failing** when the asset is
  missing. That is on purpose (the wrong typeface can be diagnosed; empty boxes cannot) and it
  is why two tests exist to notice it happening — see Tests below.

## Decisions worth knowing

**Corner marks, not borders.** A rectangle drawn all the way round a panel is a window frame:
it says *this is a different surface from the one behind it*, which is what an operating system
wants to say and the opposite of what this game does. Four corner marks say *this region is
being watched*, which is what every piece of military glass in fifty years has said, and it
costs eight quads. The marks are also where a player's own colour lives — the plate underneath
is the same dark glass on both halves of a split screen, so the one thing that differs between
the two halves is a thin line at the corners rather than the whole tint of the picture.

**The plate wears the world's vignette.** `HudPlate.EdgeDarken` and `HudPlate.EdgeReach` are
read out of `PostTuning.VignetteIntensity` and `VignetteSmoothness` rather than written down
again. The interface is drawn by a camera with the grade switched off — see `InterfaceLayers`
for why it has to be — so the *only* way it can share a filter with the world is to be built
out of the same numbers. A test asserts they have not drifted.

**The objective panel has no corner marks.** They carry a side's accent, and that panel is the
only one on the HUD that is about both sides at once.

**Scanlines are on for the player and off for the editor.** `EditorTheme.Panel` is the same
glass with `Scanlines` off. A player glances at a HUD; a mapper reads a list of map names off
the editor's panels, and texture under a word somebody is reading is texture in the way. It is
the one place the borrowed look deliberately parts company from the thing it borrowed.

**The marks are polygons, not pictures.** Four shapes, each a list of points in a unit square,
in `HudGlyph.cs` where a reviewer can see that somebody moved the tip of the drop rather than
that a PNG changed. Every one is convex, which is not decoration — it is what lets each be
drawn as a triangle fan from its first point with no triangulation step and no library. The
flag is stored as two convex pieces, a staff and a pennant, for the same reason. **A test
checks convexity**, because an outline that stopped being convex draws a fan of triangles
across itself and reads as a rendering bug rather than as bad data.

**The gauges run on scaled time and the menus on unscaled.** The pause panel sets
`Time.timeScale` to zero and the HUD carries on being drawn underneath it — `LateUpdate` does
not stop — so a gauge easing on unscaled time would keep sliding and strobing behind a panel
that exists to stop the match. A paused gauge is a still picture of the moment the player
paused, which is what they paused to look at. The main menu is the opposite case and says so
where it uses it.

**A gauge jumps when the vehicle under it changes.** Everything on the driving strip is about
the thing being driven, so the one frame where that changes is the one frame where sliding from
the old reading to the new one would be drawing a tank's armour on a jeep's gauge. `PlayerHud`
gets this from `Watch` returning whether the vehicle swapped, and the jump happens *after* the
new readings have been set rather than before — that ordering is the whole of it, and getting
it backwards produces exactly the artefact the jump exists to prevent.

**A pool and a countdown are different bars.** `HudBar.Show` is a quantity the vehicle has: it
eases, it goes red on its own via `HudPalette.Level`, and it breathes while it stays there.
`HudBar.ShowProgress` is a button being held down and does none of those — a progress bar that
eased would lag the finger pressing it, and one that pulsed at the start would be shouting
about a bar that has only just begun to fill. The fourth bar is the only caller of the second.

**The alarm colour is read off the target, not off the length being drawn.** A gauge easing
down past a fifth would otherwise turn red partway through the slide, which reads as the bar
changing its mind rather than as the tank running dry.

**The fourth bar has no mark.** It means two different things — scuttling a vehicle in the
field, stowing one at home — and renames itself between them. A mark that had to change meaning
with the word beside it would be a mark that means nothing on its own, which is the only thing a
mark is for.

## Gotchas

**A custom `Graphic` needs `[RequireComponent(typeof(CanvasRenderer))]` of its own, and
without it draws nothing, silently.** This cost more than everything else here put together
and it is worth reading the shape of it, because the symptom points nowhere.

`Graphic` is declared `[RequireComponent(typeof(RectTransform))]` — **not** the renderer. It is
`Image` that asks for `CanvasRenderer`, in its own attribute. So a subclass of
`MaskableGraphic` that copies `Image`'s usage but not its attributes gets a `RectTransform`, a
canvas, an `OnEnable`, a registration in `GraphicRegistry`, a `SetVerticesDirty` on every
property change and a correct rect — and no renderer. `Graphic.Rebuild` opens with

```csharp
if (canvasRenderer == null || canvasRenderer.cull)
    return;
```

so it returns on the first line, `OnPopulateMesh` is never called, and **nothing is logged at
any level**. Every plate, bracket and glyph was invisible in every still while `Image` and
`Text` beside them drew normally, and the whole test suite passed, because nothing in the
object graph is wrong. Two wrong hypotheses were chased before this one — `[ExecuteAlways]`
(the attribute *is* inherited from `Graphic`; it was never the problem) and construction order
(irrelevant) — and both were only ruled out by putting `Debug.Log` inside `OnPopulateMesh`,
finding it never ran, and then walking `SetVerticesDirty` → `Rebuild` until one of them
stopped being called. `HudLookTests.EveryDrawnPieceHasTheRendererItsMeshIsDrawnBy` now asserts
the renderer directly, which is the cheapest possible guard against a fourth piece being added
the same way.

**The stills need a graphics device: `-batchmode` yes, `-nographics` no.** Passing it produced
one blank grey PNG and one native crash inside URP's render loop. This is already written down
in `CameraCapture`'s own class remarks — the lesson is to read the entry point before driving
it from a new script, not to add another note about it.

**The stills are rendered in edit mode, where `Update` never fires.** The main menu's screens
now fade and rise into place, and a screen that started at alpha zero would *stay* at alpha zero
in `menu-root.png` — the picture of the menu would be a picture of an empty column. `Show()`
therefore sets the transition to finished when `Application.isPlaying` is false. Anything else
added to this interface that animates on entry has to do the same thing, and the failure is
silent: a still with nothing in it looks like a build problem, not a timing one.

**Sliding a stretched `RectTransform` needs its resting position remembered.** The menu's three
screens are anchored to fill their column with offsets, so their `localPosition` is derived from
those offsets and is *not* zero. Setting it to zero to slide from would move the panel by fifty
units and leave it there. `MainMenuController` captures `panelRest` once at build time and slides
relative to it.

**A bracket's two arms must not overlap.** Two quads of a half-transparent colour laid on top of
each other blend twice, and the result is a bright square at every corner of every panel — which
looks like a deliberate stud until somebody changes the alpha and it does not. The upright arm
stops where the flat one starts.

**`Is.AnyOf` does not exist in this NUnit.** Cost one compile round-trip; use
`Is.EqualTo(a).Or.EqualTo(b)`.

**Legacy `Text` is not obsolete in ugui 2.5.** Worth checking rather than assuming, since the
whole TMP decision above rests on it.

## File map

New, all in `unity/Assets/RF/Scripts/UI/`:

| File | What it is |
|---|---|
| `HudMotion.cs` | The easing, the pulse and the flash. Pure static arithmetic, no components |
| `HudPlate.cs` | The glass a panel is made of: nine quads of vignette plus scanlines |
| `HudBracket.cs` | Four corner marks, eight quads, team-accented |
| `HudGlyph.cs` | The four drawn marks, as convex outlines fanned from their first point |
| `HudGlyphKind.cs` | Which mark |

New elsewhere:

| Path | What it is |
|---|---|
| `unity/Assets/RF/Resources/Fonts/` | The two `.ttf`s and their two OFL licences |
| `unity/Assets/RF/Tests/EditMode/HudLookTests.cs` | The look's own suite |

Changed:

| File | What changed |
|---|---|
| `UI/HudPalette.cs` | Two typefaces with a fallback; `Plate`/`Bracket`/`Glyph`/`Headline`; `AlarmFraction`/`IsAlarming` |
| `UI/HudBar.cs` | A mark per row; `Show` vs `ShowProgress`; `Advance`/`Jump`; the pulse |
| `UI/PlayerHud.cs` | Four plates, three brackets, five marks, a stencil result, the gauge easing |
| `Editing/EditorTheme.cs` | `Panel` (glass, no scanlines) and `Bracket`; the stale TMP remark corrected |
| `Editing/EditorUi.cs` | Its six outer panels are glass |
| `Menu/MainMenuController.cs` | Stencil title; a `CanvasGroup` per screen; the fade-and-rise |
| `Menu/PauseMenu.cs` | Glass, a bracket, a stencil title |
| `Tests/EditMode/SandboxWiringTests.cs` | A HUD is built from the project's pieces and set in its face |

## Tests

`HudLookTests` (edit mode, no canvas needed — everything in it is either a pure function or a
widget that does not need one):

- Both faces load and are **not** the fallback, and are not the same object as each other.
- Every generated piece has the `CanvasRenderer` its mesh is drawn by — see Gotchas.
- The plate's falloff still equals `PostTuning`'s vignette.
- Every mark has a shape, every shape is inside its unit square, and every shape is convex.
- Easing: nothing moves on a zero-length frame; it never overshoots in either direction; it
  always arrives; and two half-steps land where one whole step lands, which is the property
  that makes the motion a rule rather than an accident of hardware.
- The pulse stays in nought-to-one and repeats; the flash changes brightness and never alpha.
- A real `HudBar` eases to its target and arrives, a progress bar lands immediately, and a
  jump stops a slide dead.

Both suites green after all of it: **532 edit-mode, 189 play-mode, nothing skipped.**

`SandboxWiringTests.EveryHudIsBuiltFromTheProjectsOwnPiecesAndSetInItsOwnFace` walks a built
HUD and checks the four plates, the three panel brackets plus one per roster cell, and five
marks are there — and that **every `Text` on the canvas** is in one of the two faces. That last one is the point: the fallback
means a broken import, a renamed file, or a label built by hand instead of through the palette
all have the same silent symptom, which is the game quietly going back to being set in Arial.

## What this does not do

- **No letter-spacing.** The one thing TMP would genuinely have bought. See above.
- **No icons in the level editor.** Parity was typography and glass; its tool palette is still
  words, which is right for a tool where the words are exact.
- **No transition on the pause panel.** It appears and disappears. A panel that faded in over a
  frozen match would be a panel the player had to wait for before they could leave.
- **No HUD-wide scanline or vignette overlay.** Both live on the panels. A full-screen version
  would sit in front of the world and double the vignette the grade has already applied.
