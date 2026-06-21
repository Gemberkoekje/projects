# Skyseed — Throw Mode System Plan

This document covers the planned dual throw-mode system for the Skyseed item. See [README.md](README.md) for general architecture.

---

## Status — implemented (2026-06-21)

Built per this plan (Option A). Chosen values:

- **Keybind:** `V` (`key.skyseed.toggle_throw_mode`, category "Skyseed"), remappable. Toggles Classic ↔ Precise; an actionbar message confirms ("Throw mode: Classic/Precise"). Mode persists in the client config (`config/skyseed-client.toml`, `preciseThrowMode`).
- **On release**, the client sends a `ThrowSeedPayload` (mode, held ticks, target, hand) to the server, which validates and spawns the seed (single path for both modes). The server clamps a Precise target to `MAX_DISTANCE` (anti-cheat).
- **Precise distances:** `MIN_DISTANCE = 5`, `MAX_DISTANCE = 40` blocks (charge-scaled). The seed is lobbed with a gravity-corrected initial velocity (capped at 4.0 so near-vertical throws don't fling it) and **germinates exactly at the target** — it ignores collisions in flight and snaps to the target on germination, so the arc is purely visual. Overlap safety (nudge/fizzle) still runs at the target.
- **Classic** is unchanged (charged arc, germinate where physics leaves it).
- **Not done:** the charge/distance HUD readout (the existing arming sparkle is the only indicator for now). Vertical extreme angles are handled by the velocity cap rather than a hard look-angle clamp.

Server-side germination-at-target verified over RCON; the keybind/packet round-trip is client-driven (verify in-game).

---

## Current system (Default mode)

- Right-click and hold charges throw power.
- On release, the seed is launched as a projectile with velocity proportional to hold duration.
- The projectile follows gravity (arc trajectory).
- After 2 seconds of flight, the projectile germinates at its current position — wherever physics happened to carry it.

**Consequence:** placement is indirect. The player aims and charges, but the actual germination point is a product of velocity + gravity + 2 seconds of flight. Close placement is difficult because a weak throw drops the seed nearly straight down.

---

## Proposed addition — Alternate mode

Placement is **direct and predictable**. The germination point is computed at the moment of release from two inputs only:

- **Direction:** the player's look vector at release time.
- **Distance:** proportional to hold duration, clamped between a minimum and maximum.

The seed still flies through the air and still germinates after 2 seconds — but the flight path is computed *backwards* from the predetermined germination point, so the projectile always arrives at exactly that point at exactly 2 seconds regardless of gravity. The throw animation is purely visual.

**Consequence:** the player has precise, readable control. Short hold = island appears close along the look vector. Long hold = island appears far. No gravity compensation needed.

---

## The keybind

A dedicated keybind toggles between Default and Alternate mode. It is:

- **Persistent across sessions** — stored in client config, not per-item NBT. The player sets their preferred mode once; it stays until they change it.
- **Not per-seed** — both modes work with every seed type. The mode is a player preference, not a seed property.
- **Visual feedback on toggle** — a short actionbar message ("Throw mode: Precise" / "Throw mode: Classic") confirms the switch. No GUI needed.

Suggested default keybind: `V` (unused by vanilla, easy to reach). Fully remappable via the standard NeoForge key mapping system.

---

## Naming

Suggest avoiding "default/alternate" in player-facing text — those are internal labels. Proposed names:

| Internal | Player-facing | Feel |
|---|---|---|
| `DEFAULT` | **Classic** | "the original way" |
| `ALTERNATE` | **Precise** | "I know exactly where I want it" |

---

## Implementation

### State storage

A single `boolean` (or `enum ThrowMode { CLASSIC, PRECISE }`) stored in a client-side config or a simple `KeyMapping` companion class. Because this is a client preference affecting how the item *behaves on use*, the chosen mode must be communicated to the server at the moment of use — the server is where the entity spawns and the germination point is computed.

Two options:

**Option A — Packet on use.** When the player releases right-click and the item fires, the client sends a small custom packet containing the chosen mode and the computed target position (for Precise mode). The server trusts this position within a validated range (max distance check) and uses it. Simple, low-traffic.

**Option B — Sync mode to server on toggle.** When the player toggles the keybind, send a packet updating the server's record of that player's current mode. The server then computes the germination point itself using its own copy of the player's look vector at release time. Slightly more robust (server computes position independently), slightly more state to maintain.

**Recommendation: Option A.** The position is simple to validate server-side (distance from player ≤ max range), and sending it with the use packet is the most straightforward path. The server still validates; it just doesn't need to store per-player mode state.

### Precise mode — germination point calculation

```
minDistance = 5 blocks
maxDistance = 40 blocks  (tune in playtesting)
chargeTime  = hold duration in ticks, clamped to [0, maxChargeTicks]

distance = minDistance + (chargeTime / maxChargeTicks) * (maxDistance - minDistance)
target   = playerEyePos + (playerLookVector * distance)
```

The target is then snapped to the nearest integer block position. Overlap safety check runs against this position (same as current system).

### Precise mode — visual flight path

The entity is spawned at the player's hand position and must arrive at `target` in exactly `flightTicks` (currently 40 ticks = 2 seconds). The required velocity is:

```
toTarget       = target - spawnPos
flightTicks    = 40
gravityPerTick = 0.03  (vanilla ThrowableItemProjectile default)

// gravity pulls the entity down by gravityPerTick each tick, accumulating
// total downward displacement over flightTicks:
//   Σ(g * t) for t=0..flightTicks ≈ g * flightTicks² / 2
gravityCorrection = Vec3(0, gravity * flightTicks * flightTicks / 2, 0)

requiredVelocity = (toTarget + gravityCorrection) / flightTicks
```

Setting the entity's initial velocity to `requiredVelocity` makes it arrive at `target` after exactly `flightTicks` ticks under normal gravity. The entity's tick-60 germination check fires at the target regardless of minor floating-point drift — or the entity can teleport to `target` on tick 39 and germinate on tick 40 as a safety measure.

**Vertical aim edge case:** if the player aims straight up, `toTarget` is nearly vertical and `requiredVelocity` becomes very large. Clamp the vertical component of the look vector to a reasonable range (e.g. no steeper than 75° elevation) or cap the required velocity magnitude and accept minor positional drift for extreme angles. Straight-up throws placing an island directly above the player is probably not a use case worth fully supporting.

### Classic mode — no changes

The existing system is unchanged. Hold duration → launch velocity → gravity arc → germinate at physics position after 2 seconds. The only addition is that the mode toggle exists and Classic is the label shown in the actionbar.

### Charge indicator

Both modes already benefit from a visual charge indicator (the longer you hold, the more you need to know how charged you are). This already exists or is planned. In Precise mode the indicator now also maps directly to distance, so consider labeling it with a distance readout ("~12 blocks") rather than a generic charge bar — more readable for the direct-placement use case.

---

## Sequence diagram

```
Player holds right-click
    → item.use() starts charge timer (same for both modes)

Player releases right-click
    → Classic mode:
          velocity = f(chargeTicks)
          spawn entity with that velocity
          entity flies under gravity
          tick 60: germinate at current position

    → Precise mode:
          distance = f(chargeTicks)
          target   = eyePos + lookVec * distance
          [client sends target to server via packet]
          server validates (distance ≤ max, not inside solid block cluster)
          requiredVelocity = solve_for_velocity(target, flightTicks, gravity)
          spawn entity with requiredVelocity
          entity flies (visually correct arc toward target)
          tick 60: germinate at target (teleport safety if needed)

Player presses toggle keybind (any time)
    → ThrowMode cycles CLASSIC ↔ PRECISE
    → actionbar message: "Throw mode: Classic" / "Throw mode: Precise"
    → mode saved to client config
    → [Option A: no packet until next use]
```

---

## Open questions

- **Max distance value.** 40 blocks is a starting guess. Needs playtesting — too short and Precise mode isn't meaningfully different from Classic; too long and islands end up out of render distance.
- **Min distance value.** 5 blocks prevents placing an island inside or directly adjacent to the current island. Adjust based on average island radius.
- **Vertical clamp angle.** Decide whether to hard-clamp or just let extreme angles produce imprecise results with a warning.
- **Charge indicator.** Distance readout vs. generic bar — decide before implementing the HUD element.
- **Default mode for new players.** Classic preserves existing behavior; Precise is more learnable. Classic as default is the safer choice since it changes nothing for existing players.

---

*No changes to the theme codec, island generator, or germination logic — this is purely a change to the throw/placement layer.*
