# Skyseed modpack (CurseForge)

A CurseForge modpack scaffold for shipping **Skyseed** with a curated **quality-of-life** mod set (no content mods).
NeoForge **1.21.1**.

## Layout

```
modpack-vanilla/
├── manifest.json      CurseForge pack manifest (MC 1.21.1, NeoForge 21.1.233; files[] filled on export)
├── modlist.md         the curated mods to add, with CurseForge slugs + sides + dependencies
├── CONFIG.md          per-mod configuration (only a few need any; Vein Mining is the important one)
└── overrides/         copied verbatim into the instance
    ├── mods/          ← drop the built Skyseed jar here (it isn't on CurseForge)
    └── config/        ← ships empty; configs generate on first run, then apply CONFIG.md
```

## Build it — CurseForge / Overwolf app (easiest)

1. Create a profile: Minecraft **1.21.1**, loader **NeoForge `21.1.233`**.
2. Add each mod from [`modlist.md`](modlist.md) (search by name; let it pull dependencies — Configured pulls Framework + Catalogue).
3. Drop the **built Skyseed jar** (`build/libs/skyseed-<version>.jar`) into the profile's `mods/` folder — Skyseed isn't on CurseForge.
4. **Launch once** (generates the config files), then apply [`CONFIG.md`](CONFIG.md) — especially the Vein Mining ore/log whitelist + activation.
5. **Export** the profile. CurseForge writes a `manifest.json` with the exact `projectID`/`fileID` for every mod, plus an `overrides/` of your tuned configs. Use that as the real pack — this scaffold's empty `manifest.json` + `overrides/` are just the starting structure.

## Or hand-fill `manifest.json`

The skeleton ships `"files": []`. For each mod add an entry:

```json
{ "projectID": 238222, "fileID": 0000000, "required": true }
```

(IDs are on each mod's CurseForge page; `238222` is JEI's project, as an example.) The app is far less tedious.

## Skyseed integration

- **No Skyseed code changes.** Patchouli is the only optional integration and it already degrades gracefully (written-book fallback). Nothing else is referenced by the mod.
- Because Skyseed uses **only vanilla blocks**, JEI shows its recipes, Jade IDs every island block, Vein Mining mines island ores/logs, and Xaero's maps the islands — all with zero patching.

## Client vs. server (if you host)

- **Server-side:** Vein Mining, Fast Leaf Decay, Clumps, FerriteCore, ModernFix, Patchouli, Framework (+ Skyseed).
- **Client-only:** Xaero's (×2), JEI, Jade, AppleSkin, Mouse Tweaks, Controlling, Catalogue, Configured, Embeddium, EntityCulling, FPS Reducer.
- For single-player none of this matters — install everything.
