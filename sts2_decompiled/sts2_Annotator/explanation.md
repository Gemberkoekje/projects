# Slay the Spire 2 — Decompiled Source Explanation

## Overview

This workspace contains the **decompiled C# source code** of **Slay the Spire 2** (STS2), the sequel to the critically acclaimed deck-building roguelike by **MegaCrit**. The code has been reconstructed from the game's compiled `.NET 9` assembly (`sts2.dll`) into a single Visual Studio project (`sts2.csproj`).

> **Note:** This is decompiled code, not the original source. Some constructs (compiler-generated types, inline arrays, regex source generators) are artifacts of the decompilation process.

---

## Technology Stack

| Layer | Technology |
|---|---|
| **Runtime** | .NET 9 (C# 14, x64) |
| **Game Engine** | Godot 4 (via `GodotSharp.dll` — the C# binding for Godot) |
| **Platform / Storefront** | Steam (`Steamworks.NET`) |
| **Networking (Multiplayer)** | ENet (peer-to-peer) + Steam Networking Sockets |
| **Spine Animation** | Custom `MegaSpine` bindings (Spine 2D skeletal animation) |
| **Error Reporting** | Sentry (`Sentry.dll`) |
| **Modding** | Harmony (`0Harmony.dll`) for runtime patching |
| **Text Formatting** | SmartFormat (`SmartFormat.dll`) for localized string interpolation |
| **Graphics (Windows)** | Vortice.DXGI / SharpGen for DirectX interop (display enumeration) |

---

## Project Structure

The codebase is organized into deeply-nested namespace-based folders. Each folder corresponds to a `MegaCrit.Sts2.*` namespace. Below is a high-level breakdown of the major subsystems.

### Core Game Architecture

| Namespace | Purpose |
|---|---|
| `Core.Nodes` | **Godot scene-tree nodes** — the UI and visual layer. Every class prefixed with `N` (e.g., `NGame`, `NCard`, `NCreature`) is a Godot `Node` subclass wired to a `.tscn` scene. `NGame` is the root entry point. |
| `Core.Models` | **Game data models** — immutable, canonical definitions for all game content (cards, relics, potions, powers, monsters, events, encounters, acts, characters, achievements, enchantments, orbs, afflictions, modifiers). All models extend `AbstractModel` and are registered in the static `ModelDb`. |
| `Core.Entities` | **Runtime game entities** — mutable state objects for cards, creatures, players, potions, relics, etc. that exist during a run. |
| `Core.Runs` | **Run management** — `RunState` (the current run), `RunManager` (lifecycle), `RunHistory`, scoring, card creation, odds/probability. |
| `Core.Combat` | **Combat system** — `CombatManager` (turn loop), `CombatState` (current fight snapshot), `CombatHistory` (action log). |
| `Core.GameActions` | **Action queue** — every discrete game effect (play card, use potion, deal damage, gain block, etc.) is a `GameAction` executed by the `ActionExecutor`. Network-aware variants prefixed with `Net*`. |
| `Core.Commands` | **Command builders** — high-level wrappers that enqueue `GameAction` sequences (e.g., `AttackCommand`, `CardCmd`, `DamageCmd`). |
| `Core.Hooks` | **Hook system** — event-driven hooks that relics, powers, and cards subscribe to (e.g., `ModifyDamageHookType`). |

### Content Definitions

| Namespace | Purpose |
|---|---|
| `Models.Cards` | Individual card implementations (500+ cards: `Bash`, `StrikeIronclad`, `Hyperbeam`, etc.) |
| `Models.Relics` | Relic definitions (200+ relics: `Akabeko`, `Kunai`, `SneckoEye`, etc.) |
| `Models.Powers` | Power/buff/debuff definitions (`StrengthPower`, `VulnerablePower`, `PoisonPower`, etc.) |
| `Models.Potions` | Potion definitions (`FirePotion`, `FairyInABottle`, etc.) |
| `Models.Monsters` | Monster definitions (`KnowledgeDemon`, `LagavulinMatriarch`, `Queen`, etc.) |
| `Models.Encounters` | Encounter compositions (which monsters appear together, weak/normal/elite/boss variants) |
| `Models.Events` | Non-combat event definitions (`Neow`, `Symbiote`, `WelcomeToWongos`, etc.) |
| `Models.Characters` | Playable characters: `Ironclad`, `Silent`, `Defect`, `Regent`, `Necrobinder`, `Deprived` (+ `RandomCharacter`) |
| `Models.Acts` | Act/world definitions: `Underdocks`, `Hive`, `Overgrowth`, `Glory` |
| `Models.Enchantments` | Card enchantment system (new to STS2): `Sharp`, `Nimble`, `Corrupted`, `Imbued`, etc. |
| `Models.Orbs` | Orb system (Defect): `LightningOrb`, `FrostOrb`, `DarkOrb`, `PlasmaOrb`, `GlassOrb` |
| `Models.Afflictions` | Affliction system: `Hexed`, `Galvanized`, `Bound`, `Entangled`, `Ringing`, `Smog` |
| `Models.Modifiers` | Run modifiers / custom run options: `CursedRun`, `SealedDeck`, `Draft`, `Murderous`, etc. |
| `Models.Achievements` | Achievement conditions |
| `Models.CardPools` / `RelicPools` / `PotionPools` | Pool definitions that govern which content each character can see |

### Progression & Unlocks

| Namespace | Purpose |
|---|---|
| `Core.Timeline` | **Timeline/meta-progression system** — `StoryModel`, `EpochModel`, `StoryPool`. The timeline consists of "stories" (e.g., `IroncladStory`, `SilentStory`, `NecrobinderStory`) divided into "epochs" that unlock cards, relics, potions, characters, and features. |
| `Core.Timeline.Epochs` | Individual epoch definitions (100+ epochs like `Ironclad2Epoch`, `Regent5Epoch`, `Colorless3Epoch`, `DailyRunEpoch`, etc.) |
| `Core.Timeline.Stories` | Story arc containers |
| `Core.Unlocks` | Unlock state tracking |
| `Core.Achievements` | Achievement system and metrics |

### Multiplayer

| Namespace | Purpose |
|---|---|
| `Core.Multiplayer` | High-level multiplayer services: `NetHostGameService`, `NetClientGameService`, `NetSingleplayerGameService`, `NetReplayGameService` |
| `Core.Multiplayer.Game` | Synchronizers for combat, events, rewards, map selection, reactions, treasure rooms, rest sites |
| `Core.Multiplayer.Game.Lobby` | Lobby system: `StartRunLobby`, `LoadRunLobby` |
| `Core.Multiplayer.Game.PeerInput` | Remote cursor tracking, peer screen state, hovered model sync |
| `Core.Multiplayer.Messages` | Network message definitions (lobby, game, sync, flavor, checksums) |
| `Core.Multiplayer.Transport` | Transport abstraction (`NetHost`, `NetClient`, `INetHandler`) with ENet and Steam implementations |
| `Core.Multiplayer.Serialization` | Binary packet reader/writer, message type registry |
| `Core.Multiplayer.Replay` | Combat replay recording and playback |
| `Core.Multiplayer.Quality` | Connection quality monitoring, heartbeat tracking |

### Saves & Persistence

| Namespace | Purpose |
|---|---|
| `Core.Saves` | Save/load infrastructure: `SaveManager`, `SerializableRun`, `SettingsSave`, `ProgressState`, `ProfileSave`, cloud saves, JSON serialization, file I/O |
| `Core.Saves.Runs` | Serializable forms of runtime objects (`SerializableCard`, `SerializableRelic`, `SerializablePlayer`, etc.) |
| `Core.Saves.Managers` | Specialized save managers: `RunSaveManager`, `ProgressSaveManager`, `SettingsSaveManager`, `ProfileSaveManager`, `RunHistorySaveManager` |
| `Core.Saves.Migrations` | Versioned save-file migration system with `MigrationRegistry`, `MigrationManager`, and per-schema migrations |
| `Core.Saves.Validation` | Save deserialization validation |

### UI & Screens

| Namespace | Purpose |
|---|---|
| `Core.Nodes.Screens.*` | All game screens: `MainMenu`, `CharacterSelect`, `Map`, `Settings`, `CardSelection`, `CardLibrary`, `DeckView`, `Shops`, `Timeline`, `RunHistory`, `GameOver`, `Bestiary`, `PotionLab`, `CustomRun`, `DailyRun`, `ModdingScreen`, `Credits`, `FeedbackScreen`, `PauseMenu`, `ProfileScreen`, `StatsScreen`, `TreasureRoomRelic`, `Capstones`, `Overlays`, `InspectScreens` |
| `Core.Nodes.Combat` | Combat UI: `NCombatUi`, `NPlayerHand`, `NEndTurnButton`, `NHealthBar`, `NCreature`, `NIntent`, `NPower`, `NTargetManager`, `NTargetingArrow`, `NCardPlayQueue` |
| `Core.Nodes.Cards` | Card display: `NCard`, `NTinyCard`, `NCardGrid`, card holders (hand, grid, preview) |
| `Core.Nodes.Vfx` | Visual effects (100+ VFX classes): damage numbers, card trails, spell impacts, screen shake, etc. |
| `Core.Nodes.CommonUi` | Shared UI widgets: buttons, popups, scrollbars, dropdowns, banners, input management |
| `Core.Nodes.Events` | Event screen layouts and option buttons |
| `Core.Nodes.Ftue` | First-time user experience / tutorials |
| `Core.Nodes.HoverTips` | Tooltip display system |

### Supporting Systems

| Namespace | Purpose |
|---|---|
| `Core.Localization` | Localization: `LocManager`, `LocString`, `LocTable`, dynamic variable substitution, font management, language codes, formatters |
| `Core.Audio` | Audio management, FMOD sound effects |
| `Core.Assets` | Asset loading, texture-packed sprite sheets (`TpSheet*`), atlas management, preloading |
| `Core.Random` | Deterministic RNG: `Rng`, `PlayerRngSet`, `RunRngSet` (seeded for replays and multiplayer sync) |
| `Core.Map` | Procedural map generation: `ActMap`, `StandardActMap`, `MapPoint`, `MapCoord`, path pruning, post-processing |
| `Core.Rooms` | Room types: `CombatRoom`, `EventRoom`, `RestSiteRoom`, `MerchantRoom`, `TreasureRoom`, `MapRoom` |
| `Core.MonsterMoves` | Monster AI state machines and intent system |
| `Core.Rewards` | Reward generation: `CardReward`, `RelicReward`, `PotionReward`, `GoldReward` |
| `Core.CardSelection` | Card selection/targeting preferences |
| `Core.Events` | Event option handling, layout types |
| `Core.ControllerInput` | Controller support: Xbox, PlayStation, Switch, Steam Controller configs |
| `Core.Platform` | Platform abstraction: Steam achievements, leaderboards, stats, window modes |
| `Core.Modding` | Mod loading: `ModManager`, `Mod`, `ModManifest`, Harmony-based patching |
| `Core.DevConsole` | In-game developer console with 40+ commands |
| `Core.AutoSlay` | Automated play-testing ("auto-slay") framework with screen and room handlers |
| `Core.Debug` | Debug tools, Sentry error reporting, FPS visualization, scene bootstrapping |
| `Core.Helpers` | Utility classes: math, easing, reflection, string formatting, colors, time, scrolling |
| `Core.Extensions` | Extension methods for lists, enumerables, signals |
| `Core.TextEffects` / `Core.RichTextTags` | Rich text rendering with animated effects (jitter, sine, fade-in, fly-in, etc.) |
| `Core.Bindings.MegaSpine` | Spine animation interop (skeletons, bones, skins, animation states) |
| `Core.ValueProps` | Damage and block value propagation (`DamageProps`, `BlockProps`) |
| `Core.Leaderboard` | Daily run leaderboard queries |
| `Core.Daily` | Daily run seed generation via time server |
| `Core.Odds` | Probability/odds configuration for card rarities, potions, map points |

### Other Notable Files

| File | Purpose |
|---|---|
| `GodotPlugins.Game/Main.cs` | Godot engine entry point — native interop bootstrap |
| `Properties/AssemblyInfo.cs` | Assembly metadata and Godot `[ScriptPath]` registration |
| `RiderTestRunner/` | JetBrains Rider CI test runner integration |
| `*SourceGeneration*` | Source-generator attributes for model subtype discovery |
| `System.Text.RegularExpressions.Generated/` | Compile-time regex source generators |

---

## Key Design Patterns

1. **Model-Entity separation** — `AbstractModel` subclasses are immutable, singleton game definitions registered in `ModelDb`. Runtime state is held in entity classes (`Player`, `Creature`, `CardPile`, etc.).

2. **Action queue** — All game-state mutations go through `GameAction` objects executed by `ActionExecutor`, ensuring determinism and network replayability.

3. **Hook-based extensibility** — Cards, relics, and powers register for hooks (`ModifyDamageHookType`, etc.) to react to game events without tight coupling.

4. **Deterministic RNG** — Seeded `Rng` instances (`RunRngSet`, `PlayerRngSet`) ensure identical outcomes across multiplayer clients and replays.

5. **Scene-tree UI** — All visual elements are Godot `Node` subclasses (prefixed `N`) bound to `.tscn` scene files via `[ScriptPath]` attributes.

6. **Network-transparent actions** — Each `GameAction` has a `Net*` counterpart that serializes the action for multiplayer synchronization.

7. **Save migration system** — Versioned schema migrations (`IMigration`) applied by `MigrationManager` for forward-compatible saves.

---

## Characters (Playable)

| Character | Description |
|---|---|
| **Ironclad** | Strength-based warrior. 80 HP, 99 gold. Relic: `BurningBlood`. |
| **Silent** | Poison/shiv rogue. Relic: `RingOfTheSnake`. |
| **Defect** | Orb-channeling construct. Relic: `CrackedCore`. |
| **Regent** | New to STS2 — star/royalty themed. |
| **Necrobinder** | New to STS2 — soul/undead summoner. |
| **Deprived** | New to STS2 — appears to be an additional unlockable character. |

---

## Acts

| Act | World |
|---|---|
| Act 1 | **Underdocks** |
| Act 2 | **Hive** |
| Act 3 | **Overgrowth** |
| Final | **Glory** |

---

## What's New in STS2 (vs. STS1)

Based on the decompiled code, notable additions include:

- **Multiplayer co-op** — Full 2-player cooperative play with lobby system, action synchronization, map voting, shared events, combat replays, and peer input tracking.
- **Enchantment system** — Cards can be enchanted with modifiers (`Sharp`, `Nimble`, `Imbued`, `Corrupted`, etc.) at rest sites and events.
- **Affliction system** — Status conditions beyond debuffs (`Hexed`, `Galvanized`, `Bound`, `Ringing`, `Smog`).
- **Three new characters** — Regent, Necrobinder, and Deprived join the original three.
- **Timeline/meta-progression** — Story-driven unlock system with epochs replacing the simple character-level unlocks.
- **Star resource** — A new resource system used by certain cards and powers.
- **Summon/Minion system** — Some characters/cards can summon minions.
- **Daily runs with leaderboards** — Seeded daily challenges with score tracking.
- **Mod support** — Built-in mod manager using Harmony for runtime patching.
- **Treasure room relic-picking fight** — A new relic acquisition mechanic.
- **Map drawing** — Players can draw on the shared map.
- **Reaction wheel** — Multiplayer emote/reaction system.
- **Bestiary** — In-game monster encyclopedia.
- **Card library with stats** — Browse all cards with play/win statistics.
- **Godot engine** — Migrated from LibGDX (Java) to Godot 4 (C#).

---

## Build Notes

- **Target**: .NET 9.0, x64, C# 14
- **Unsafe code**: Enabled (for Godot interop and Spine bindings)
- **External DLLs**: `GodotSharp.dll`, `Steamworks.NET.dll`, `Sentry.dll`, `0Harmony.dll`, `SmartFormat.dll`, `Vortice.DXGI.dll`, `SharpGen.Runtime.dll`, `System.IO.Hashing.dll`
- The project will not produce a runnable game on its own — it requires the Godot engine runtime, scene files (`.tscn`), assets, and the native DLLs present in the game installation.
