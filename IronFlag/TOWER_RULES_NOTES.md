# Tower rules: the flag is behind a wall now

**To understand this, start by reading `unity/Assets/RF/Scripts/Objective/FlagTower.cs`, then
`Objective/Flag.cs` (`IsVisible` and `Rest`), then the `FlagTower` case in
`Destruction/StructureTuning.cs`.** The first states the three rules and why they replace what
was there. The second is where "you can take it exactly when you can see it" is one condition
rather than two. The third is the price.

This is not a milestone from the design doc. It is a rules change to M6's flag, made after M7,
and it supersedes part of [M6_NOTES.md](M6_NOTES.md) — see *What M6 said that is no longer
true* at the bottom.

---

## How to see it

Open `unity/Assets/RF/Scenes/Sandbox.unity` and press Play. Your HUD says
`THEIR FLAG SEALED - BREAK A TOWER`. Take the **jeep** to the enemy's pyramids first, if you
like: drive into both, park on them, wait — nothing happens, and nothing tells you which is
which. That is rule 1 and rule 3 doing their jobs.

Now take the **tank**, stand off at its full 36 m, and put five shells into one of them. The
top comes off, the four corners are left standing, and you are looking down into the tower -
at a flag, or at an empty floor. Either way you have the answer and it cost you a quarter of
your load. Go home, swap to the jeep, drive into the tower, and take what you found.

`m7-sandbox.png` is that moment: the raider's half shows the tower they broke open with the
jeep leaving past it, and the defender's half shows an intact pyramid and a strip that says
their flag has been taken.

---

## The rules

1. **An intact flag tower looks the same whether it holds the flag or not.** There is no
   distance you can drive to and no angle you can look from that answers the question.
2. **A broken or destroyed tower shows what it was holding.** A cracked tower with a flag on
   it is the real one; a cracked tower with nothing on it is the decoy, and you have just
   spent the shells to learn that.
3. **A jeep may only take a flag off a tower that has been broken open.** Driving a jeep to a
   pristine tower achieves nothing at all.

Together they turn the design document's first pillar — *"only the jeep carries the flag;
everything else exists to clear its path"* — from a slogan into a mechanic. There is now
literally a wall in the jeep's path, and the jeep is the worst thing in the game at removing
it. The normal shape of a raid becomes **tank sortie, then jeep**, which is the
scout → clear → dash rhythm the vision paragraph asks for.

---

## What it costs

A tower is 340 hit points and cracks open at half, which makes it the toughest thing on the
map — a shade above the bridge at 320. Read against the real weapon table:

| Vehicle | To crack one open | To flatten one | Both of a side's towers |
|---|---|---|---|
| Tank (34 × 20 shells, 36 m) | 5 shells | 10 | 10 of 20 — half a load |
| ASV (55 × 12 rockets) | 4 rockets | 7 | 8 of 12 |
| Jeep (22 × 24 grenades, 14 m) | 8 grenades | 16 | 16 of 24 |
| Helicopter (4 × 240 rounds) | 43 rounds | 85 | 86 of 240 |

The numbers that matter are the two in the tank's row. **One sortie always finds the flag**:
even guessing wrong and having to open the second tower costs half a load, leaving ten shells
to fight with. And **the jeep can do it alone but should not** — eight grenades is eight
seconds parked fourteen metres from the tower with one-to-two hits of armour, which is a
gamble rather than a plan. That gap between "possible" and "sensible" is where the tank earns
its place; closing it entirely by pricing the jeep out was considered and rejected, because a
rule you cannot break is not a decision.

There is a test for both halves of that, read off the weapon table rather than restated:
`OneTankLoadPaysToOpenBothOfASidesTowers`.

---

## What was built

**The tower became a destructible, and it is the same destructible as everything else.**
`StructureKind` gained `FlagTower`; the prefab is now assembled by
`DestructiblePrefabBuilder.Assemble` — three state models, mesh colliders, a debris burst —
and `ObjectivePrefabBuilder` bolts the one component on top that makes it an objective rather
than cover. A second assembler for it would have been a second answer to what a destruction
state is.

**The tower is a container, and the flag is inside it.** Each of the three tower meshes
exports a `FlagMount` carrying the position *and rotation* a flag stands at, kept out of the
join exactly as the bunker keeps `LiftPlatform` and `Helipad` — so Unity reads an origin
rather than a height, and where the flag sits is an art decision. All three mounts are
**identical**: on the plinth, inside the walls. Breaking a tower open therefore *reveals* the
flag rather than moving it, and a sealed tower hides its flag physically rather than by a
renderer switch — there is no angle it could be glimpsed from even if something turned it on.
There is a test that walks the three states and insists the mount has not moved between them.

**The damaged tower has had its top taken off, not a window cut in it.** The first attempt
punched a band of windows between four corner posts, which was wrong for one reason that
overrides the rest: this game's camera looks *down* at 58 degrees. A hole in the roof shows
the whole flag; a window in a wall shows a slice of it. So the damaged state is a solid course
up to 1.75 m — above a tank, so it is a box to look into rather than a doorway to drive into —
with the four corners standing above it and nothing in between.

**The corners are laid as courses, because a tilted box slopes on the inside too.** Second
wrong attempt: corner posts tilted to follow the tower's taper. The maths was right — the
outer faces sat exactly on the intact tower's wall plane at every height — and it still read
as rectangles tacked onto a slope, because the *inner* faces leaned as well. A masonry tower
has a straight vertical shaft inside and steps its outer face inward course by course. So each
corner is now three stacked blocks with their inner faces all on one line (1.6 m across, which
the 1.45 m banner stands clear of) and their outer faces at 1.548, 1.393 and 1.237 m against a
wall that runs 1.625 to 1.16 over the same span. One corner is a course shorter than the rest
and four loose stones sit at the breaks, because four stumps of matching height read as a
design rather than as damage.

**Visibility and pickup are the same condition, deliberately.** `Flag.IsVisible` is now
`home.IsOpen`, and the loop that looks for a carrier runs only when the flag is visible.
Writing "you may take it from a broken tower" as a second rule would have been a second place
for the two to disagree — and a flag you could take without seeing would hand the decoy's
answer to whoever drove into a pyramid on spec. `GiveTo` refuses a sealed tower as well, so
the tests and the command-line still cannot stage a raid past the rule.

**The proximity reveal is gone rather than kept alongside.** `FlagRules.RevealRadius`,
`FlagTower.Scout`, `IsScouted`, `Forget` and the `Discovered` event are all deleted. Two ways
to learn the same fact means the cheaper one is the only one anybody uses, and driving past is
always cheaper than shooting — rule 1 would have been a fiction.

**A flag on a tower is reached from the tower's walls, not from the flag.** Taking the flag
turned out to depend on which way you drove up: nose-on to a wall a jeep's origin sits 4.2 m
from the middle of the tower - its own length past four and a half metres of masonry - and the
pickup radius is 4 m, so nothing happened. Side-on the same jeep sits at 3.1 m and gets it.
`FlagTower.DistanceFrom` measures to the tower's actual collider bounds instead, so the answer
is about 2 m from every approach and the vehicle's own shape stops leaking into the objective.
`LevelLoadingTests` parks a jeep against the real tower from eight directions.

**The HUD says what to do about it.** `THEIR FLAG NOT FOUND` became
`THEIR FLAG SEALED - BREAK A TOWER`, and the defender's line gains
`YOUR FLAG TOWER BREACHED` in the warning colour. That is the fix for a real hole: a tower
under fire looks exactly like a tower that cannot be hurt, and nothing on screen distinguished
them. It was noticed by shooting one and concluding it was indestructible.

---

## Decisions worth knowing

**Damaged counts as open, not just destroyed.** A tower that only gave up its flag once
flattened would make the damaged state a stage nobody stops at, and the five shells that got
you there wasted. As it is, cracking a tower is the decision and flattening it is optional.

**Nothing repairs a tower.** The second raid down the same lane costs a drive rather than a
sortie, which makes the first one an investment rather than a toll. It also means a defender
who loses a tower has lost it for the match — the pressure only goes one way, which is what
stops a match stalling.

**A tower is open for everybody, including the side being raided.** Same trade M6 made for
scouting: there is one world and two viewports of it. The defender watching their own pyramid
come apart is the warning that they have been found, and it is worth more than the symmetry.

**Anyone can damage any tower, including their own.** Structures are neutral, so the existing
rule applies unchanged: a defender whose rocket splashes their own real tower has just told
the raider where to go. That is a real own goal and it is left in, because special-casing the
tower would make it the one thing on the map that reads differently from everything else.

**The tower spacing rule was rebased, and it got weaker.** It used to be "more than twice the
reveal radius apart, so one drive cannot confirm both". Finding a flag costs ammunition now,
so the rule that still means something is about blast: two towers close enough to sit inside
one splash would both open to a single round. That is 9 m against the ASV's 4.5 m rocket; the
shipped map has 36 m. Standing in one place and shelling both is fine — the tank reaches 36 m
and repositioning was never the price.

**Which tower is real is authored in the file, but a match no longer plays what is
authored.** True from M6 through this pass, and reversed afterwards: `FlagTower.Roll`
rerolls the choice at random, per side, the instant a real match begins, so a raider who
has already played a map cannot simply remember which pyramid was real last time. The
authored value still means something - it is what the level editor shows and edits, and
still the one thing `LevelValidation` insists a file name exactly one of. See
`Objective/FlagTower.cs` and `Objective/Match.cs` (`OnEnable`); the fuller reasoning for why
the roll lives in `Match` rather than `LevelLoader`/`LevelBuilder` is in
[PAUSE_MENU_NOTES.md](PAUSE_MENU_NOTES.md), which landed in the same session.

---

## Gotchas

**`Open()` has to aim at whichever threshold this tower's next state is.** A tower with no
damaged mesh — which is what a bare `FlagTower` component gets in a test — goes straight from
standing to rubble, so damaging it to the halfway mark leaves it intact and `Open()` quietly
returning false. It aims at zero in that case.

**A bare `FlagTower` is not enough to test the rules on.** `[RequireComponent]` gives it a
`Destructible`, but an unconfigured one has default tuning and no state models. `FlagTests`
builds three empty state nodes and configures the shell with the real tuning, which is what
makes the damaged state reachable in a test at all.

**Counting `Destructible` no longer means counting scenery.** Two tests compared the number of
destructibles in the world against the number of structures in the level file; the four towers
made that 34 against 30. Both now skip anything carrying a `FlagTower`.

**Ten flag tests had to be told to open the tower first.** They were written when a fresh
tower handed over its flag, and every one of them failed with `AtTower` where it wanted
`Carried`. They use a `CreateOpenTower` helper now, so a test about capturing reads as being
about capturing; only the five that are about the tower rule start from a sealed one.

**Damaging a destructible outside play mode leaves its debris frozen on top of it.**
`Destructible.TakeDamage` spawns a `DebrisBurst`, and a burst clears itself up in `Update` -
which never runs in the editor. Every chunk sits at its spawn point, and a dozen chunks at one
point is a solid dark cube in the middle of whatever you just shot. This cost an hour: the
cube sat exactly where the flag stands, so it read as the flag rendering black. Any editor-side
render that damages something has to destroy the bursts afterwards. `StageRubble` in the
sandbox still gets away with it only because the shot scenery is far from the camera.

**The flag mount carries rotation as well as position, and both matter.** A banner turned
edge-on to the opening is a two-pixel green line at gameplay distance - which is no answer at
all to "which pyramid is the real one". The mount is also offset by half the banner's length,
because a flag hangs to one side of its staff: centring the *staff* puts a 1.45 m banner into a
1.3 m gap, so it clips the masonry on one side and shows a sliver on the other.

**`ObjectivePrefabBuilder.TowerModel` is gone.** The tower is loaded per state by
`DestructiblePrefabBuilder.ModelNameFor`, so the single-model constant had nothing left to
name. `CategoryOf` had to learn that `FlagTower` is a `Structure`, or the models would have
been looked for as `RF_Prop_FlagTower_*`.

---

## Verified

Run from `C:\git\projects\IronFlag` on Unity 6000.5.9f1: the project compiles with no errors
and **no warnings**, **216 edit-mode tests pass** and **96 play-mode tests pass**. Ten
existing flag tests were rewritten rather than added to - they were written when a fresh tower
handed over its flag, and every one of them failed with `AtTower` where it wanted `Carried`
(eleven call-sites, since one of them opens both a decoy and a real tower). New coverage:

- **The three rules, one test each.** An intact tower gives nothing away with a tank parked on
  top of it; breaking one open shows what it holds; a jeep is refused a flag from a sealed
  tower and takes it the moment the tower opens.
- **A tower stays open** and announces itself exactly once.
- **Opening the decoy buys nothing but the answer** — the real tower is still sealed, its flag
  still hidden, and a jeep standing at the decoy is still refused.
- **The prefab is a destructible** with all three state models, and every state carries a
  `FlagMount` in the *same* place - so damaging a tower reveals the flag rather than moving it.
- **An intact tower physically encloses its flag**, checked by placing the real flag prefab at
  the real mount and slicing the tower mesh at the flag's own height. Merely being shorter than
  the tower is not enough: the first attempt had the banner poking through a wall, because a
  flag hangs to one side of its staff and the tower is narrower up top than it looks.
- **A jeep parked against an opened tower can take the flag from all eight compass approaches**,
  measured against the tower's real collider.
- **The balance holds against the real weapon table**: a tower is tougher than anything else on
  the map, and one tank load pays to open both of a side's towers without paying for much else.
- Every map in the scene still has four towers, one real per side, all starting sealed, and no
  two of a side's close enough for one rocket to open both.

**Not verified: whether five shells is the right price.** It is a first guess, and the two ways
it can be wrong pull in opposite directions — too cheap and the decoy is a speed bump, too dear
and every match opens with the same six minutes of shelling. The number to move is
`StructureTuning.For(StructureKind.FlagTower).HitPoints`; everything else follows from it, and
`OneTankLoadPaysToOpenBothOfASidesTowers` will say when it has gone too far either way.

**Also not verified: whether a breached tower reads at a glance.** A player has to be able to
look at four pyramids and see which two have already been checked. A tower with its top off and
four stepped corners standing is a completely different silhouette from a whole one, and the
flag inside is a full green rectangle rather than the edge-on sliver the first attempt gave -
both are clear in a still. But the camera is thirty-four metres up in half a screen, and this is
exactly the kind of thing that turns out to be invisible in motion.

**And a smaller one: whether the flag is *too* well hidden while a tower is whole.** It is now
physically enclosed rather than merely switched off, which is the right rule - but it also means
there is no cue at all that a pyramid is worth shelling beyond the HUD line telling you so.

---

## What M6 said that is no longer true

[M6_NOTES.md](M6_NOTES.md) is otherwise still the right description of the flag. These parts
are superseded:

| M6 said | Now |
|---|---|
| "you cannot see which until you have driven within ten metres of it" | You cannot see which until somebody breaks one open |
| "Reveal radius 10 m" | Gone. The cost is 340 hit points, not a distance |
| "Nothing may shoot the objective" | Shooting the objective is the only way to the objective |
| "The towers join the bunkers as the two things on the map without a `Destructible`" | The bunker is the only one |
| "The flag tower's three states are exported and unused" | All three are used, and the difference between them is the mechanic |
| "Both towers of a side must be more than twice the reveal radius apart" | More than twice the widest blast radius apart |
| "scouting in the tank is the cheap way to buy the answer" | Still true, and now it is the *only* way — the tank is the one that can afford the shells |
| `THEIR FLAG  NOT FOUND` (HUD strip) | `THEIR FLAG  SEALED - BREAK A TOWER` |
| "the bunker and the towers are the two things on the map that may not be shot down" | The bunker is the only one |
