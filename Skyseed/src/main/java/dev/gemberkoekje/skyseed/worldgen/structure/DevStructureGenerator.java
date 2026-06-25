package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.neoforged.fml.event.lifecycle.FMLCommonSetupEvent;
import net.neoforged.fml.loading.FMLEnvironment;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * Dev-only: writes Skyseed's code-authored building templates (Hamlet/Trade Post/Village Center) into the source
 * {@code data/skyseed/structure/} tree on startup, so they are committed and shipped as real {@code .nbt}.
 * Never runs in production (the templates load from the jar there) and never overwrites an existing file,
 * so a structure-block-authored replacement is safe. Registered on the mod event bus in {@link Skyseed}.
 */
public final class DevStructureGenerator {
    private DevStructureGenerator() {}

    public static void onCommonSetup(FMLCommonSetupEvent event) {
        if (FMLEnvironment.production) {
            return;
        }
        try {
            // build.gradle passes the repo-root structure dir as a system property — needed under Stonecutter, where
            // the run executes in the per-version node and a user.dir guess would write to the node's throwaway src
            // copy. Fall back to the old "run/ is a sibling of src/" guess for a plain run without the property.
            final String override = System.getProperty("skyseed.structureDir");
            final Path base = (override != null && !override.isBlank())
                    ? Path.of(override)
                    : Path.of(System.getProperty("user.dir"))
                            .resolveSibling("src").resolve("main/resources/data/skyseed/structure");
            HamletTemplates.generateInto(base.resolve("hamlet"));
            TradePostTemplates.generateInto(base.resolve("trade_post"));
            VillageCenterTemplates.generateInto(base.resolve("village_center"));
            AnimalTemplates.generateInto(base.resolve("animal"));
            DungeonTemplates.generateInto(base.resolve("dungeon"));
            RuinedPortalTemplates.generateInto(base.resolve("ruined_portal"));
            NetherFortressTemplates.generateInto(base.resolve("nether_fortress"));
            BastionTemplates.generateInto(base.resolve("bastion"));
            DebugStreetTemplates.generateInto(base.resolve("debug_streets")); // SKYJIGSAWPLAN Phase 0 spike
            PiglinTradingPostTemplates.generateInto(base.resolve("piglin_trading_post"));
            WitherArenaTemplates.generateInto(base.resolve("wither_arena"));
            DesertTempleTemplates.generateInto(base.resolve("desert_temple"));
            JungleTempleTemplates.generateInto(base.resolve("jungle_temple"));
            WitchHutTemplates.generateInto(base.resolve("witch_hut"));
            OutpostTemplates.generateInto(base.resolve("outpost"));
            TrailRuinsTemplates.generateInto(base.resolve("trail_ruins"));
            TrialChamberTemplates.generateInto(base.resolve("trial_chamber"));
            WoodlandMansionTemplates.generateInto(base.resolve("woodland_mansion"));
            OceanMonumentTemplates.generateInto(base.resolve("ocean_monument"));
            RareStructureTemplates.generateInto(base);
            writeGameTestRegion(base.resolve("gametest").resolve("region.nbt"));
            writeBigGameTestRegion(base.resolve("gametest").resolve("big_region.nbt"));
        } catch (Exception e) {
            Skyseed.LOGGER.warn("[skyseed] dev structure template generation skipped: {}", e.toString());
        }
    }

    /** An empty 16×24×16 box (stone floor + air) used as the test region for {@code SkyseedGameTests}. */
    private static void writeGameTestRegion(Path file) throws IOException {
        if (Files.exists(file)) {
            return;
        }
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final BlockState stone = Blocks.STONE.defaultBlockState();
        final BlockState air = Blocks.AIR.defaultBlockState();
        for (int x = 0; x < 16; x++) {
            for (int z = 0; z < 16; z++) {
                m.put(new BlockPos(x, 0, z), stone);
                for (int y = 1; y < 24; y++) {
                    m.put(new BlockPos(x, y, z), air);
                }
            }
        }
        StructureWriter.write(m, file);
        Skyseed.LOGGER.info("[skyseed] generated gametest region template");
    }

    /** A 48×24×48 solid-dirt platform (3 deep) + air, for assembling sprawling jigsaw structures in a gametest. */
    private static void writeBigGameTestRegion(Path file) throws IOException {
        if (Files.exists(file)) {
            return;
        }
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final BlockState dirt = Blocks.DIRT.defaultBlockState();
        final BlockState air = Blocks.AIR.defaultBlockState();
        for (int x = 0; x < 48; x++) {
            for (int z = 0; z < 48; z++) {
                for (int y = 0; y <= 2; y++) {
                    m.put(new BlockPos(x, y, z), dirt);
                }
                for (int y = 3; y < 24; y++) {
                    m.put(new BlockPos(x, y, z), air);
                }
            }
        }
        StructureWriter.write(m, file);
        Skyseed.LOGGER.info("[skyseed] generated big gametest region template");
    }
}
