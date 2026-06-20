# Skyseed

A terraforming skyblock mod for **Minecraft 1.21.1 / NeoForge**. Craft a *Skyseed*, throw it into open air, and a new procedurally generated themed sky island germinates where it lands. Progression is driven by exploration + crafting, not condensing.

See [../skyseed.md](../skyseed.md) for the full design plan.

## Status

**Milestone 0 — toolchain scaffold.** A minimal mod that loads with no content registered yet. Proves the build/run loop works.

Milestones (from the plan):
- [x] 0. Toolchain — MDK scaffolded, builds, `runClient` launches an empty mod.
- [ ] 1. Item + one recipe
- [ ] 2. Throwable
- [ ] 3. Timer + placeholder platform
- [ ] 4. First real island
- [ ] 5. Decoration + ore
- [ ] 6. Datapack themes
- [ ] 7. Sizes
- [ ] 8. Starting island
- [ ] 9. Polish

## Building & running

Requires **JDK 21** (NeoForge 1.21.1's required Java version). The build is pinned to a local JDK 21 via `org.gradle.java.home` in `gradle.properties` — adjust that path if your JDK lives elsewhere.

```sh
./gradlew build          # compile + package the mod jar
./gradlew runClient      # launch a dev Minecraft client with the mod loaded
./gradlew runServer      # launch a dev dedicated server
```

The first invocation downloads Gradle, NeoForge, and Minecraft, so it takes a while.

## Layout

- `src/main/java/dev/gemberkoekje/skyseed/` — mod sources (`Skyseed.java` is the `@Mod` entry point).
- `src/main/resources/` — assets, and `META-INF/neoforge.mods.toml` (generated from `src/main/templates/`).
- `gradle.properties` — mod id/version and Minecraft/NeoForge versions.

Scaffolded from the [NeoForge ModDevGradle MDK](https://github.com/NeoForgeMDKs/MDK-1.21.1-ModDevGradle).
