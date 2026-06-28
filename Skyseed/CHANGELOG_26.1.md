# Changelog — Minecraft 26.1.2 build

Notable changes to the **26.1.2** Skyseed build. Skyseed is one codebase built for multiple Minecraft versions (see
`REFACTORPLAN.md`); the **1.21.1** build's history is in [CHANGELOG_1.21.1.md](CHANGELOG_1.21.1.md). Both builds share
the version-number sequence, so a version can appear in one changelog and not the other.

> **Status: bootstrapping (Stage 2).** The 26.1.2 build does **not** compile or run yet. The Stonecutter node is wired
> (`0.155.0`), but the version-volatile API differences (~18 months of MC + NeoForge churn) still need their `compat`
> directives, and the worldgen content delta (the Pale Garden biome/theme, the 1.21.5 vegetation, and the new
> placeable mobs) is not yet implemented. See `REFACTORPLAN.md` "Stage 2 in detail".

## [0.155.0] - 2026-06-28

### Added
- **26.1.2 build node bootstrapped (Stage 2a).** Added the `26.1.2` node (NeoForge `26.1.2.76`, Java 25) to the
  Stonecutter version matrix, with per-node MC / NeoForge / Java / Parchment / Patchouli selection from the
  version-keyed root `gradle.properties`. The node **configures** but does **not yet build** — the `compat` directives
  and the version-keyed worldgen content are the work ahead.
