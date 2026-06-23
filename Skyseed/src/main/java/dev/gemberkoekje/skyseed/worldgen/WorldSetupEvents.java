package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.QuartPos;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.ResourceKey;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.tags.BiomeTags;
import net.minecraft.world.level.GameRules;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.biome.Biome;
import net.minecraft.world.level.levelgen.NoiseBasedChunkGenerator;
import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.fml.common.EventBusSubscriber;
import net.neoforged.neoforge.event.server.ServerStartedEvent;
import org.jetbrains.annotations.Nullable;

/** Places the curated starting island once, when a brand-new world is first created (README → World & progression setup). */
@EventBusSubscriber(modid = Skyseed.MODID)
public final class WorldSetupEvents {
    private static final BlockPos FALLBACK_CENTER = new BlockPos(8, 100, 8);
    private static final int SEARCH_STEP = 48;   // biome regions are large; sample every 48 blocks
    private static final int SEARCH_RINGS = 250; // outward to ~12k blocks before giving up

    private WorldSetupEvents() {}

    @SubscribeEvent
    static void onServerStarted(ServerStartedEvent event) {
        MinecraftServer server = event.getServer();
        ServerLevel overworld = server.overworld();
        SkyseedWorldData data = overworld.getDataStorage()
                .computeIfAbsent(SkyseedWorldData.factory(), SkyseedWorldData.NAME);
        if (data.isStartPlaced()) {
            // Existing world: keep raids off if this is a Skyseed world (it has a curated start island).
            if (data.getStartSpawn() != null) {
                disableRaids(server, overworld);
            }
            logVersion(server, data);
            return;
        }
        // Only a brand-new world (no ticks elapsed yet) — never retrofit an existing save.
        if (overworld.getGameTime() == 0L) {
            BlockPos center = findLandCenter(overworld);
            // Honour the vanilla "Generate Bonus Chest" world-creation option with a starter chest on the island.
            boolean bonusChest = server.getWorldData().worldGenOptions().generateBonusChest();
            BlockPos spawn = StartIsland.build(overworld, center, bonusChest);
            overworld.setDefaultSpawnPos(spawn, 0.0F);
            data.markStartPlaced(spawn);
            data.setCreatedVersion(Skyseed.VERSION); // stamp the creation version for future-migration warnings
            disableRaids(server, overworld);
            Skyseed.LOGGER.info("[skyseed] placed curated starting island at {}; spawn {}", center, spawn);
        } else {
            data.markStartPlaced(null); // existing world: leave spawn untouched
        }
        logVersion(server, data);
    }

    private static void logVersion(MinecraftServer server, SkyseedWorldData data) {
        String created = data.getCreatedVersion();
        Skyseed.LOGGER.info("[skyseed] Skyseed {} active; this world was created on {}{}",
                Skyseed.VERSION,
                created != null ? created : "an earlier version (pre-0.35.2)",
                hasLegacyDimensions(server)
                        ? " — its Nether/End are still vanilla; a new world is needed for the empty Skyseed Nether and End"
                        : "");
    }

    /**
     * True if this world's Nether or End predate the void-dimension setup (v0.35.x) — i.e. they're still the
     * vanilla terrain dimensions baked in at creation, which can only be fixed by starting a new world. The
     * generator (and so its noise settings) is inlined into {@code level.dat} at world creation, so we read it
     * straight off the live dimension rather than guessing from a version stamp (which old worlds don't have).
     */
    public static boolean hasLegacyDimensions(MinecraftServer server) {
        return !usesVoidSettings(server.getLevel(Level.NETHER), "void_nether")
                || !usesVoidSettings(server.getLevel(Level.END), "void_end");
    }

    private static boolean usesVoidSettings(@Nullable ServerLevel level, String settingsPath) {
        if (level == null) {
            return true; // dimension absent (e.g. removed by another mod) → nothing to flag
        }
        if (level.getChunkSource().getGenerator() instanceof NoiseBasedChunkGenerator gen) {
            return gen.generatorSettings().is(ResourceKey.create(Registries.NOISE_SETTINGS,
                    ResourceLocation.fromNamespaceAndPath(Skyseed.MODID, settingsPath)));
        }
        return false;
    }

    /**
     * Raids don't work on tiny floating islands (illagers path into the void), so they're disabled on
     * Skyseed worlds — villager islands still trade, breed and spawn iron golems as normal
     * (SKYVILLAGESPLAN → Raids).
     */
    private static void disableRaids(MinecraftServer server, ServerLevel overworld) {
        overworld.getGameRules().getRule(GameRules.RULE_DISABLE_RAIDS).set(true, server);
        // Also stop random pillager patrols wandering onto islands — pillagers should be an Outpost encounter.
        overworld.getGameRules().getRule(GameRules.RULE_DO_PATROL_SPAWNING).set(false, server);
    }

    /**
     * Spiral outward from origin for the nearest normal land start biome (not ocean/river), sampling
     * at the island's exact Y so the island never lands in a deep sea. Uses the biome source directly
     * ({@code getUncachedNoiseBiome}) — no chunk loading.
     */
    private static BlockPos findLandCenter(ServerLevel level) {
        final int y = FALLBACK_CENTER.getY();
        final int qy = QuartPos.fromBlock(y);
        for (int ring = 0; ring <= SEARCH_RINGS; ring++) {
            for (int dx = -ring; dx <= ring; dx++) {
                for (int dz = -ring; dz <= ring; dz++) {
                    if (Math.max(Math.abs(dx), Math.abs(dz)) != ring) {
                        continue; // only the perimeter of each ring (nearest-first)
                    }
                    int x = dx * SEARCH_STEP;
                    int z = dz * SEARCH_STEP;
                    Holder<Biome> biome = level.getUncachedNoiseBiome(QuartPos.fromBlock(x), qy, QuartPos.fromBlock(z));
                    if (!biome.is(BiomeTags.IS_OCEAN) && !biome.is(BiomeTags.IS_DEEP_OCEAN) && !biome.is(BiomeTags.IS_RIVER)) {
                        return new BlockPos(x, y, z);
                    }
                }
            }
        }
        return FALLBACK_CENTER;
    }
}
