# Skyseed — curated mod list

**Loader:** NeoForge **1.21.1** (pinned to `21.1.233`, matching the Skyseed build).
**Rule:** every mod here is quality-of-life only — **zero new blocks/items/mobs**. All must be the **NeoForge 1.21.1**
build of the mod.

Slugs are a guide for finding each on CurseForge; the CurseForge app resolves them by name and pulls dependencies.

## Your picks (kept)

| Mod | CurseForge slug | Side | Notes |
|---|---|---|---|
| Just Enough Items (JEI) | `jei` | client | Recipe viewer. Auto-lists every Skyseed recipe. |
| Patchouli | `patchouli` | both | **The only mod Skyseed references** — and it's optional (falls back to a written book). |
| Configured | `configured` | client | In-game config menus. Needs **Framework** + pairs with **Catalogue**. |
| Fast Leaf Decay | `fast-leaf-decay` | server | Snappy leaf clearing. |
| Xaero's Minimap | `xaeros-minimap` | client | Separate download from the World Map. |
| Xaero's World Map | `xaeros-world-map` | client | Pairs with the minimap. |

## Vein Mining (your pick — kept)

| Mod | CurseForge slug | Side | Notes |
|---|---|---|---|
| Vein Mining | `vein-mining` | both | By **TheIllusiveC4**; ~48M downloads (the popular one). The "(Fabric/Forge/Quilt)" name is legacy — it **does** ship a NeoForge 1.21.1 build, and it's standalone (no extra deps). **Whitelist ores + logs** so it can't mine the island body — see CONFIG.md. |

## Standard QoL additions (the near-universal set)

**Performance**

| Mod | Slug | Side | What |
|---|---|---|---|
| Embeddium | `embeddium` | client | The Sodium rendering engine for NeoForge. *(Alt: Sodium's own NeoForge build — pick one.)* |
| FerriteCore | `ferritecore` | both | Cuts memory use, no behaviour change. |
| ModernFix | `modernfix` | both | Faster boot + many bug/perf fixes. |
| EntityCulling | `entityculling` | client | Skips rendering entities you can't see. |
| FPS Reducer | `fps-reducer` | client | Throttles FPS when the window is idle (the NeoForge equivalent of Dynamic FPS). |
| Clumps | `clumps` | server | Merges XP orbs so big drops don't tank FPS. |

**Info / HUD (read-only)**

| Mod | Slug | Side | What |
|---|---|---|---|
| Jade | `jade` | client | "What am I looking at" tooltip (block + harvest tool). |
| AppleSkin | `appleskin` | client | Food/saturation + held-food hunger on the HUD. |
| Just Enough Resources *(optional)* | `just-enough-resources-jer` | client | Adds mob-drop / world-gen tabs to JEI. Info only. |

**Inventory / controls**

| Mod | Slug | Side | What |
|---|---|---|---|
| Mouse Tweaks | `mouse-tweaks` | client | Drag-distribute / refill inventory QoL. |
| Controlling | `controlling` | client | Search + conflict-detect in the keybinds screen. |
| Catalogue | `catalogue` | client | Searchable mod-list screen (pairs with Configured). |
| Framework | `framework` | both | MrCrayfish library; **Configured** dependency. |

## Dependencies (CurseForge resolves these automatically)

- **Configured** → **Framework** + **Catalogue** (both listed above).
- **Vein Mining** is self-contained (no Architectury/Cloth needed).

## Deliberately excluded (popular but break the "no content" rule)

Quark · Supplementaries · Sophisticated Backpacks/Storage · Farmer's Delight / any "…Delight".
*(Carry On — pick up chests/mobs intact — is content-free if you ever want it.)*
