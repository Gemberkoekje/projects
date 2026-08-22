# Return Fire Homage — Asset Spec Sheet (v0.1)

Reference this file directly when prompting Claude Code / Blender MCP for any asset — point at it instead of re-describing the rules each time.

---

## Global Rules (every asset, no exceptions)

- **Construction:** primitives only — box, cylinder, wedge/prism, cone, pyramid. No organic sculpting, no subdivision-surface modeling.
- **Shading:** flat-shaded / vertex-color materials. No texture maps, no UV unwrapping.
- **Material groups:** colour is vertex colour on one material by default. Use a separate material *only* where Unity must change something a vertex colour cannot express — team accent, head/tail lights (emission), and weapon muzzles. See `blender/README.md` for the group list and how they are authored.
- **Unit scale:** 1 Blender unit = 1 meter.
- **Before export:** apply all transforms (Ctrl+A → All Transforms) so scale/rotation aren't baked into the object's transform channel.
- **Axis:** Blender is Z-up; the glTF exporter handles Y-up conversion for Unity automatically — don't manually re-orient.
- **Complexity ceiling:** if a shape needs more than ~10–15 primitives to read clearly, simplify the design rather than adding detail.
- **Naming:** `RF_<Category>_<Name>_<State>` — e.g. `RF_Vehicle_Jeep`, `RF_Structure_FlagTower_Damaged`, `RF_Prop_Bridge_Destroyed`.
- **Export:** one `.glb` per asset (or per destruction-state variant), collection name matching the object name.

Dimensions below are gameplay-scaled for silhouette readability, not real-world accuracy — treat them as relative proportions to hold consistent, not measurements to research.

---

## Color Palette (fixed — reuse everywhere)

- **Team Green** accent (matches the original's factions)
- **Team Brown** accent
- **Neutral/map-furniture base:** greys/tans, no team tint
- **Damage tint:** charred dark grey, consistent across every destructible's `_Damaged`/`_Destroyed` states

---

## Destruction States

Every destructible **structure/prop** (not vehicles — those use an HP pool + explosion per the design doc) needs separate meshes, not a modifier or hidden-state trick:

- `_Intact`
- `_Damaged`
- `_Destroyed`

Unity swaps between these directly per the state-swap destruction system in the design doc (§5).

---

## Vehicles (4 core)

Always prompt against this table, not in isolation — each vehicle's silhouette is defined by contrast with the other three.

| Vehicle | Length | Width | Height | Silhouette feature | Separate/moving mesh |
|---|---|---|---|---|---|
| Jeep | 4.0m | 1.8m | 1.6m | Lowest, most open-frame | Wheels (4) |
| Tank | 5.5m | 3.2m | 2.4m | Widest, tallest and longest hull; rotating turret | Turret |
| ASV | 4.8m | 2.8m | 2.3m | Most armored-looking, but compact and low-slung | Rocket launcher (rotate/elevate) |
| Helicopter | 5.25m airframe (5.65m incl. rotor) | 1.35m fuselage, 4.4m rotor span | 2.8m incl. rotor | Only airborne silhouette; slim fuselage under a wide rotor disc | Main rotor + tail rotor |

**Revised after reviewing the built models in-editor.** Two rows moved:

- **ASV** was 5.0 x 3.0 x 2.6m, which made it taller and bulkier than the tank. In
  the editor it read as the heavy of the roster, and the high launcher pedestal made
  it look like an anti-air platform rather than a support vehicle. It is now smaller
  than the tank in every dimension with the launcher recessed into the deck. "Most
  armored-looking" is carried by sloped armor and six road wheels, not by size.
- **Helicopter** width was 2.0m, which forced a rotor no wider than the fuselage and
  looked stubby. The "widest hull" contract belongs to the tank and a rotor disc is
  not a hull, so the two figures are now separate: the fuselage stays the narrowest
  in the roster, and the rotor spans 4.4m. A wide disc is also the clearest "this one
  flies" cue from directly above, which is the angle the game is played at.

**Weapon rule (added with M3):** every vehicle carries a `muzzle` group, because that
object is where Unity spawns its rounds, not only where VFX hang. On the tank and the
ASV it is a child of the traversing part; the jeep gained a cowl-mounted grenade
launcher and the helicopter a chin gun, both fixed forward, and both fitted inside the
dimensions above rather than extending them.

**Team-color rule:** accent color goes on flag decals/trim only — hull stays neutral so team identity doesn't require recoloring the whole model.

---

## Structures & Props

**Bunker (per team)**
- Houses the vehicle-select lift (ground vehicles) + a separate helipad
- Team-color trim on entrance/roof accents
- ~10m × 10m footprint — needs to read as "home base" at gameplay camera distance
- No destruction states (it's the win-condition target, not a combat prop)

**Deploy-point rule (added with M4):** the lift platform and the helipad are exported as
their own child objects named `LiftPlatform` and `Helipad`, each with its origin on the
surface a vehicle stands on. Unity deploys vehicles from those origins, exactly as it fires
rounds from the `Muzzle` object — so moving the lift or the pad is an art change and
nothing else. Renaming either one silently drops the bunker back to a guess at where its
own door is.

**Flag Tower (real + decoy — must be visually identical to each other)**
- Pyramidal silhouette — distinct shape from everything else so it reads instantly on minimap and battlefield
- Neutral color only, no team tint (decoys must be indistinguishable from the real tower)
- Needs `_Intact` / `_Damaged` / `_Destroyed`, and **all three are used** — see the tower rule below
- Carries a bare flag **pole** on the apex, which is decoration and misdirection: the flag
  never flies from it. The flag itself is not part of this mesh — see the flag rule below.
- Every state carries a **`FlagMount`** object carrying the position *and rotation* a flag
  stands at, kept out of the join like the bunker's deploy points. **It is identical in all
  three states** — on the plinth, inside the tower — so breaking a tower open reveals the
  flag rather than moving it. It is offset by half the banner's length so the *banner* is
  centred on the tower rather than the staff, and turned so the banner presents its broad
  face rather than its edge: a flag seen edge-on is a two-pixel line at gameplay distance.
- The **damaged** state has its **top taken off**, because the camera looks down at 58° and a
  hole in the roof shows far more than a window in a wall. A solid lower course survives to
  1.75 m — above a tank, so the tower is a box to look into rather than a doorway to drive
  into — and the four corners stand above it.
- Those corners are laid as **stacked courses of the same stone as the wall**. The tower's
  interior is a straight vertical shaft (1.6 m across, so the 1.45 m banner stands clear of
  the masonry) and only the *outer* face steps inward, once per course, following the intact
  tower's own taper. That is how a tapered masonry building is actually laid, and it is what
  stops a corner reading as a rectangle tacked onto a slope — a tilted box slopes on the
  inside too.
- Make the break **ragged**: one corner a course shorter than the others, and individual
  stones left at the shear — some jutting off a side, one still hanging under where the
  course above it went. Four clean stumps of matching height read as a design; a building
  does not come apart evenly.

**Flag rule (added with M6):** the flag is its own asset, `RF_Prop_Flag`, and never geometry
on a tower. It is a gameplay object with three homes — the pole on its tower, the ground where
its carrier was killed, and a mast above whichever jeep is carrying it — so its origin is at
the **foot of the staff**, and it is built facing the way vehicles are so a carried one trails
behind the jeep. The banner is the only `TeamTrim` on it; the staff stays neutral.

A tower with a flag modelled into it could not have the flag taken off it, and a decoy with a
visible difference from the real tower is not a decoy. Both are the same rule: nothing about
which tower is real may be in the art.

**Tower rule (revised after M7):** all three tower states are shown, and the difference
between them is the game's central mechanic rather than damage feedback. An **intact** tower
looks identical whether or not it holds the flag; a **damaged or destroyed** one shows what it
was holding; and a jeep may only take a flag off a tower that has been broken open.

The art brief that follows from it: **the tower is a container, not a plinth.** The flag stands
inside on its base in every state, and what changes is how much masonry is in the way — sealed,
then breached between the corners, then a stump. That is what makes rule one true *physically*
rather than by a renderer switch, and it is why the flag does not jump when the first shell
lands. The damaged and destroyed states also have to be unmistakably *open* at a glance, because
a player has to be able to look at four pyramids and tell which two they have already checked.

The bunker is now the only structure on the map that cannot be shot at all.

**Bridge**
- Simple beam/plank construction spanning the water crossing
- `_Intact` / `_Destroyed` only — bridges are binary (crossable or not), no partial-damage state needed
- **The origin is the riverbed, not the deck.** The model stands on its piers with the deck
  1.2 m above the origin, so a level places a bridge sunk by that much and its deck comes out
  flush with the banks it joins. A bridge dropped at ground level is a metre-high step that
  reads as a perfectly good bridge and is a wall. `LevelLoadingTests` drives a tank onto one
  to check it, because nothing in a level file can.
- The deck is 5 m wide between rails 0.55 m high, which is deliberately tight against the
  tank at 3.19 m: a bridge is a bottleneck as well as a route.

**Depots (fuel + ammo)**
- Small, visually distinct from each other at a glance — e.g. fuel = cylindrical tank, ammo = crate/box
- Neutral color, no team tint (contestable by both sides)
- `_Intact` / `_Damaged` / `_Destroyed`

**Generic cover (buildings, trees)**
- 1–2 simple building shapes + 1 tree/vegetation prop, reusable across the map for terrain variety
- `_Intact` / `_Damaged` / `_Destroyed`

---

## Prompting Pattern

Reference the file, don't restate the rules:

> "Using the spec in return-fire-homage-asset-spec.md, generate `RF_Structure_FlagTower_Intact` — pyramidal, neutral color, ~6m tall."
