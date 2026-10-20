# Mirrored Pairs — what shipped

**To understand this, start by reading the `Pair` field on
[`LevelStructure.cs`](unity/Assets/RF/Scripts/Levels/LevelStructure.cs) — one `int` and the
whole argument for it — then `TwinOf`, `PairUp`, `Unpair` and `PairTheHalves` in
[`LevelEdits.cs`](unity/Assets/RF/Scripts/Editing/LevelEdits.cs), and then the four
`X`/`XOne` pairs in the same file (`MoveTo`/`MoveOne`, `SetYaw`/`SetOneYaw`,
`SetLandEdges`/`SetOneLandEdges`, `Remove`/`RemoveOne`), which is where the feature actually
happens.** The editor as a whole is [M8_EDITOR_NOTES.md](M8_EDITOR_NOTES.md), the generator is
[GENERATOR_NOTES.md](GENERATOR_NOTES.md), and this changes one paragraph of each.

This was **not in [MASTER_PLAN.md](MASTER_PLAN.md)**. It was asked for directly, as a bug and
its fix in one sentence: that mirroring something, moving it and pressing mirror again left
the first copy behind and made a second — and that things should be paired *at the generator
stage* so they move mirrored by definition, unless that is turned off for one particular item
or piece of land.

---

## How to see it

`pairing.png` is the editor with the shipped map open and the green flag tower selected. The
inspector's action button says **Unpair from its twin** rather than *Mirror to the other
side*, and there are two boxes on the map: the bright one round the tower being held, and a
dimmer one of the same weight round the brown tower at the opposite corner — the thing that is
going to move with it.

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.LevelEditorScene.RenderToFile -editorOutput ../pairing.png -logFile -
```

Then open `unity/Assets/RF/Scenes/LevelEditor.unity`, press Play, and drag any tree on the
map. The tree at the opposite corner goes with it.

---

## The feature in one paragraph

Two things of the same kind, on opposite sides, carrying the same **pair number** are each
other's opposite number, and the editor moves, turns, resizes and deletes them together. The
number is a plain `int` on the four placed types, zero for something that stands on its own,
and it is written by the generator when it turns a half-map into a whole one, by the mirror
button when a person does the same thing by hand, and by a *Pair up the halves* pass over a
map that was drawn before any of this existed. It is an editing annotation and nothing else:
no rule of play reads it, and a level whose pairs are all wrong is exactly as playable as one
with none.

| | |
|---|---|
| Where it lives | `LevelStructure.Pair`, and the same field on `LevelLand`, `LevelTower`, `LevelBunker` |
| What follows | position, heading, a rectangle's four edges, deletion, side, the supply numbers, which tower holds the flag |
| What does not | names |
| Turning it off | **Unpair from its twin** in the inspector, per item; both ends let go |
| Making one | **Mirror to the other side**, which now refuses when there already is one |
| Repairing one | **Pair up the halves** with nothing selected, and `Tools ▸ IronFlag ▸ Pair Up Shipped Maps` for the shipped folder |
| Generated maps | mirrored maps come out paired; asymmetrical ones come out with none |
| Shipped maps | `iron-channel` is 54 pairs — every rectangle and every prop; `iron-watch` is 8, its ground only |
| Schema | unchanged at 4, deliberately |

---

## The decisions

### 1. A number, not an index

The obvious shape is a pointer: each thing remembers *which slot* its twin is in. It is wrong
here for one reason that settles it — **indices shift the moment anything is deleted.** A pair
held as "the thing at slot 14" has to be rewritten across four arrays on every removal, and
the first time somebody forgets, a prop is quietly married to whatever slid into that slot. A
level file edited by hand would be one inserted line away from pairing a tree with a bridge.

A number that means nothing except *the same as that one* survives all of it. `TwinOf` scans
for it, which costs a walk of one array per lookup and buys a format that cannot be corrupted
by an edit somewhere else in the file. `APairSurvivesTheThingsInFrontOfItBeingDeleted` is that
claim as a test.

It also gives the degenerate case a free answer. A number that ends up on **one** thing — a
hand-edited file, or half a pair dropped by a build that never heard of them — reads as
unpaired: `TwinOf` finds nothing, the thing behaves normally, and it can be mirrored afresh.
Nothing needs repairing and nothing refuses to load.

### 2. The mirroring lives in `LevelEdits`, not in the editor

Every public mutator that moves something now has a private one-thing core behind it:
`MoveTo` wraps `MoveOne`, `SetYaw` wraps `SetOneYaw`, `SetLandEdges` wraps `SetOneLandEdges`,
`Remove` wraps `RemoveOne`. The public one does the thing, asks `TwinOf`, and does the turned
version to the twin.

The alternative — a twin-aware `MoveBoth` alongside the plain `MoveTo`, with the editor
choosing — is the same trap as two fields that have to agree. **Every call site would have to
remember**, and there are five ways to move something in this editor already: the drag, the
two typed coordinates, the arrow the palette places with, and the bunker tool. One of them
would be missed, and the symptom is a map that mostly stays symmetrical.

Doing it in the primitive means the drag mirrors because `LevelEditorSession` calls `MoveTo`,
and the inspector's *East (x)* field mirrors for the same reason, and neither file knows
pairing exists. `DraggingSomethingPairedDragsItsOppositeNumber` is a PlayMode test through a
virtual mouse precisely because that is the claim: not that the arithmetic is right, but that
the gesture goes through the function that does it.

### 3. Placement is mirrored; identity is not

The line is: **what follows is where a thing is, how big, which way round, and whether it
exists.** A name does not follow, because the two halves of a pair are *supposed* to be called
different things — the generator names each one for its own side, and a rename that reached
across the map would undo that every time.

Two things sit awkwardly on that line and both go with the twin anyway:

- **The side.** The twin of a green thing is a brown one, by definition of this game's mirror,
  so `SetSide` gives the twin `Opposite`. It is not "copy the side across", which would leave
  one player holding both emplacements of a pair on a map that still looked symmetrical — the
  invisible kind of asymmetry `Mirror` has warned about since M8.
- **The supply numbers.** A depot's rates and radius, and a bunker's. Two twins whose fuel
  rates had drifted apart is the same invisible asymmetry with the whole match riding on it:
  the map looks symmetrical, plays lopsided, and nothing in the file says which side is being
  favoured. Those go through `BothProps` / `BothBunkers` in the inspector.

Which tower holds the flag is the third, and it is the sharpest. Promoting a paired tower
promotes its twin, so the two flags stay on towers that are each other's opposite numbers —
which is the pairing the shipped map has and the thing that makes both runs the same length.
This one **inverted an existing test**; see the gotchas.

### 4. Deleting one deletes both

A pair is one thing placed twice, so `Del` takes both and the status line says so. Deleting
only the end that was clicked leaves a map that was symmetrical a moment ago with one
unanswered prop on it — nothing complains, and nobody finds out until somebody plays the side
that still has the cover. Undo puts both back, and anybody who wants only one end gone unpairs
it first, which is one click and is what the off switch is for.

The trap inside it is arithmetic: **the higher index has to go first.** Removing the lower one
shifts everything after it down a slot, and the second removal then takes the wrong thing off
the map. On a list of forty trees that is a bug that looks exactly like nothing.

### 5. Mirror refuses when there already is a twin

This is the reported bug, fixed at the source rather than papered over. `Mirror` returns
nothing for something that already has an opposite number, and the inspector shows *Unpair
from its twin* in that slot instead — so the gesture that made the duplicate no longer exists.
There is nothing to press twice, because the original never goes out of step in the first
place.

### 6. `PairTheHalves` is conservative, and it is a button

Two things pair only if they are the same kind, on opposite sides, and within 25 cm of each
other's turned position. That tolerance is deliberately tight: it covers a coordinate somebody
rounded while typing and is well inside the gap between two props anybody would want to tell
apart. **A pass that guesses wrong welds two unrelated things together for the rest of the
map's life**, and from then on moving one drags something across the map — which is much worse
than missing a pair, because missing a pair is what the map already does today.

Same-kind is the rule everywhere, including the explicit `PairUp`. It is why a new map's two
depots come out *unpaired*: they stand at opposite points, but one is a fuel dump and the
other an ammunition dump, and a pair is the same thing placed twice.

It is a button rather than something the editor does on open, for a reason that only shows up
the second time: running it puts back any pair somebody deliberately broke. An editor that
re-paired on every load would make the off switch last until the next time the file was
opened.

### 7. A one-player map pairs its ground and nothing standing on it

A solo map's *land* is mirrored like anybody else's — `Ground` runs the same turned pass — but
its props are scattered over the whole map from one set of dice and were never each other's.
So `Draw` calls `PairTheHalves(level, EditTarget.Land)` for solo and the full four-list version
otherwise. Pairing solo props would weld together whatever two trees happened to land
opposite, which is an accident wearing a relationship.

### 8. No schema bump

By the format's own stated bar: a build that has never heard of `Pair` ignores the field and
builds **exactly the same battlefield**, because nothing about play reads it. The worst an old
build can do is lose the pairs on save, leaving a map that is still the map and can be paired
up again in one press. That is the same reasoning that kept surfaces at version 1 and is
written into `LevelDefinition.Schema`'s remarks next to the four bumps that were earned.

---

## Gotchas found while building this

### A saved map in the player's folder shadows the shipped one — including in the still

The first render of `pairing.png` came back with the button still reading *Mirror to the other
side*, on a map whose file was 100 % paired. Nothing was wrong with the code: `LevelLibrary`
prefers `persistentDataPath/Levels` over `StreamingAssets`, this machine had an
`iron-channel.json` saved there from August, and the still had faithfully photographed a
five-day-old copy of the map.

That is the documented shadowing rule working, and **it is also every player's migration
story**: anybody with a saved map from before this change opens it unpaired, and *Pair up the
halves* is the one press that fixes it. The still was re-rendered with the user copy moved
aside and put back afterwards. Worth remembering for any future still: **a shipped-asset
change is not visible in a render until the shadow is checked.**

### `AddBunker` was a second way to move something

`MoveTo` is not the only thing that writes a position. A side has exactly one bunker, so
`AddBunker` *moves* the existing one rather than adding a second — and it did that by
assigning `Position` directly. Which made the bunker tool the one gesture on the map that
quietly broke a pair, and on a two-bunker map that is the whole symmetry gone in one click.

It now goes through `MoveTo`. **The lesson generalises: grep for `.Position =`,
`.YawDegrees =` and the rest outside the file that owns them before believing a rule like this
is enforced.** That grep is what found it, and it now comes back with nothing but the two
twin-aware inspector helpers.

### One existing test was asserting the thing this feature deliberately changes

`MakingATowerRealDemotesTheOthersOnItsSideAndLeavesTheOtherSideAlone` failed, with the message
*"promoting a green tower moved the brown flag"* — which is now exactly what it is supposed to
do, on a paired tower.

The right move was not to weaken the test but to notice it had **two** claims in it. The one
worth keeping is that the demotion is scoped to the promoted tower's own side rather than
sweeping the map; that is still true and still needs a test, and it now drops the pairs first
so four towers answer for themselves. The other claim is now false on purpose and has its own
test saying so. **A failing test after a behaviour change is a question about which of its
sentences was the point.**

### Re-serialising a hand-authored level file loses its round numbers

Writing `iron-channel.json` back through `LevelFile.TryWrite` turned `"WaterDepth": 0.7` into
`0.699999988079071`, and did the same to five depot rates and the two bridges' sink. All of
them are the nearest `float` and none of them changes anything — but a hand-authored file full
of `0.11999999731779099` is a file nobody can read, and every one of those numbers was typed
by a person for a reason.

They were put back by hand, and a JSON-level diff was run to prove that **`Pair` (and 16
explicit `"Shape": "Rectangle"` defaults) were the only changes in either file.** Any future
tool that rewrites a shipped map wants the same check: parse both, strip the field you added,
compare.

### PlayMode has timing-sensitive tests that flake under repeated batch runs

Two runs in the middle of this failed on one test each — `AGunWithNoSupplyBehindItNeverRunsOut`
and `AJeepTakesTheEnemyFlagAndCarriesItOnItsMast` — a *different* one each time, each passing
in the run the other failed, and neither anywhere near a level file. They are rigs that wait a
fixed number of seconds for a frame-paced thing to happen, and batch mode's frame pacing moves
with machine load. A third run came back 200/200. **Two different failures in two runs is
flakiness, not a regression — but it is worth one more run to say so rather than assuming it.**

---

## File map

**New**

| File | What it is |
|---|---|
| `unity/Assets/RF/Editor/Gameplay/ShippedLevelPairing.cs` | `Tools ▸ IronFlag ▸ Pair Up Shipped Maps` — the batch twin of the editor's button, and how both shipped maps got their pairs |

**Changed — the format**

| File | What changed |
|---|---|
| `Levels/LevelStructure.cs` | `Pair`, and the remarks every other copy points at |
| `Levels/LevelLand.cs`, `LevelTower.cs`, `LevelBunker.cs` | `Pair` |
| `Levels/LevelDefinition.cs` | Why this did not earn a schema bump |
| `StreamingAssets/Levels/iron-channel.json` | 54 pairs: every rectangle, every prop, both tower pairs, both bunkers |
| `StreamingAssets/Levels/iron-watch.json` | 8 pairs — its ground; a solo map's props are not twins |

**Changed — the mechanism**

| File | What changed |
|---|---|
| `Editing/LevelEdits.cs` | `PairTolerance`, `PairOf`, `TwinOf`, `NextPair`, `PairUp`, `Unpair`, `PairTheHalves` (both overloads), `SetSide`, and the private `PairIdAt`/`SetPairId`/`StandOpposite`/`SetOneSide`/`Near`. `MoveTo`, `SetYaw`, `SetLandEdges`, `Remove` and `MakeRealTower` split into a propagating public half and a one-thing private half. `Mirror` refuses a paired thing and pairs what it makes, with `Copied` holding the old body. `AddBunker` repositions through `MoveTo`. `Starter` pairs its bunkers and towers |
| `Editing/LevelGenerator.cs` | `Draw` pairs a mirrored map's halves once everything is drawn — land always, the rest only when it is not solo |

**Changed — the editor**

| File | What changed |
|---|---|
| `Editing/EditorInspector.cs` | *Unpair from its twin* in place of *Mirror* when there is one; *Pair up the halves* on the map panel; the three side pickers go through `LevelEdits.SetSide`; the depot and bunker supply numbers go through the new `BothProps` / `BothBunkers` |
| `Editing/EditorOverlay.cs` | A real opposite number is drawn round the thing itself at selection weight; the old hairline ruler is kept for everything that has none |
| `Editing/EditorTheme.cs` | `Paired` — the selection colour cooled and dimmed |
| `Editing/LevelEditorSession.cs` | `DeleteSelected` says when a pair went |
| `README.md`, `M8_EDITOR_NOTES.md`, `GENERATOR_NOTES.md` | The behaviour, and a pointer here |

---

## Tests

769 pass: 569 EditMode and 200 PlayMode. Twenty-four are new.

**The mechanism** (`Tests/EditMode/LevelEditingTests.cs`)

- `MovingSomethingPairedMovesItsOppositeNumber` — the whole feature in one test.
- `MirroringSomethingThatAlreadyHasATwinMakesNothing` — *the reported bug*: mirror, move,
  mirror again, and there are still two props rather than three.
- `TurningSomethingPairedTurnsItsTwinTheOtherWayRound`, `ResizingPairedLandResizesItsOppositeNumber`.
- `DeletingSomethingPairedTakesItsOppositeNumberToo` — with a prop either side of the pair in
  the array, so the higher-index-first rule is what makes it pass.
- `UnpairingLetsBothEndsMoveOnTheirOwn`, `SomethingUnpairedCanBeMirroredAgain` — the off
  switch, and that both ends really let go.
- `APairSurvivesTheThingsInFrontOfItBeingDeleted` — the number-not-an-index claim.
- `ChangingAPairedThingsSideGivesItsTwinTheOtherOne`,
  `PuttingTheFlagOnAPairedTowerPutsTheOtherFlagOnItsTwin`,
  `PlacingASecondBunkerForAPairedSideMovesBothOfThem`.
- `PairingTheHalvesFindsEveryTwinOnASymmetricalMap` — and that running it twice finds nothing,
  and that it moved nothing.
- `PairingTheHalvesLeavesAnUnsymmetricalMapAlone`, `…NeverMarriesTwoDifferentThings`,
  `…NeverMarriesTwoThingsBelongingToOneSide` — the three ways the conservative matcher says no.
- `ANewMapIsBuiltInPairsAndSaysSo`, `PairsSurviveBeingWrittenOutAndReadBack`,
  `HalfAPairIsSimplyNotAPair`.

**The generator** (`Tests/EditMode/LevelGeneratorTests.cs`)

- `AMirroredMapComesOutAlreadyPaired` — three layouts × twelve seeds, and the claim is that
  running the pass again finds **nothing**, so a generator change that stopped putting a
  turned copy where its twin expects it fails here rather than showing up as a map somebody
  cannot keep symmetrical.
- `MovingAPropOnAGeneratedMapMovesItsOppositeNumber`, `AnAsymmetricalMapComesOutWithNoPairsAtAll`,
  `AOnePlayerMapPairsItsGroundAndNothingStandingOnIt`.

**The shipped map** (`Tests/EditMode/LevelDesignTests.cs`)

- `TheShippedMapKnowsWhichOfItsPropsAreEachOthers` — every rectangle and every prop is in a
  pair, and the pass finds nothing left. This is what stops `Pair Up Shipped Maps` being a
  migration somebody has to remember to re-run after hand-editing the map.

**The gesture** (`Tests/PlayMode/LevelEditorInputTests.cs`)

- `DraggingSomethingPairedDragsItsOppositeNumber` — a real press and drag through a virtual
  mouse. The arithmetic was never the doubtful part; what this proves is that the drag goes
  through `MoveTo` rather than reaching past it, which is a thing every arithmetic test in the
  project would miss.

---

## What this does not do

- **Placing is not mirrored.** Putting a tree down puts down one tree; mirroring it afterwards
  is the second click and the pair comes from that. A "mirror everything I place" mode is a
  different feature — a state on the editor rather than a fact about the map — and it would
  need its own answer for what happens when the mirrored copy lands in the sea.
- **Names are not synchronised**, on purpose. See decision 3.
- **A pair is two things, never three.** `PairUp` unpairs both ends first, so linking
  something that was already half of a pair leaves that pair's other end on its own rather
  than in a three-way nothing can answer.
- **Nothing validates pairs.** `LevelValidation` is about whether a map can be *finished*, and
  a map with wrong pairs plays identically to one with right ones. A pair that has gone odd
  shows up as *Pair up the halves* finding something, not as a red line.
- **The land surface and shape still are not editable at all**, so there is nothing there for
  a pair to carry. If a surface picker is ever added to the inspector it wants `BothLand`
  alongside the two helpers that exist.
- **The editor does not offer "pair these two selected things".** There is one selection at a
  time, and everything that wants a pair either mirrors or is found by the halves pass.
