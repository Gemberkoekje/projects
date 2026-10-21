# Hamlet — an Ambient Village at the Edge of the Screen

The design (§1–§9), the roadmap (§10) and the risks (§11) for the ambient-village effort. It sits
beside `vista-plan.md` and depends on it: Vista renders the landscape and the buildings; Hamlet
simulates the village and draws the things that move. **Not started.** Hamlet is the project after
Vista; nothing here is scheduled before Vista's M1 sign-off, and the one thing it asks of Vista
before then is a note in M8 (§8.3).

Everything below was settled in a brainstorm on 2026-10-21; the decisions are collected in the
appendix so they don't have to be re-litigated. "Hamlet" is a working name.

---

## 1. Vision

A thin, borderless, always-on-top window docked to the top or bottom of the screen, showing a
village in a valley. It takes **no input**. It runs while you work, and you glance at it once in a
while. The sky is the real sky — your real sunrise, season and weather — and the village lives at
its own faster tempo: in a real hour a village week goes by. Smoke rises, a waterwheel turns, carts
come and go, torches light at your actual dusk.

Once in a while you glance over and something is *different*. The mill is a blackened shell. There
is a new house on the slope. Half the village is standing in a line between the well and a burning
roof. A chronicle, one button away, lists what happened in the last day, and any event in it can be
**rewound**: the strip jumps back to that moment, plays it, and returns to now.

The village is not yours. You cannot help it, build for it, or save it. You can only watch, and
later, read what happened. It can fail. It usually won't.

**The feeling to hit:** concern for the villagers, curiosity about the event. Both at once.

---

## 2. Design pillars & non-goals

### Pillars

1. **Zero input into the village.** Nothing the user does reaches the sim or the strip: no clicks
   on villagers, no decisions, no upgrades. The app itself has a small amount of input: the
   chronicle (open, scroll, play, close), the first-run setup, and a **settings menu** — sound,
   size and edge, the clock ratio, weather source, location, restart in a new valley. Settings
   change how the village is shown and how fast it runs, never what happens in it.
2. **The glance is the unit of design.** One second of attention. Everything is judged by what a
   glance can read: proof of life always, a small difference every half hour, a story a few times a
   day, a catastrophe rarely.
3. **Real sky, fast village.** Two clocks (§4). The sky and weather are real; the village runs
   about 170× faster. The seam between them is a feature, not a bug.
4. **The sim decides, the stage dramatises (§5.4).** Every event is resolved by the simulation,
   with its outcome, before anything is drawn. Animation is theatre about something that already
   happened. Nothing on screen is ever "in progress" in the sim.
5. **History is the product.** The village accumulates scars, rebuilds differently, drifts across
   the valley, and remembers through the chronicle and the annals. A hundred-year-old village should
   be legible as a hundred years of decisions.
6. **Static in the photograph, dynamic on top (§6).** Anything that doesn't move between Vista
   renders is Vista's — buildings, ruins, roads, terraces, torch *light*. Anything that moves is
   Hamlet's — people, smoke, fire, carts, wheels, bells. Hamlet's renderer is a small deferred
   compositor whose G-buffer is a path tracer's output.
7. **Stay out of the way.** CPU only in the steady state; Vista's GPU render is a minute every
   quarter hour, skipped if the GPU is busy. Sound off by default and rare. Never a notification.

### Non-goals

- **No player agency.** No clicking on villagers, no interventions, no "send help." Ever.
- **No idle-game economy.** No prestige, no currency, no numbers going up for the player.
- **No ages.** Medieval Europe only. Roman-to-late-medieval progression is noted as a later idea,
  not a design.
- **No genealogy as a goal.** Families exist because births and weddings produce them; nothing is
  built for the player to browse a family tree.
- **No real-time ray tracing.** Vista never runs faster than its cadence. Movement is never traced.
- **No multiple villages, no map.** One valley, one village, one camera.

---

## 3. User experience

### 3.1 First run

- **Location:** "exactly where I am" (lat/lon, used for the sky and, with Open-Meteo, the weather),
  a custom latitude/longitude, or a preset ("somewhere in the Alps", "near the sea", "at a lake in
  the hills"). **V1 ships one terrain type** (a valley with a river, §8.2); the presets are later.
  The location decides sun, season and climate; Vista's recipe + seed decides the terrain.
- **Weather source:** real (Open-Meteo, needs a network and your coordinates) or generated
  (offline, seeded, realistic-feeling, from Vista's climate normals). The sim never knows which.
- **Size:** 1×, 2× or 4× (§3.3). **Edge:** top or bottom.
- **Sound:** off by default.
- **Seed** (random, shown, copyable).

### 3.2 Steady state

The strip. No chrome except two small buttons at the top right (fade unless hovered): the
chronicle and the settings. Nothing in the strip itself responds to the mouse.

### 3.2a Settings

A small window: sound on/off, size (1×/2×/4×) and edge, the clock ratio, weather source and
location, seed display, and "restart in a new valley" (with a confirmation; the old annals are
kept and readable). Settings are shown-and-speed only (pillar 1): nothing in it influences what the
village does. A setting that changes the backdrop (size, location) triggers a Vista render.

### 3.3 Size

Three presets, chosen at setup. Height **120 / 240 / 480 px** at 1× / 2× / 4×; width is the
monitor's. Vista renders the backdrop at the preset's native pixels. The fast layer's sprites are
authored once at 1× and integer-scaled, so people are 5 px tall at 1× and 20 px at 4×. No custom
scaling, no resizing. Multi-monitor: one monitor, chosen at setup.

### 3.4 The chronicle

A button opens a separate, scrollable window. Two sections:

- **Recent** — the last ~24 real hours (about 24 village weeks). One row per headline or upheaval:
  village date, a headline in the village's voice, and a **play** button. Play **rewinds the strip
  itself** to that moment (the stored backdrop and sim snapshot), plays the event's vignette, then
  returns to now. The chronicle window stays open.
- **Annals** — everything older, condensed per village year into a few lines, like a parish
  register: *"Year 14: the great fire; the stone mill built; Old Maarten died; a hard winter."* No
  replay. Annals are written when a village year closes, from the headlines of that year, by rules
  about what the villagers would have remembered (deaths, buildings, disasters, weddings of
  notables), not what happened most.

Headlines are **hand-written templates** filled from event data: *"The baker's house burned on a
windless night."* Templates carry the ambiguity that makes a chronicle readable (*windless night*,
so not an accident?). An LLM writer is a later option, and a network dependency; not V1.

### 3.5 Sound

Off by default. When on, **diegetic and rare**:

- the bell: weddings, funerals, and a large fire;
- a distant hammer when a building goes up;
- nothing continuous, nothing ambient, nothing twice in an hour.

---

## 4. Time

### 4.1 Two clocks

| Clock | Runs at | Drives |
|---|---|---|
| **Sky** | real time | sun position, season (foliage, snow), weather, torches lighting at dusk |
| **Village** | ~170× real time: 1 real hour = 1 village week | births, deaths, building, trade, events |

Derived: a village year ≈ 2 real weeks, a generation ≈ 1 real year. The ratio is a setting, for
testing and for taste; the defaults above are the design.

### 4.2 The seam

The village lives at its own tempo under a real sky, like a fish tank. The sky leaks into the sim
a little, never the other way:

- real rain → this village week's crops do better, fire risk down;
- real snow / cold → roads slow, no carts, more firewood burned;
- a real dry spell → drought week, fire risk up;
- real night → the sim keeps running (§4.3); real dusk → torches light.

Nothing explains this. People will notice the bad fire came after a fortnight without rain outside
and feel clever.

### 4.3 Night and absence

The sim never slows when nobody is looking. While the machine is on, it never pauses for an
absence; while it is off, the absence is fast-forwarded on resume, up to 24 hours (§5.7).
**Upheavals are biased toward happening during the user's long absences** (overnight, detected
from input idleness over hours, not minutes; and the fast-forwarded stretch after a resume), so
the morning glance is the ritual: coffee, "what happened to the chapel", open the chronicle.
Absence is the interesting part, not dead time. The bias is on the *timing* of events the sim was
going to produce anyway, never on their *frequency*.

### 4.4 Cadence

Three tiers of visible change, with their own rhythms. These are the design's tuning targets.

| Tier | How often | What | Chronicle? |
|---|---|---|---|
| **Texture** | every 10–20 real minutes | a cart arrives, washing hung, a fence, the smith's chimney out because he's at the tavern, a new field ploughed | no |
| **Headlines** | a few per real day | a birth, a wedding, a funeral, a building raised, a stranger settles, a bad harvest | yes, with replay |
| **Upheavals** | every few real weeks, irregular | fire, flood, raid, plague, a cascade from a bad year | yes, with replay |

**Upheavals are earned, never rolled.** A fire on a windless night in a village with a drunk
smith and a dry well is a consequence; a fire because the dice said so is a jump scare. Every
upheaval has a cause findable in the chronicle.

---

## 5. The simulation

### 5.1 Scope of the model

A **needs model, not an economy game**: the village wants things, solves for them, and the
solutions are what you read. Enough economy to make the carts mean something (§5.3). Nothing is
tuned for balance in the game sense; it is tuned for *plausible histories*.

### 5.2 People and households

- **Everyone is a person record**, forever: name, birth, death, parents, spouse, household, role,
  alive. A century of village time is a few thousand records; data density is not a concern (a
  decision, 2026-10-21). Nothing is pruned from the person table; only the chronicle condenses.
- **Households are the unit the sim reasons about:** a household has a building, a trade, members
  and stores. Needs, work, moves and inheritance are household-level.
- **Names:** given names from a period list; **surnames emerge** from trade or place (Miller,
  Smit, van der Brug) and then inherit, so lineages are readable from the chronicle without a tree.
- **Notables:** a small rotating cast attached to roles (the miller, the priest, the smith, the
  troublemaker, the widow at the edge of the village). Events attach to notables; the chronicle
  writes about them by name; when one dies the role passes. Notables are the bridge between
  "population" (sturdy) and "people" (where stories live).
- **Life:** birth, childhood, marriage (within and, via carts, from outside), work, old age, death
  by age, accident, disease or disaster. Inheritance of buildings and trades.

### 5.3 Needs, trade and the carts

- **Needs:** food, water, shelter, fuel, light, faith, morale. Each is produced by trades (farmer,
  fisher, miller, smith, woodcutter, chandler, priest…) from the land (fields, river, forest) and
  stores.
- **The village has a character given by the land:** a river village fishes, a hill village
  grazes. What it cannot make, **it trades for**: the cart that arrives with candles because there
  is no beeswax here, and leaves with barrels of fish. Trade is the only connection to the
  off-screen world and the only source of strangers. One or two trade goods per village is enough;
  the point is that the cart's cargo is *legible* and *true*.
- **Scarcity has consequences, and the solution is the story.** The well fails → they dig a new one
  further west → the village centre drifts left over the years. No beeswax and the road snowed in →
  dark windows this winter → morale down → fewer weddings in spring.

### 5.4 Events: the sim decides, the stage dramatises

- The sim runs in village time, deterministic from (seed, weather feed), and **emits events with
  their outcomes attached**: *fire at the bakery, village week 412, villagers responded, 60%
  damage, no deaths, the wind was still.*
- The stage (§6) plays a **vignette** for the event in real time: a two-minute dramatisation of
  something the sim resolved in a tick. The bucket chain forms, the fire shrinks to 40% of the
  building, the smoke goes grey. The vignette simulates nothing; it is theatre. Chronicle replay is
  the same vignette played again from the stored event.
- **Vignettes are the unit of scope.** Five ship with V1 (§10) and the project is alive; each later
  one is an evening. An event without a vignette still happens, still changes the backdrop, still
  gets its chronicle line: a missing vignette degrades to a cut, never to a bug.
- **Distraction budget:** one vignette at a time; a big one pre-empts a small one ("wedding
  interrupted by fire" is itself a story); vignettes never overlap texture-tier changes in the same
  spot.

### 5.5 Destruction, decay and rebuilding

Without destruction the valley fills up and the strip dies. Three mechanisms, all producing
different villages afterwards, never resets:

- **Decay** is the quiet one and the important one: buildings age; an unused chapel loses its roof
  after the population drops; the east road grows over. Not noticed happening, noticed *having
  happened*.
- **Upheavals** (§4.4) remove things fast and in clusters.
- **Rebuilding is a decision, not a restore.** The mill burns → rebuilt in stone, or not rebuilt,
  or the plot becomes a shrine. Scars accumulate: a scorch mark that stays a month, a ruin that
  stays for good, a village that has drifted along the river over a century.

### 5.6 Failure

Villages can fail. **There is no clock on it**, and a lifespan shorter than a generation would be a
tuning failure. Failure is a spiral the sim can't climb out of: too few people to keep the well →
the well fails → people leave → the chapel is abandoned → the last household goes. Watched in the
chronicle over several real days, knowing, unable to help.

When the last building falls the strip goes quiet: a real day or two of empty, overgrown ruins
under the real sky. Then a cart with three people in it. **Same valley**, unless the user chooses
to restart in a new one; the old village's annals stay, and the newcomers dig things up.

### 5.7 Determinism and persistence

- The sim is a pure function of (seed, weather feed, time). The weather feed is recorded, so a
  village can be replayed from its log.
- **Snapshots:** the sim state is snapshotted at every headline and upheaval for the 24-hour
  window, together with the Vista backdrop current at that moment and the event's vignette
  parameters; that is what replay plays. Older snapshots are dropped when their year is written to
  the annals.
- Save on every village day. On resume the sky catches up to real time and **the village
  fast-forwards through the absence, capped at 24 real hours** (24 village weeks). The cap equals
  the chronicle window (§3.4), so everything that happened while you were away is exactly the
  recent list, with replay, and nothing has fallen into the annals unseen. An absence longer than
  the cap is village time that simply didn't happen. The fast-forward runs the sim on the recorded
  or generated weather feed for that stretch, counts as absence for the upheaval bias (§4.3), and
  takes under a second; the backdrop snapshots its replays need are rendered on demand when an
  event is played, which determinism allows. The tamagotchi draw — "I turn the PC on and see how
  my village is doing" — is the point.

---

## 6. Rendering

### 6.1 Three layers, three cadences

| Layer | Who | Runs on | Cadence |
|---|---|---|---|
| **Backdrop** | Vista | GPU | every ~15 real minutes, or on a weather/sky change, or when the village's static state changed; skipped while the GPU is busy |
| **Stage** | Hamlet | CPU | 30 fps when something is animating, idle otherwise |
| **Chronicle** | Hamlet | CPU | on demand |

The backdrop is a **still**, in Vista's sense: fixed camera, deterministic from (recipe, seed,
village static state, sky state). Vista's non-goal "no animation, no live wallpapers" is untouched;
Hamlet just asks for a new still now and then.

### 6.2 What is static and what is dynamic

**Static (Vista renders it):** terrain and its modifications (terraces, mill races, worn paths),
buildings in every state (new, weathered, burned shell, ruin, overgrown), walls, fences, bridges,
docks, fields in season, snow, and **torch and fire light on surfaces** (§6.4).

**Dynamic (Hamlet draws it):** people, animals, carts, smoke, flames, the waterwheel, washing,
the bell swinging, weather particles (rain, snow), and the flicker of torch light.

**The handover:** a house that burns at 14:02 shows flames and smoke on the stage until the 14:15
backdrop lands with the blackened shell and its shadow; the stage's fire multiplier (§6.4) is
already at zero by then. The seam is invisible because the stage's fire fades out as the backdrop's
damage fades in.

### 6.3 The stage as a deferred compositor

The stage is a small deferred renderer whose G-buffer is Vista's output (§8.1). Per frame:

1. Start from the **base backdrop** (sky-lit + sun-lit, no emitters).
2. Add each **emitter buffer** × its multiplier *k(t)* (§6.4).
3. **Draw dynamic sprites** with a per-pixel **depth test** against Vista's depth buffer, so a cart
   goes behind a spur of rock and a villager behind a wall at any distance up the valley. Sprites
   are **lit** from Vista's sun direction, sky colour and the emitter positions (a villager between
   two torches is lit from both sides).
4. **Shadows of dynamic objects onto the static scene** (§6.5).
5. Weather particles, the chronicle button, done.

### 6.4 Emitter buffers: flicker and waver

Vista renders the backdrop as separable light contributions (§8.1), one additive buffer per
emitter or emitter cluster: the torch's glow on the wall, the orange on the cliff behind the
chapel. Linear light adds, so the stage composites `base + Σ emitter_i × k_i(t)`.

- **Flicker:** *k_i(t)* is a per-emitter noise function. A dimmed light is exactly a scaled
  contribution, so this is correct, not a cheat. The same *k* drives the flame sprite, so the wall
  brightens when the flame does.
- **Waver:** the nearest emitters are rendered **two or three times at slightly different
  positions**; the stage blends between them with drifting weights, so the hotspot moves and the
  wall's shadows swing. Far emitters get one flicker buffer per cluster, with phase offsets in the
  sprites only.
- **Colour wobble** in display space: warmer when *k* is high, cooler when it dips.
- **Fire** is the same mechanism with a bigger emitter and louder noise, decaying over the vignette
  as the bucket chain works, so the orange on the neighbouring houses fades as the fire dies.
- Each buffer is non-zero in a small region; memory is trivial at strip resolution; the extra render
  cost is one light sample per buffer per pixel-sample, inside the 15-minute cadence.

(Quake, 1996, did this with lightmaps and light styles. The justification is new; the trick is
not.)

### 6.5 Dynamic shadows: a very small ray tracer

Dynamic objects shadow the static scene, cheaply, because everything expensive is baked:

- Vista's depth + camera give the **world position of every visible surface pixel**.
- The only occluders are **proxies** for dynamic objects: a villager is a capsule, a cart a box,
  a cow a fatter capsule.
- For each pixel in an emitter's lit region (a few thousand at strip resolution): cast a ray from
  the surface point toward the emitter, test against the capsules, and multiply that emitter's
  buffer at that pixel toward zero on a hit. Jitter the emitter over three or four samples for a
  soft edge. Microseconds per frame.
- **Daylight, same trick:** Vista outputs the **direct-sun contribution as its own buffer**;
  villagers cast sun shadows on the ground that lengthen at golden hour. The sky buffer is left
  alone (sky occlusion by a person is negligible).
- The result is small shadowy blobs, which is what a 5-px capsule should throw.

**Accepted limits:** dynamic objects don't shadow each other accurately, don't bounce light onto
walls, and don't shadow surfaces the camera can't see. All invisible at this scale.

### 6.6 Scale and look

The model-railway permission slip: people are 5 px tall at 1× (HO-gauge against a photographic
backdrop), the waterwheel turns, a cart does its loop, nobody asks why. The buildings are *in* the
photograph; only the little people are on top. The palette and lighting of sprites follow Vista's
sun and sky so the two layers share a light.

---

## 7. Weather and sky

- **Sky:** Vista's astronomy (M1.2) at the chosen location, real time: sun position, sunrise and
  sunset, season via date, moon and stars once Vista's M6 exists.
- **Real weather:** Open-Meteo forecast API at the user's coordinates — no key, free for
  non-commercial use, data under CC BY 4.0, credited with a link in the chronicle window's footer.
  One call per backdrop cadence. A network failure falls back to the generator for that tick.
- **Generated weather:** seeded, offline, from Vista's hand-entered climate normals (M1.11): a daily
  temperature curve, day-to-day persistence with drift, a weather-type Markov chain per season,
  fog on cold clear mornings near water. Realistic-feeling is the bar, not accurate.
- Both produce the **same small struct** (temperature, cloud cover, precipitation type and amount,
  wind, fog, humidity); Vista's weather chain (§3.6 in `vista-plan.md`) consumes it, the sim reads
  the few fields it cares about (§4.2).

---

## 8. The Vista contract

Hamlet is a client of Vista. Everything below is a *data* interface; Vista gains no behaviour.

### 8.1 Vista exports, per backdrop

- Beauty, split into additive buffers: **sky-lit**, **direct-sun**, and **one per emitter** (or
  emitter cluster), each denoised separately; nearest emitters at 2–3 jittered positions.
- **Depth**, **normal**, **material id** (the auto camera's G-buffer, M1.14, at strip resolution).
- The camera (so depth → world position), the sun direction, a sky colour, the exposure used
  (so the stage can light sprites in the same units and the same display curve, AgX).
- Resolution: the strip's native pixels (§3.3). Cost: ~2.5× the 320-px preview at 1×; well under a
  minute at full quality, less with lower spp + OIDN.

### 8.2 Vista imports, per backdrop

- The **prop instance list with states**: `(prop type, state, position, orientation, scale)` for
  every static thing the village owns — M8's cabins, docks, paths, fences, plus Hamlet's additions
  (mill, chapel, well, ruin states, scorch decals, fields by crop and season).
- A **terrain-modification layer**: terraces, mill races, worn paths, a small height/material
  edit over the eroded grid near the village. Deterministic, part of the village's static state.
- The **emitter list** (torches, hearths, fires, windows at night) with positions and intensities.
- The weather struct (§7), the time and the location.
- The camera, which **never moves** for a village's lifetime (a restart may pick a new one).

### 8.3 What Vista needs to know now

Two notes, nothing more, until Vista's M1 is signed off:

1. **M8 (props):** the shot-first layout should place props **from a prop-instance list** that can
   come from a file as well as from the recipe rules, and props should have **states**. That is
   the entire bridge; it costs M8 nothing to design in.
2. **M8 / M3 (layout):** a "village" set piece reserves a **buildable stage** near the camera —
   a flattish valley floor, the river leaving the backdrop — and the recipe plants the viewpoint
   looking across it.

Separable light buffers (§8.1) are an AOV bookkeeping change on an integrator that already samples
sun and sky separately (M1.5); the stage's needs are a reason to keep that separation.

### 8.4 Without Vista

For development and testing only: a **flat-shaded stand-in backdrop** (a height field rasterised
with sun/sky lighting, writing the same buffers in the same format) so the sim, the stage and the
chronicle can be built and unit-tested without the GPU, in CI, and before the Vista contract
exists. Not shipped.

---

## 9. Working conventions

Inherited from Vista where they fit:

1. **Pure libraries, tested in CI.** `Hamlet.Sim` (village model, events, chronicle, annals) and
   `Hamlet.Stage` (compositor, sprites, shadows, vignettes) are plain `net10.0` with unit tests and
   no GPU. `Hamlet.App` is the WinForms shell: the layered always-on-top window, the timer, the
   chronicle window, sound, Open-Meteo, and the Vista client. No game engine (decision, 2026-10-21).
2. **Determinism.** Seed + recorded weather feed → the same village. Goldens for the stage at 1×
   with the stand-in backdrop.
3. **Vignettes are data plus a small script**, never sim logic. The sim's event record must contain
   everything a vignette needs; a vignette never computes an outcome.
4. **The chronicle is written for a reader.** Templates in the village's voice; no event names,
   no numbers unless a villager would say them.
5. **Measure the glance.** Each tier's cadence (§4.4) is checked on a logged real week: how many
   visible changes per hour, how many headlines per day, how long between upheavals.
6. **Don't move Vista.** Vista changes are data contracts (§8) and the M8 note; never behaviour.
7. **Slices land directly on `main`, then stop for review**, each ending with a log, as in Vista.

---

## 10. Roadmap

Each milestone: **goal → deliverable → verification.** Nothing is scheduled before Vista M1.17.
Milestones H0–H3 need no Vista at all (the stand-in backdrop) and could be built in cloud sessions.

**H0 — Skeleton.**
Goal: the window, the clocks, the stand-in backdrop. Deliverable: `Hamlet.Sim` / `Hamlet.Stage` /
`Hamlet.App` in the solution; the layered always-on-top window at 1×/2×/4×, top or bottom; Vista's
astronomy for the real sky over a flat-shaded stand-in valley with a day/night cycle; the two-clock
model with the ratio as a setting; save, and resume with the capped fast-forward (§5.7). Verify:
the window survives a day; the sky matches the real sun; a resume after 3 hours off advances the
village 3 village weeks, a resume after 3 days off advances it 24.

**H1 — Proof of life.**
Goal: a glance always shows something alive. Deliverable: the stage compositor over the stand-in
G-buffer (depth test, sun-lit sprites, sun shadows of capsules); ambient animations (smoke, the
waterwheel, washing, a cart loop, chickens); texture-tier events from a stub sim on the 10–20
minute cadence. Verify: a logged real day shows a texture change at least every 20 minutes; sprites
occlude correctly behind stand-in terrain.

**H2 — The village.**
Goal: a village that lives and dies for reasons. Deliverable: people, households, surnames,
notables, life events; needs and trades; one or two trade goods and the carts; decay; rebuilding
as a decision; failure spiral and resettlement; the first upheaval (**fire**, with cause: dry
spell, wind, a hearth, a drunk smith). Headless accelerated runs (a village century in a minute)
with statistics: population curves, lifespan distributions, how often villages fail. Verify: on a
hundred seeded centuries, median village lifespan ≥ several generations, failures possible, no
seed runs away to infinity; every fire has a findable cause in its event record.

**H3 — Chronicle and replay.**
Goal: the unique selling point. Deliverable: snapshots at every headline and upheaval; the 24-hour
recent list with play (rewind the strip, play, return); annals written at year end by rules;
hand-written templates for every V1 event. The five V1 vignettes: **fire with bucket chain**,
**wedding with bells**, **funeral**, **building raised**, **cart arrives / departs**. Distraction
budget and pre-emption. Verify: replay of any event in the window renders bit-identically to its
live playing; annals read as a parish register on a sample century.

**H4 — Vista backdrop.**
Goal: the photograph. Deliverable: the Vista contract (§8): the exports as AOVs, the imports as
files, the 15-minute cadence with GPU-busy skip, the village's static state (props with states,
terrain mods) rendered by Vista; the stand-in retired to tests. Needs Vista M8 (props) and the
light-buffer AOVs. Verify: a burned house hands over from stage flames to backdrop shell with no
visible seam; a 1× backdrop renders in under a minute at the chosen quality; Vista's own goldens
and self-tests unmoved.

**H5 — Light.**
Goal: torches that live. Deliverable: emitter buffers with flicker, waver and colour wobble;
fire as an emitter; dynamic shadows from torches (§6.5); windows glowing at night; the whole thing
under Vista's night (M6) when it exists. Verify: a villager walking past a torch darkens the wall
behind them; the fire's light on the neighbours decays with the vignette.

**H6 — Weather and place.**
Goal: the real outside. Deliverable: Open-Meteo with the credit line; the offline generator from
climate normals; the sky leaking into the sim (§4.2); overnight bias of upheavals (§4.3); first-run
setup with "exactly where I am" and custom coordinates. Verify: both weather sources produce the
same struct; the sim is deterministic given the recorded feed; a week of real weather visibly
shows in the strip.

**H7 — Sound.**
Goal: the bell. Deliverable: off-by-default sound; bell for weddings, funerals and large fires;
the hammer. Verify: never more than one sound an hour in a logged week.

**H8 — More village.**
More upheavals (flood from the river, plague, a raid, a hard winter cascade), more vignettes, more
trades and goods, the location presets (Alps, coast, lake, waterfall) as Vista's set pieces allow.
Each an evening or a weekend; never blocking.

**Later ideas, not planned:** ages (Roman → late medieval); an LLM chronicle writer; a village
that reacts faintly to the user's own activity (the forge smokes when the keyboard is busy — risky,
see §11).

---

## 11. Open questions & risks

- **The seam between clocks** (§4.2) is a bet. If a harvest happening on a real October evening
  reads as wrong rather than charming, the fallback is to align the village's *season* to the
  real one and keep only the social clock fast. Decide after a real week in H1.
- **Tuning plausible histories** (H2) is the long tail. Needs models produce either stability
  (boring) or oscillation (silly); the sweet spot is found with headless centuries and statistics,
  not by watching the strip.
- **Vignettes versus feature creep.** The unit-of-scope rule (§5.4) only works if every event
  degrades gracefully without one. Test that path first.
- **The chronicle's voice.** Templates will show their seams after a month. Budget for a second
  pass at the templates once there is a real month of headlines to read.
- **Vista dependencies:** H4 needs Vista M8 and the light-buffer AOVs; H5's night needs M6. If
  Vista stalls, H0–H3 still ship on the stand-in, which is why the stand-in exists.
- **GPU contention with Vista look-dev.** The backdrop must yield to any GPU use, not only to
  games; "skip the tick, keep the old sky" has to be the default behaviour, with the stale backdrop
  age shown nowhere but the log.
- **Overnight bias** (§4.3) depends on detecting absence from idleness. Keep it coarse (hours) so
  a lunch break never counts, and never let it change event frequency.
- **Reacting to the user** is tempting and dangerous: it risks becoming a productivity tracker in
  a costume. Not planned; if ever tried, one effect, subtle, off by default.
- **Vlaardingen is flat.** "Exactly where I am" for a Dutch user produces marsh and polder, which
  Vista's terrain pipeline does not do in M1. The honest V1 answer is a preset valley with
  Vlaardingen's sky; a polder set piece is a Vista question.

---

## Appendix — decisions from the brainstorm (2026-10-21)

| Topic | Decision |
|---|---|
| What it is | An ambient village at the screen edge, glanced at while working; not an idle game. Zero input *into the village*: the only input is the chronicle, first-run setup and a settings menu (sound, size, clock ratio, weather, location, restart), none of which touch the sim |
| Window | Always-on-top borderless window docked to the top or bottom edge; 1× / 2× / 4× height presets, native width; no custom scaling |
| Stack | C#, WinForms shell, own CPU compositor; no game engine — it would give nothing Vista or the compositor doesn't already need to own |
| Setting | Generic medieval Europe; ages are a later idea, not a feature |
| People | Full person records forever (cheap); households as the sim's unit; emergent surnames; a rotating cast of named notables; genealogy is a by-product, never a goal |
| Failure | Possible, no clock, rare; a lifespan under a generation is a tuning bug; same valley on resettlement unless the user restarts |
| Economy | A needs model with one or two legible trade goods; the carts' cargo must be true |
| Clocks | Real sky, 1 real hour = 1 village week (a setting); the sky leaks into the sim, never the reverse |
| Absence | On resume the village fast-forwards through the time the machine was off, capped at 24 real hours = the chronicle window, so nothing is missed unseen; longer absences are simply lost time |
| Weather | Open-Meteo (no key, non-commercial, CC BY credit) or an offline seeded generator from Vista's climate normals; same struct either way |
| Location | First-run setup: "exactly where I am", custom coordinates, or presets; V1 ships one terrain type |
| Chronicle | A button, top right, opens a scrollable window: 24-hour recent list with play (rewinds the strip), older years condensed into annals, no replay; hand-written templates |
| Rendering | Static in Vista's still (buildings, ruins, terrain mods, emitter light as separable buffers); dynamic on a CPU deferred compositor using Vista's depth/normal/light AOVs; torch flicker and waver by buffer multipliers and position blends; dynamic shadows by shadow rays against capsule proxies |
| Vista | Untouched: a data contract (§8) and one note for M8 (props from a list, with states; a buildable stage) |
| Stand-in backdrop | Yes, for development and tests; never shipped |
| V1 minimum | Ambient animations, texture events, one upheaval (fire), chronicle with replay — replay is the USP and is in V1 |
| Sound | Off by default; bell for weddings, funerals, large fires; a hammer for building; sparse and meaningful |
| Name | "Hamlet", working name |
| Order | After Vista; H0–H3 need no Vista and no GPU |
