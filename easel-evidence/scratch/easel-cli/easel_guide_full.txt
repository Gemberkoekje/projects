# Painting with Easel

You are about to paint. Not draw, not render — paint. This file is the method: the
loop, the order of work, the mistakes you will make, ten exercises, and a checklist.
Read *The first hour*, run `easel demo mistakes` and look at the sheet, paint the
exercises at the end, and start. Come back here while you paint; open the other files
when a situation calls for one, and when the tool says something you do not know the
reason for, `easel explain <code>` prints it.

| File | What it holds | When |
|---|---|---|
| **`PAINTER.md`** — this one | the method: the loop, the order, the mistakes, the exercises, the checklist | now, and open beside you while you paint |
| [`RECIPES.md`](RECIPES.md) | one situation at a time: the calls in order, what it looks like when it goes wrong, and the number behind it | when you are about to lay one |
| [`PAINTING.md`](PAINTING.md) | the reasons: how the paint, the brushes and the planning tools behave; copying a photograph, in its last chapter | when a notice points at it — `easel explain <code>` prints the passage — or when a rule's reason is the question; the last chapter only if you have a photograph |
| [`REFERENCE.md`](REFERENCE.md) | every fact on one page: units, defaults, what each argument does | when you want to look something up |
| [`CALIBRATION.md`](CALIBRATION.md) | the measurement behind every number quoted in the other four | when a number is the question |

**One thing to settle before you read further: have you decided what to paint?** If
you have, [`paintings/`](https://github.com/Gemberkoekje/EaselAPI/tree/main/paintings)
is open to you — in the repository, since the package carries none of them — finished
pictures with the pass scripts that built them, the prelude of mixtures and masses
beside them, and the painter's own notes. They are the end-to-end worked example this
file cannot be. **If you have not decided, do not open them.** They name their subjects,
and a named subject chooses for you. Decide first, then look.

---

## The first hour

**What this is.** Brushes carry a finite load of paint and run out. Paint lands wet and
mixes with what is already there. The canvas has tooth. There are no layers, and undo
is scraping the canvas rather than a free rewind: **when something is wrong, you paint
over it.** You work in passes and you look between them.

```python
from easel import Session, cell, span, blob, region

s = Session(1024, 768, texture="linen", ground="toned_grey", seed=7, budget=300)
s.palette["dark"]  = s.palette.mix("ultramarine", "burnt_umber", 0.45)   # to a ratio
s.palette["light"] = s.palette.at_value("yellow_ochre", 0.78)            # to a value
```

Coordinates run `0..1` from the top-left. A place is `cell("D5")`, `span("C3", "F6")`,
`region("lower-half")` or a shape. Say a place; do not compute a coordinate. **The
`budget=300` is this example's**: set yours from the picture, more for one subject
worked closely than for a few big masses.

**The loop.** **Look every 5 to 15 strokes.** `s.look()` writes a PNG and returns the
path; open it. `s.look(values=True)` is greyscale, and it is the one that tells you the
truth. A stroke you did not look at was a guess. **Rehearse every pass before you pay
for it.** `s.rehearse(plan)` paints the plan on a copy and shows you the result; from
the shell, `easel run pass.py --rehearse` does it for a whole script. A rehearsal costs
a look and commits nothing, and what it shows is what will land. With `--alternatives`
it rehearses versions of a pass side by side, and with `--count` it prices one in
seconds.

**What a mark costs.** `stroke`, `dab`, `smudge` and `glaze` are one each. `block_in`,
`sweep`, `scumble` and `cover` are one *per pass* — ten to thirty for one call, and the
number nobody can guess: `s.cost(plan)` says, and `s.cost_line(plan)` says why. Drawing,
looking, drying and planning are free.

```python
plan = [{"shape": blob(cell("D5"), 0.12, seed=3), "brush": "bristle",
         "color": "dark", "size": 0.09}]
s.cost(plan)                  # what it charges
s.preview(plan)               # where it would go
s.rehearse(plan)              # what it would look like
s.paint(plan)                 # the same plan, now paid for
```

**The order. It is the whole method, and it is not optional.**

1. **Draw it first, in graphite.** `s.pencil()` is free. Put the arrangement down,
   look, move it, and keep drawing until it is proportional and the way you want it.
   Nothing you paint later fixes a composition you did not draw. `s.thumbnail({shape:
   value})` shows it flat and small, where a silhouette reads or does not, and is free
   too.
2. **Big masses first, in the biggest brush you will use**, at `density` below 1 so the
   ground breathes through. Two or three of them. Not outlines — masses.
3. **Back to front.** The furthest thing goes down first and every nearer thing is
   painted over it. Nothing is cut around.
4. **Check the values before anything else.** In greyscale, a clear light, a clear mid
   and a clear dark. Two masses closer than `0.10` in value read as one.
5. **Then the mid-tones**, across the whole canvas, each mass along *its own* axis. A
   soft passage is overlapping strokes at close values, not a smudge.
6. **Edges last but one.** Hard edges pull the eye; soft and lost ones let it move on.
7. **Highlights last, smallest brush, fewest strokes.** Ten deliberate marks.

```python
far  = blob(span("B2", "G4"), wobble=0.3, seed=1)
s.block_in(far, "bristle", "dark", density=0.8, size=0.18, direction="axis")
s.look(values=True)                                  # before anything goes on top
s.block_in(span("A5", "H8"), "flat", "light", size=0.14, solid=True, direction=6)
s.dry()                                              # so the next paint covers, not mixes
s.stroke([(0.30, 0.62), (0.42, 0.58), (0.55, 0.60)], "round_hard", "dark", size=0.02,
         pressure="taper", opacity=0.9, note="subject")   # a mark by hand, and counted
```

**What holds the paint where you put it**: `edge="hard"` ends a mass on its own
outline, and `clip=shape` holds any call inside another shape.

**Before the first stroke.** Count the horizontal bands in the drawing; more than
three, and find a viewpoint or a thing that crosses them while it is still graphite.
**Then count them inside the biggest mass, and count any row of like things**: a comb
inside a mass is the same fault one scale down, and the band count is the instrument
for both. **What is this thing's foreshortening?** Draw the view, not the object; no
brush repairs the difference. **Write down what you decided, where the engine can see
it** — `s.plan(values=..., lightest=..., subject_share=..., ground=..., key=...,
why=...)`. The three values, the place meant to be lightest, the share of the budget
the subject gets, and whether this picture is low in key or buries its ground are yours
to choose, and declaring them is what makes the check answer to your picture rather
than to a default (*What the plan changes* in
[`REFERENCE.md`](REFERENCE.md#what-the-plan-changes)). `s.checklist()` quotes your
sentence back at the end, and nothing else can ask for it.

**The six things you will get wrong**, with the fix on the same row as the mistake.

| The mistake | What it looks like | Do this instead | Where |
|---|---|---|---|
| **Outlines, filled in** | a coloured shape with a drawn edge; a thin dark line run along a silhouette to sharpen it | paint the mass with a brush wide enough to cover it in a few strokes; sharpen an edge by painting the mass on the *other* side of it | *What you are bad at*, below |
| **Boxes** | rectangles, especially in the background, which nobody made you draw | `blob`, `ellipse`, `hull`, `ribbon`, `polygon`, `union`; check the background hardest | *Masses that are not rectangles* in [`PAINTING.md`](PAINTING.md#masses-that-are-not-rectangles) |
| **Parallel marks** | hatching; a grain repeated thirty times; a soft passage laid as three hard bands | vary the direction; two directions break a comb; a soft passage is `s.scumble(...)` | *The angle of the mark* in [`PAINTING.md`](PAINTING.md#the-angle-of-the-mark); *A quiet gradient* in [`RECIPES.md`](RECIPES.md#a-quiet-gradient) |
| **Reaching for `undo`** | a scraped canvas and a stream of marks put back one at a time | `s.cover(place, colour)` buries a mistake inside the place you gave it; keep each mass in a named function and re-run the stack | *A repair under things that are standing on it* in [`RECIPES.md`](RECIPES.md#a-repair-under-things-that-are-standing-on-it) |
| **The tool's own shape** | floating discs; capsules; a rectangle with chisel ends; a staircase down a sloped side | give a mark a length, or `tip_wobble=0.7`. The tool says the rest: `chisel-staircase` at the call, and `report()` counts the discs | *The shape each tool leaves behind* in [`PAINTING.md`](PAINTING.md#the-shape-each-tool-leaves-behind) |
| **Repainting a passage that has failed twice** | a third treatment of one passage, and a fourth, each more expensive than the last | **if a passage has failed twice, the fault is upstream of the brush.** Go back to the drawing: it is still free, and it is the only thing that is | *What you are bad at*, below |

**And six failures, painted**, because a failure you have seen is worth more than one
you have read: `easel demo mistakes` puts them on one sheet. Each is a recipe's own
*Goes wrong as* in [`RECIPES.md`](RECIPES.md), each is something the tool says at the
call or after the pass, and `easel demo <words of its heading>` paints any of them
beside the call that does not make it.

**When you think it is finished**, run `s.checklist()`. It answers every line that
has a number behind it, and leaves you three that nothing can measure — they are at
the end of this file, and they are about finishing rather than about faults: the last
third of the budget goes on what surrounds the subject, the last marks are about the
picture rather than a score, and the reason you chose the subject is still in it.

**Now run `easel demo mistakes`, paint the ten exercises** at the end of this file, and
start.

**And before you lay a passage you have not laid before — a form that turns, a graded
field, a hollow thing, lit air — open [`RECIPES.md`](RECIPES.md) and find it.** One
painter finished a whole picture without opening any of the other four files, and its
two worst passages were both recipes three keystrokes away. The moment to go looking is
*before* the pass, not after the rehearsal shows it failing.

---

## Getting started

```python
from easel import Session

s = Session(1024, 768, texture="linen", ground="toned_grey", seed=7)
```

Coordinates are always `0.0` to `1.0`, origin **top-left**. `(0.5, 0.5)` is the
centre, `(0.9, 0.1)` the top right. Where a drawing wants pixels, `s.px(x, y)` is a
pixel's place.

**Both axes run 0–1, so on a canvas that is not square the same number is a different
distance in each.** Brush sizes are a fraction of the canvas's **long side**, so a
brush is round, but `ellipse(p, 0.1, 0.1)` is an oval. Rather than do the arithmetic,
ask the session:

```python
s.circle((0.5, 0.5), 0.09)                 # round in pixels, on any canvas
s.circle(cell("D5"))                       # the biggest circle that fits the cell
s.circle((0.5, 0.5), 0.09, wobble=0.25)    # round, with a silhouette nobody drew
```

Grounds: `white`, `warm_white`, `toned_grey`, `toned_warm_grey`, `burnt_sienna`,
`umber_wash`, `cool_grey`. Textures: `smooth`, `linen`, `rough`. **Start on a toned
ground.** A mid-tone ground means your first marks already sit in a value relationship,
and any ground left showing reads as a colour you chose; on white every gap is a hole.
A picture whose masses all sit below the presets wants a ground of its own:
`Session(ground="#5a5045")` reads `0.32`, and `value_of` will tell you any other.

---

## The workflow

The seven steps of the card again, each with the mechanism behind it.

### 1. Draw the arrangement in graphite

A rehearsal shows you one answer; a pencil shows you six at once.

**Write down why this subject and not another, in a sentence, before the first mark.**
Not what it is — why you chose it over the others. One painter chose the underside of a
pier because there the light arrives from *below*, bounced off the water, so every form
is lit backwards; it then painted a competent dark structure lit from the ordinary
direction, and passed every line of the closing checklist on the way. The sentence is
free, and it is the only thing that can be checked at the end.

Five things about the drawing:

- **Count the horizontal bands before the first mass.** More than three, and find a
  viewpoint or a thing that crosses them now, while it is graphite. **Count them
  inside the biggest mass as well, and count any row of like things**: four fingers
  and nine pots on a ledge are the same question at a smaller scale, and the mass
  being one mass is what hides it.
- **Ask what the view is, not only what the arrangement is.** Everything around this
  step — the grid, the cells, `preview`, three silhouettes in one look — helps you
  move a mass rather than turn it, so the question has to be asked out loud: **what
  is this thing's foreshortening?** Draw the view, not the object (*A small cluster
  of like parts gripping an edge* in
  [`RECIPES.md`](RECIPES.md#a-small-cluster-of-like-parts-gripping-an-edge)). (*A mass
  built of planes* is the inside of a mass once the view is settled; this is the
  question before it.)
- **If the scene is built of straight edges that converge, write the projection before
  you draw anything.** You are reliable about sizes and unreliable about where they
  land (*A scene with straight edges* in
  [`RECIPES.md`](RECIPES.md#a-scene-with-straight-edges)).
- **Draw the planes of anything built from them with its silhouette**, not after the
  mass is down (*A mass built of planes* in
  [`RECIPES.md`](RECIPES.md#a-mass-built-of-planes)).
- **Landmarks before anything, pencil after the far masses, near masses on top.** Paint
  buries graphite in proportion to how much lands; a `mark()` is a point beside the
  canvas and cannot be buried. So draw the whole arrangement now to judge it, and draw
  the near things *again* once the far masses are down — unless you draw it with
  `s.guide()`, which is `mark()` along a path: graphite on the view rather than in the
  canvas, so `look()` keeps showing it and `export()` never does.

```python
s.guide([(0.05, 0.38), (0.95, 0.34)], note="bench")   # still there at step 7
s.guide(mass, note="mass")                            # a shape: its outline, corners kept
s.unguide("bench")                                    # or s.erase(), which takes both
```

`erase()` rubs out both drawings, the graphite and the overlay, which is what redrawing
an arrangement wants; `unguide(note)` takes back one labelled part and leaves the rest.

The two passages you do not draw are the two that will come out weakest. Draw the small
things too — a lid, a handle, what stands in a thing — before the pass that paints them.

### 2. Tone the ground, then find the big shapes

**`density` spaces the passes; it does not fill them.** Every pass runs dry along its
own length whatever the spacing, so `density=1.0` still leaves a twentieth of the ground
showing. When a mass has to be solid — because it is near, or because fine marks will
stand on it — say `solid=True`. Not for these first masses.

```python
s.block_in(blob(cell("D5"), 0.26, wobble=0.3, seed=2), brush="bristle", color="dark",
           density=0.7, size=0.2, direction="axis")
```

Resist detail here: if you can already name what you are painting, you have gone too far
too early.

### 3. Paint from back to front

A near shape's edges are real edges — where its paint stops and the mass behind it still
shows — and you drew none of them. Paint it first and the only way to those same edges is
to cut the mass behind it carefully around it, which is painting *up to* a line.

- **Let the near mass overlap.** A silhouette that stops exactly on a boundary was
  measured; one that overlaps was painted.
- **A veil of light is a mass at a depth**: laid last because it is *light*, it is
  still in front of something, and `report()` says so when it buries details.
- **A mistake in the background is cheap until something stands on it.** Keep every mass
  in its own named function so the stack can be re-run in depth order for a repair later.

**Anything with an inside has its own depth order: the far edge, then what is inside,
then the near edge.**

```python
far  = ellipse(span("D2", "F3"))          # what stands behind the inside
near = polygon([(0.36, 0.31), (0.64, 0.31), (0.62, 0.72), (0.38, 0.72)])

s.block_in(far, "flat", "pale", size=0.04)                 # behind the inside
s.block_in(far.inset(0.015), "flat", "dark", size=0.04)    # the inside
s.block_in(near, "flat", "light", size=0.06)               # in front of the inside
```

Painted the other way round there is nothing for the inside to stop against. Ask it of
anything you can see into, before the first stroke.

### 4. Check your values before you check anything else

**Plan the three as numbers before you mix anything.** `palette.value_of(c)` reports
exactly what the greyscale view will show, and `palette.at_value(base, target)` hands
back the mixture of `base` that reads at `target` — **from whichever side it starts**,
mixing in white to raise a value and a dark to lower one. It raises when a value is out
of reach rather than handing back the nearest it managed.

Two masses within `0.10` read as one, where the two places meet. **Write the plan down
where the engine can see it** and it names those pairs on the empty canvas —
`plan-pairs` — then says after every pass which places are painted as promised.

```python
upper, lower = span("A1", "H4"), span("A5", "H8")
s.plan(why="the light comes off the water", values={upper: 0.72, lower: 0.38},
       lightest=upper, subject_share=0.40)  # everything you decided, held to
s.compare(s.plan())                         # and the sheet, whenever you want it
```

Re-read the places whenever you move a silhouette: a place reads what most of it reads,
and one that now straddles two masses says so — `(18% of it lighter by more than 0.15)`.
**Plan the place your `why` names, at the value the place will read** — not the value
its mixture was mixed at. A light is often a small bright part of a place that is mostly
something else, and `lightest:` prints both.

**That threshold is a floor, and a mass also has a ceiling.** Shading spends value range,
and the range is shared: shade until the form clears `0.10` and stop, because past about
`0.15` across one mass its shadow side starts closing on whatever it stands against.

**The box has no black and does not need one.** Its floor is burnt umber alone, at
`0.128`; `mix("ultramarine", "burnt_umber", 0.5)` is the neutral dark just above it, at
`0.14`, and varying the ratio swings it cool to warm between `0.13` and `0.15`. Piling
on passes will not go lower; if a mass is not dark enough, mix it darker, and under the
floor supply a colour of your own. **A cast shadow is the first place you will spend
that dark, and the wrong one**: a shadow lying on a lit surface is a step or two below
*that surface*, and it is a tapering stroke that loses its far end, not a filled shape.

### 5. Refine the mid-tones

**There is no gradient tool.** A painter lays the gradient as steps and then loses the
joins while the paint is wet — and one `smudge` removes about 40% of a step, once, while
a second pass undoes most of the first. When once is not enough, stop smudging and lay
paint:

```python
s.scumble(span("A4", "H6"), "shadow", "light", 8)    # close the join with paint
```

Passes running along the band and stepping across it, one value step each, every pass
wider than the step so the joins close. Leave `size` off: the verb picks its brush from
its own step. Below about five passes the steps read as steps again.

**A passage that is light in the *middle* is the same verb turned inward** —
`direction="inward"`, for a lit patch or a bloom, which is dark at every edge. The first
ring lands on the boundary, so give it the value the patch meets its surroundings at.

A field gradated top to bottom is a stack of horizontal bands until its joins are gone
and something crosses it, and a stack of bands is a composition whether or not you meant
one (*A graded field that is most of the picture* in
[`RECIPES.md`](RECIPES.md#a-graded-field-that-is-most-of-the-picture)).

### 6. Edges: lost and found

The step most painters so far have skipped. Decide where you want attention and lose
every other edge — two masses merging with no boundary at all in places. Lose one edge
completely rather than four partly. The check counts what you did: `edges:` is the share
of the picture's boundaries under `2.5` px wide, and a picture whose every edge is crisp
says so as a number (*The `edges:` row, measured twice* in
[`CALIBRATION.md`](CALIBRATION.md#the-edges-row-measured-twice)).

`smudge` softens a *stretch* of a boundary, once. Hand it the stretch you mean to lose —
under a tenth of the canvas, along the boundary's own shape — and leave `size` off:

```python
s.smudge([(0.30, 0.40), (0.34, 0.415), (0.38, 0.43)])  # a curve's own points
s.smudge(mass.closed[3:5])                             # or a stretch of a shape's outline
```

The call says when a path crosses a boundary instead of following it (`smudge-across`),
runs long enough to leave a band of its own (`smudge-long`), or is wider than one pass
can use (`smudge-wide`). One pass, not three. If the boundary is still there afterwards,
the answer is paint laid across it, not another smudge (*An edge that is actually lost*
in [`RECIPES.md`](RECIPES.md#an-edge-that-is-actually-lost)).

### 7. Highlights last, smallest brush, fewest strokes

Every highlight you add makes the others count for less. **Anything the size of a cell
or smaller has taken three marks so far — the dark, the light, and the edge between
them** — laid dark first, and looked at through a `region=` crop before the light goes
on. That is a habit and not a measurement: the number that is yours is the budget's
split, which `s.plan(subject_share=)` holds the check to. `size` is the *feature's*
fraction of the canvas, not the mass's.

---

## What you are bad at, and what to do instead

Be honest about these. They are specific to what you are, and the fix is beside each.

**You cannot reason in pixels.** You will misjudge absolute positions and you are
reliable at *relationships*. Look with the grid on, say "the dark mass sits around D5
to F6", then paint into `cell("D5")`:

```python
s.look(grid=True)                    # labelled A–H across, 1–8 down
s.block_in(cell("D6"), ...)          # then target what you saw
s.block_in(region("upper-left"), ...)
s.stroke([below(region("center"), 0.1).point(0.5, 0.5), ...])
```

**You will draw outlines and fill them.** That is how you make colouring books. Paint
the mass directly with a brush wide enough to cover it in a few strokes, and let the
*edge of the mass* be the drawing. There is no outline in a painting — there is a place
where one mass stops.

**Finding an edge is the same mistake in disguise.** When a silhouette is not crisp
enough, the reflex is to run a thin dark stroke along it, and that stroke reads as a
drawn line the moment you look. Sharpen an edge by painting the mass on the *other*
side of it: a brush at least `0.03` wide, running along the boundary with its centre
outside the shape, laying the background's own colour up to where the shape stops.

**A hole in a mass is the same rule again.** Dab the background colour into the middle
and you get a floating disc. A gap is a broken sliver worked in from the silhouette
with a starved brush, because that is where the light comes in:

```python
s.stroke([(0.42, 0.36), (0.47, 0.41)], "round_hard", "pale",
         size=0.012, load=0.3, opacity=0.7)      # in from the edge, not a dot
```

**Over half the paintings so far have boxes in them, and the checklist counts them** —
`boxes: 3 of 11 masses were laid in a rectangle`. A rectangle is the easiest place to
name, and if you block in a shaped mass as a box you get a box, and no later work
removes it. Build the shape, preview it, and **check the background hardest**: you will
reach for a shape on your subject, because its silhouette was a problem, and then lay
everything behind it in boxes because nothing back there asked anything of you. A
background of square patches reads instantly as made by a machine. Give the masses that
needed no drawing the same treatment as the ones that did — and the marks inside them
are not parallel lines. If a surface has a grain, the grain varies: it breaks, it
crosses, it disappears for a whole passage. Three marks that describe it beat thirty
that repeat it.

**You will reach for `undo`.** Real repairs happen with paint. `s.cover(place, colour)`
dries the area and buries it with every clause of the recipe in place — a long solid
stroke at full load with no run-out, held to the area:

```python
s.cover(cell("D5"), "corrected_colour")      # the whole recipe, already set

s.dry()                                      # or the same clauses on a stroke of your own
s.stroke([(-0.05, 0.55), (1.05, 0.58)], "flat", "corrected_colour",
         size=0.09, load=1.0, load_falloff=0.0, opacity=1.0, pressure="even")
```

A `bristle` does not bury — its comb leaves the old paint showing between the streaks.
**`cover` lands the size of the place you hand it** and not a brush past it. On a worked
passage that is a crisp patch, which can read as cut out, and it is the smaller fault:
run a brush past the place, as it was before 0.6.0, a repair left four times the place
visibly repainted, the neighbouring marks with it (*A burial and the place it was
handed* in [`CALIBRATION.md`](CALIBRATION.md#a-burial-and-the-place-it-was-handed-060-the-default-moved)). Or
bury it by hand with marks shaped like the passage. `undo(n)` exists; treat it as
scraping the canvas, and remember that `n` counts log entries rather than marks you
paid for.

**You will repaint a passage that has failed twice.** This is the rule for when to stop
doing that, and it is a number because a preference will not survive the moment it is
needed: **if a passage has failed twice, the fault is upstream of the brush.** Go back
to the drawing. It is still free, and it is the only thing that is. One painting spent
about eighty strokes on four successive treatments of one failing passage — vary the
brushes, break the lights, lay core darks, abandon two of the four fingers — each one a
brush-level answer to a drawing-level fault, and each one making the next repaint dearer
because more was standing on it. The drawing underneath was a cupped hand drawn as four
fingers laid out sideways, and no brush was ever going to fix that.

**You will under-vary your objects, and `report()` says so when the marks are alike
enough** — *one disc printed 5 times*, *a loop's signature: one length, one spacing*.
Having worked out how to paint one of a thing,
you will paint the next with the same recipe, and a viewer reads three copies of one
object rather than three of a kind. Vary one thing per object on purpose: which way its
light falls, how sharp its edge is, how much of it the mass in front takes away. One
difference each is enough.

---

## Ten small exercises

**Nothing stops you skipping these, and the cost of it is the same every time**: a
painter who skips them meets the same lessons inside the picture instead, with the rest
of the painting already standing on the mass that has to be repainted. An exercise is
the one place where a mistake has nothing built on top of it. They take a minute each.

Run them in one script if you like, but **give each `look()` a `path=`** — or run each
in its own directory. Looks are numbered from what is already in `out_dir`, so nothing
is overwritten; ten unnamed looks are still ten files to tell apart afterwards.

**1. A value scale.** Nine even steps from the darkest mix to white. Mix to a *value*,
not to a ratio: white is much stronger than its share of the mixture, so ask for the
value and let `at_value` find the ratio.

```python
from easel import Session, Region

s = Session(900, 200, ground="toned_grey", seed=1)
p = s.palette
dark = p.mix("ultramarine", "burnt_umber", 0.5)     # the box's neutral dark
lo, hi = p.value_of(dark), p.value_of("titanium_white")

for i in range(9):
    target = lo + (hi - lo) * i / 8
    band = Region(i / 9.0, 0.15, (i + 1) / 9.0, 0.85)
    s.block_in(band, "flat", p.at_value(dark, target), density=1.0, size=0.06,
               direction="axis")       # along the band, so its ends miss the next one
    print(f"value {target:.2f}  reads {p.value_of(p.at_value(dark, target)):.2f}")
s.look(values=True)     # nine even steps, 0.14 to 0.96
```

A third of white gets you the first step of nine and it takes nine tenths to reach the
eighth: a mixture that "should" be halfway comes out too dark, and the fix is always
more white than feels right. It goes both ways — to lower a value it mixes in a dark,
so a planned value is reachable from either side.

**2. One stroke, six pressures.** On the round brush on the left they change the
*width* of the mark as well; on the `bristle` on the right only how much paint lands.

```python
from easel import Session

s = Session(900, 500, ground="toned_grey", seed=2)
for i, p in enumerate(["taper", "press_in", "lift_off", "even", "swell", "dab"]):
    y = 0.12 + i * 0.15
    s.stroke([(0.10, y), (0.50, y)], "round_soft", "titanium_white", pressure=p,
             size=0.05, opacity=0.35, load=1.0, load_falloff=0.0, note=p)
    s.stroke([(0.55, y), (0.92, y)], "bristle", "titanium_white", pressure=p,
             size=0.05, opacity=0.35, load=1.0, load_falloff=0.0, note=p)
s.look()      # soft brush on the left reads the profiles most clearly
```

**3. Paint running out.** The same stroke at four loads, on rough canvas. The broken
streaks a starved brush drags are what everything laid after it sits on.

```python
from easel import Session

s = Session(900, 400, texture="rough", ground="toned_grey", seed=3)
for i, load in enumerate([1.0, 0.6, 0.35, 0.2]):
    y = 0.15 + i * 0.22
    s.stroke([(0.06, y), (0.94, y)], "bristle", "titanium_white",
             size=0.07, load=load, load_falloff=0.0, pressure="even")
s.look()
```

**4. Wet versus dry.** The same yellow over blue, once into wet paint and once onto dry.
Each band is a *single* stroke, because a block-in is many strokes and the wet half
would have dried before the yellow arrived.

```python
from easel import Session

s = Session(800, 400, ground="white", seed=4)
s.stroke([(0.10, 0.27), (0.90, 0.27)], "flat", "ultramarine", size=0.22, pressure="even")
s.stroke([(0.15, 0.27), (0.85, 0.27)], "flat", "cadmium_yellow", size=0.10, pressure="even")

s.stroke([(0.10, 0.73), (0.90, 0.73)], "flat", "ultramarine", size=0.22, pressure="even")
s.dry()
s.stroke([(0.15, 0.73), (0.85, 0.73)], "flat", "cadmium_yellow", size=0.10, pressure="even")
s.look()      # top band goes olive; the bottom one stays yellow
```

**5. An edge study.** One hard, one soft, one lost. Look at which one your eye goes to.

```python
from easel import Session, region

s = Session(900, 300, ground="toned_grey", seed=5)
left, mid, right = region("all").split_h(3)
for r in (left, mid, right):
    s.block_in(r, "bristle", "burnt_umber", density=1.0, size=0.1)
s.dry()
light = s.palette.tint("burnt_umber", 0.6)
s.block_in(left, "bristle", light, density=1.0, size=0.1, edge="hard")  # its edge on the line
s.stroke([(0.38, 0.5), (0.62, 0.5)], "round_hard", light, size=0.03)    # hard
s.smudge([(0.333, 0.40), (0.333, 0.60)])                                # soft: a stretch of it
s.look()
```

**6. Mixing.** Prove to yourself what the pigments do together.

```python
from easel import Session

p = Session(600, 200, seed=6).palette
for a, b in [("cadmium_yellow", "ultramarine"), ("cadmium_red", "ultramarine"),
             ("cadmium_yellow", "cadmium_red"), ("cerulean", "titanium_white")]:
    print(a, "+", b, "->", p.hex(p.mix(a, b, 0.5)))
```

**7. Draw, try, paint.** The whole precision loop, on an abstract shape: this is how a
feature gets painted.

```python
from easel import Session, cell, polygon

s = Session(900, 600, ground="toned_grey", seed=7)
s.mark("a", *cell("C3").point(0.5, 0.5))       # three verified points
s.mark("b", *cell("F3").point(0.5, 0.5))
s.mark("c", *cell("D6").point(0.5, 0.5))
s.pencil(polygon([s.pt("a"), s.pt("b"), s.pt("c")]), pressure=0.7)   # a shape: corners kept
s.look()                                        # the drawing, before any paint

plan = [{"points": [s.pt("a"), s.pt("c")], "brush": "bristle", "size": 0.09,
         "color": "titanium_white"}]
s.rehearse(plan, region="C3:F6")                # what would that mark look like?
s.rehearse([dict(plan[0], size=0.03, brush="liner")], region="C3:F6")   # or this?

s.stroke(**plan[0])                             # spend the stroke on the better one
s.look(region="C3:F6")     # the pencil is gone under the paint and still there beside it
```

The two rehearsals cost nothing and neither appears in `s.log()`. The drawing did not
count against `s.stroke_count`. The graphite has vanished exactly where the paint
landed and survived everywhere else, which is what an underdrawing is for.

**8. A box and a shape.** The same mass twice, so you can see the difference before you
have to judge it in a painting.

```python
from easel import Session, blob, cell

s = Session(900, 400, ground="toned_grey", seed=3)
s.palette["dark"] = s.palette.mix("ultramarine", "burnt_umber", 0.45)

mass = blob(cell("B4").point(0.5, 0.5), 0.16, 0.30, wobble=0.3, seed=1)
s.block_in(mass.box, "bristle", "dark", size=0.10, direction="axis")   # as a box
s.block_in(mass.shifted(0.5, 0.0), "bristle", "dark", size=0.10,
           direction="axis")                                           # as a shape
s.look()
```

Same brush, same direction, much the same number of passes. One is a rectangle and
will still be a rectangle at the end of the painting; the other has a silhouette, and
a silhouette is what a mass *is*. Look at the ends of the passes on the shaped one:
they stop at the boundary, and the brush breaks past it by about half its width, which
is the ragged edge you want and did not have to make.

**9. A swatch strip.** Exercise 1 calibrates value; this calibrates *hue*, where the
mixing surprises live. Lay every mixture you plan to use side by side and look before
the first mass: a grey that is secretly green shows here and not in its numbers.

```python
from easel import Session, Region

s = Session(900, 200, ground="toned_grey", seed=9)
p = s.palette
plan = {"haze": p.at_value(p.mix("cerulean", "titanium_white", 0.8), 0.64),
        "deep": p.at_value(p.mix("cerulean", "burnt_umber", 0.4), 0.40),
        "dark": p.at_value(p.mix("burnt_umber", "viridian", 0.3), 0.20),
        "lit":  p.at_value(p.mix("yellow_ochre", "titanium_white", 0.6), 0.78)}
for i, (name, colour) in enumerate(plan.items()):
    band = Region(0.05 + i * 0.225, 0.15, 0.25 + i * 0.225, 0.85)
    s.block_in(band, "flat", colour, size=0.04, solid=True)   # a fifth of its width
    print(f"{name:5s} value {p.value_of(colour):.2f}  chroma {p.chroma_of(colour):.2f}")
s.look()            # the hues beside each other, which the numbers cannot show
```

`chroma_of` is *how coloured*, beside `value_of` for how light.

**10. Three silhouettes.** One set of parts laid three ways, each thumbnailed. Write
down what you expect each to read as *before* you look.

```python
from easel import Session, ellipse, ribbon, union

s = Session(900, 600, ground="toned_grey", seed=10)
big, top = ellipse((0.5, 0.6), 0.16, 0.13), ellipse((0.5, 0.44), 0.06, 0.08)
members = [ribbon([(x, 0.64), (x, 0.9)], 0.03) for x in (0.42, 0.58)]
turned = [top.shifted(-0.13, 0.04), members[0].shifted(-0.03, 0),
          members[1].shifted(0.05, 0)]
swell = ribbon([(0.40, 0.68), (0.35, 0.80), (0.37, 0.93)], 0.07, end_width=0.02)
for parts in ([big, top, *members], [big, *turned], [big, turned[0], swell, turned[2]]):
    print(s.thumbnail({union(*parts): "burnt_umber"}))     # free: a drawing's rehearsal
```

---

## A checklist before you call it finished

**Passing this list means the painting is not *wrong*. It does not mean it is
finished.** Most of it is measured, and the tool answers it:

```python
print(s.checklist())          # or `easel check painting.easel` from a shell
```

That prints a number for every line that has one behind it — `values:`, `edges:`,
`ground:`, `boxes:`, `subject:` and `unspent:` — with the marks that landed nothing, and
whatever else the post-pass check has to say about the painting as a whole. What each
line measures, and when it speaks, is one table under `report()` in
[`REFERENCE.md`](REFERENCE.md#looking-planning-measuring). Read it, and then answer the
three below.

**These three are the ones to answer slowly**, because nothing can ask them for you.
The check reads marks and measures pixels; every line it prints is one or the other.
A picture can pass all of them and have quietly become a different picture,
competently painted.

- **Is the thing you measured most carefully still attached to the picture?** Cover
  it and look at what is left. And is the lightest mass the one you planned to be
  lightest? A mass that has quietly become the brightest thing takes the eye whatever
  the picture is about.
- **You have named the weakest passage. How many strokes are left? Spend them
  there.** A painter who stops with a third of the budget unspent has left the
  picture short without deciding to. The passage you would apologise for is the one
  that wants them — not the one you have most recently been enjoying — and the last
  marks are about the picture, never about a score.
- **Read back why you chose this subject. Is that reason still in the picture, looked
  at small?** `s.look(scale=256)` is about the size it will be met at, and a reason
  only a crop can find is not in the picture. Not *is the painting good* — is the
  thing you wanted there. `checklist()` quotes your own `s.plan(why=...)` back at you,
  which is the whole of what an instrument can do for it. If the reason is gone and
  strokes are left, that is what they are for.

One of the measured lines is worth knowing the shape of before you read it. **The
subject's share** is counted off the log, so say which marks they are — and after the
number is taken it is *meant* to fall, because the last third of the budget goes on
what surrounds the subject: a well-built feature in an unfinished picture reads as a
detail come loose, and a rough one in a picture that holds together reads as the
thing itself.

```python
s.stroke([(0.30, 0.40), (0.45, 0.44)], "round_hard", "light",
         size=0.02, note="subject")               # as you paint it
```

## Sign it

When you have decided it is finished — after the last look, before you export — sign
it. **Up to five marks, and they do not come out of your stroke budget**, so long as
each one carries `note="signature"`:

```python
s.stroke([(0.86, 0.94), (0.90, 0.92)], "liner", "dark", size=0.006,
         note="signature")
```

You do not have a name, so this is not a name. It is a mark: whatever you would put in
the corner of something you made. It is not a title and it is not a last chance to fix
something. Put it where it does the picture no harm — a corner, small, close in value
to what it sits on — and say in your notes what you chose and why. That question is
asked here, at the end, because a mark chosen to be explained is not the same as a
mark chosen.

```python
s.export("painting.png")
s.timelapse_gif("painting.gif")
```

---

## Working from a shell instead

The same thing is available from the command line, without holding a Python process
open. Session state lives in one file. If `easel` is not found, put `python -m easel`
in front of the same arguments; do not spend time fixing your `PATH`.

```bash
easel new painting.easel --size 1024x768 --texture linen --ground toned_grey --seed 7 --budget 300
easel run painting.easel pass1.py                 # your script; `s` is already defined in it
easel run painting.easel pass1.py --thumbnail     # ...its masses flat and small, unpainted
easel run painting.easel pass1.py --rehearse      # ...against a copy, committing nothing
easel look painting.easel --grid
easel look painting.easel --values
easel look painting.easel --region D4 --fine
easel compare painting.easel ref.jpg              # per-cell value numbers
easel export painting.easel painting.png
```

A script given to `easel run` has `s`, `palette` and the whole API already in scope. A
`prelude.py` beside the session file runs first, in the same scope, so mixtures,
landmarks and the masses-as-functions do not have to be redefined at the top of every
pass. **The habit here is: rehearse every pass with `--rehearse`, look, and only then
run it without.** Name several scripts at once and they run in order; rehearsed
together they share one copy, so each is judged on the pass before it. After
every pass `run` prints the budget line, the pass's dearest calls by the line that made
them, and a check of the pass against the standing warnings, whose rules are a table
under `report()` in [`REFERENCE.md`](REFERENCE.md#looking-planning-measuring); read it
before the next pass. The full command list is in
[`REFERENCE.md`](REFERENCE.md#the-session-and-the-shell).
