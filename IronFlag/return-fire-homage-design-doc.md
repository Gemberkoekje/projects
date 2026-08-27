# Return Fire Homage — Design Document & Implementation Plan (v0.1)

**Engine:** Unity (URP)
**Multiplayer target:** 2-player local split-screen
**v0.1 scope:** Core loop — 1 map, 4 core vehicles

---

## 1. Vision & Pillars

A tight, split-screen vehicular capture-the-flag game where only the fastest, weakest vehicle can carry the flag — forcing every match into a scout → clear → dash rhythm instead of a straight race.

**Pillars**
- Only the jeep carries the flag. Everything else exists to clear its path or stop the enemy's.
- Everything is destructible. Buildings, cover, and bridges are part of the tactical puzzle.
- Four vehicles, deep roles — not a big roster diluted thin. Rock-paper-scissors, not a shopping list.
- Readable at a glance — silhouette, team color, and minimap tell you everything mid-chaos.
- Local split-screen chaos is the experience being designed for, not an afterthought.

---

## 2. Core Loop

1. Player selects a vehicle from their bunker.
2. Vehicle deploys to the field (lift for ground vehicles, launch pad for the helicopter).
3. Player scouts, fights, clears defenses, or defends their own base.
4. Jeep locates and grabs the enemy flag from its tower.
5. Jeep returns the flag to the home bunker → **win**.
6. If a vehicle is destroyed, its pilot returns to the bunker to select again.

**Win conditions (v0.1)**
- Primary: return the enemy flag to your bunker.
- Secondary: destroy all the enemy vehicles that could ever have done that. *Done — see
  [RESERVES_NOTES.md](RESERVES_NOTES.md). It needed the finite vehicle roster M6 said it
  needed: a side now gets a fixed allotment per level (8 jeeps, 3 tanks, 3 ASVs, 3
  helicopters by default), a destroyed vehicle never comes back, and losing the last jeep
  loses the match because only a jeep can carry a flag. The base structures half of the
  original wording is still not a win condition — the bunker cannot be destroyed.*

---

## 3. Vehicle Roster (Core Four)

| Vehicle | Speed | Armor | Weapon | Special | Flag |
|---|---|---|---|---|---|
| **Jeep** | Fastest | Very low (1–2 hits); the shipped tuning gives the Helicopter a lower pool still | Grenades | Amphibious (stretch) | **Only vehicle that can carry the flag** |
| **Tank** | Medium | Medium | Rotating turret cannon | General-purpose damage dealer | Can find/reveal flag, can't carry it |
| **ASV** (Armored Support Vehicle) | Slowest | Highest | Rockets | Mine-laying (not implemented — stretch goal) | Same |
| **Helicopter** | Fast | Fragile | Missiles/bullets | Ignores ground terrain; must return to bunker/pad to refuel/rearm | Same |

---

## 4. Map Design (v0.1 — single map)

- Two mirrored bases at opposite ends of one island-style map.
- One real flag tower + one decoy tower — tests the "confirm before committing" mechanic from the original.
- One fuel depot + one ammo depot per side, both destroyable/contestable.
- Terrain mix: open ground (tank/ASV routes), a channel crossing with an indestructible causeway
  plus two destructible flanking bridges (see [M7_NOTES.md](M7_NOTES.md) for why it's three
  crossings and not one), and scattered cover (buildings/trees) for line-of-sight breaks and
  destruction spectacle.

---

## 5. Systems

### Resource economy
- Each vehicle spawns with fixed fuel/ammo pools that deplete with use and time.
- Refuel/rearm at the bunker or a friendly depot.
- Empty fuel strands the vehicle — it can still fight in place, or self-destruct to redeploy immediately (a genuine tension mechanic worth keeping from the original).

### Damage & destruction (v0.1 approach)
- **State-swap destruction**: intact → damaged → destroyed mesh per structure, with a debris VFX burst on each transition. Cheap, readable, and tractable for AI-assisted implementation.
- Voxel/physics-based destruction (Teardown-style) is explicitly deferred — revisit only once the core loop is validated as fun.
- Vehicles use a simple HP pool; destroyed = explosion VFX + pilot returns to bunker.

### Bunker / vehicle selection
- Per-player HUD panel (each split-screen half) shows available vehicle counts and fuel/ammo
  status. A small radar/minimap is planned for M8 and not yet built — see `README.md`'s
  milestone table.
- Selecting a vehicle triggers a spawn beat — lift animation for ground vehicles, launch pad for the helicopter — as a deliberate pacing moment, not just a menu.

---

## 6. Local Split-Screen Multiplayer

- v0.1 target: 2 players, horizontal split-screen, independent cameras.
- Input: Unity's Input System package — keyboard/mouse for P1, gamepad for P2 (or dual gamepad), cleanly handling per-player device binding.
- Camera: fixed-angle top-down per viewport, each following its player's currently active vehicle.

---

## 7. Art & Tech Spec

- **Engine:** Unity, URP.
- **Style:** stylized low/mid-poly, PBR materials, real-time dynamic lighting — deliberately not photoreal, to keep the art pipeline tractable for a small/AI-assisted team.
- **Readability rules:** team-color accents (flags/decals) on every vehicle; visually distinct silhouette per vehicle type.
- **Perf target:** stable 60fps in split-screen on mid-range PC hardware (revisit once content scope grows).
- **Asset generation:** see `return-fire-homage-asset-spec.md` for the per-asset dimension/naming/palette spec used when prompting Claude Code for any vehicle, structure, or prop.

---

## 8. MVP Milestone / Task Breakdown

Sized to hand off individually as implementation specs (similar to the per-step LLM assignment approach used on By Galactic Accord).

| # | Milestone | Description |
|---|---|---|
| M0 | Project setup | Unity project, URP config, folder structure, source control |
| M1 | Vehicle movement & camera | Basic controllers for all 4 vehicles + per-viewport top-down camera |
| M2 | Split-screen input | 2-player local input routing via Input System, viewport setup |
| M3 | Combat basics | Weapons, projectiles, vehicle HP, death → return-to-bunker flow |
| M4 | Bunker & vehicle selection | Bunker UI, spawn/switch flow, fuel/ammo tracking |
| M5 | Destruction (state-swap) | Destructible props/buildings: 3-state model swap + debris VFX |
| M6 | Flag & win conditions | Flag tower, jeep-only pickup rule, capture/return win check |
| M7 | Greybox map | Build the v0.1 map: bases, depots, terrain variety, real + decoy tower |
| M8 | Polish pass | Per-vehicle music/SFX hook, minimap, HUD readability, juice (screen shake, particles) |

---

## 9. Deferred Scope

- **Voxel/physics destruction** — only after the state-swap version proves the loop is fun.
- **Post-MVP:** online multiplayer, additional vehicles (jets, PT boats), additional maps/biomes.

---

## 10. Near-Term Backlog (not yet scheduled to a milestone)

Real work the project owes, captured here so it survives between sessions. Not sized or
sequenced yet.

**The backlog is currently empty.** Every item it carried was done in M9 — see
[M9_NOTES.md](M9_NOTES.md) for what each one turned into and what it cost. For the record,
they were: the helicopter's fixed flight altitude, automated turrets, a destroyed structure
losing its collider, and the audit that every `StructureKind` really is destructible end to
end. The first of those was the one with a thread to pull: locking the altitude meant
deciding what "home" means for an aircraft that can no longer land, and the answer is that a
helicopter is served hovering over its own bunker — `SupplyPoint.ServesAircraft` is what
still keeps it out of the field depots, where the height gate used to.

Add the next thing here rather than to a milestone, and read
[M9_NOTES.md](M9_NOTES.md)'s "What the next phase inherits" before starting it.
