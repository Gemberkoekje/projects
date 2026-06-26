# Per-mod configuration

The generated configs now live in `overrides/config/` (captured after a first launch). Most mods are perfect on
their generated defaults; the notes below cover what I tuned and the one choice left to you. Anything not mentioned
sits at its default.

---

## Vein Mining (TheIllusiveC4)  *(the one that matters on a skyblock)*

Two files: `config/veinmining-server.toml` (behaviour) + `config/veinmining-client.toml` (activation). Both can also
be edited in-game via **Configured**.

**Island safety — already handled by the generated defaults.** The config is a *whitelist of ores + logs*:

```
blocks       = "CONFIG_LIST"
blocksList   = ["#c:ores", "#forge:ores", "#minecraft:logs"]
blocksListType = "ALLOW"
```

So stone / dirt / grass can **never** be vein-mined and the island body stays unminable. (You could equivalently set
`blocks = "ORES_LOGS"`, a built-in preset.) Also on by default and good for skyblock: `preventToolDestruction`,
`limitedByDurability`, `limitedByWorld`.

**Activation — free from the start, hold `` ` ``/`~` to vein-mine.**

- `maxBlocksBase = 100` (was `0`) → works **without** the Vein Mining enchantment; the enchantment now just adds
  `+50` blocks per level on top. `100` comfortably fells a whole tree / ore vein.
- `activationState = "HOLD_KEY_DOWN"`, and `options.txt` binds `key.veinmining.activate` to `grave.accent` — so you
  **hold `` ` `` (the `~` key) while breaking** an ore or log to vein-mine it. ⚠️ `V` stays free for Skyseed's
  throw-mode toggle.

**Hunger is the balance.** `addExhaustion = true` makes vein-mining drain hunger, so you can't spam it while
starving — it "works as long as you're not hungry." It's light at the default `exhaustionMultiplier = 1.0`; raise it
(e.g. `5`–`15`) if you want hunger to bite harder.

## Fast Leaf Decay  — *tuned*

`config/fastleafdecay-server.toml`: lowered to `MinimumDecayTime = 2` / `MaximumDecayTime = 6` ticks (from 4 / 11)
for near-instant leaf clearing. (A marginal nudge — it was already fast.)

## FPS Reducer  — *tuned*

`config/fpsreducer/fpsreducer-client.toml`: set `waitingTime = 300` (was `0` = off), so it caps the frame rate to
`idleFps` (10) after 5 min idle. `reducingInBackground` + `suppressSound` were already on (throttles + mutes when the
window is unfocused).

## Clumps

No config file generated — runs on defaults (XP-orb merge range 8). Fine as-is.

## Jade

Defaults are good (`config/jade/`): mod-name tooltip on, hidden from tab list / GUIs, blocks + entities + harvestability
shown. `displayMode` is `TOGGLE` (press the key to show the overlay) — flip it in-game if you'd rather it always show.

## JEI

Runs in normal (non-cheat) mode by default — nothing to change.

## Xaero's Minimap / World Map

Defaults are great for single-player. Use **Controlling** to spot/rebind if keys ever collide.

## Keybind map (current — from `options.txt`)

- `V` → **Skyseed: toggle throw mode** (leave it).
- `` ` ``/`~` → **Vein Mining** (hold while breaking).
- Xaero: `M` open map · `B` new waypoint · `Y` minimap settings · `Z` enlarge map · `U` waypoints.
