package dev.gemberkoekje.skyseed.gametest;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.compat.Ids;
import dev.gemberkoekje.skyseed.compat.Jigsaw;
import dev.gemberkoekje.skyseed.compat.Lookup;
import dev.gemberkoekje.skyseed.command.SkyseedCommands;
import dev.gemberkoekje.skyseed.entity.IslandSeedEntity;
import dev.gemberkoekje.skyseed.registry.ModEntities;
import dev.gemberkoekje.skyseed.registry.ModItems;
import dev.gemberkoekje.skyseed.registry.SkyseedRegistries;
import dev.gemberkoekje.skyseed.worldgen.GenerationJob;
import dev.gemberkoekje.skyseed.worldgen.DebugForce;
import dev.gemberkoekje.skyseed.worldgen.IslandGenerator;
import dev.gemberkoekje.skyseed.worldgen.IslandPlacement;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan;
import dev.gemberkoekje.skyseed.worldgen.StartIsland;
import dev.gemberkoekje.skyseed.worldgen.TwinPlacer;
import dev.gemberkoekje.skyseed.worldgen.structure.PathSurfacer;
import dev.gemberkoekje.skyseed.worldgen.theme.BiomeOverride;
import dev.gemberkoekje.skyseed.worldgen.theme.IslandTheme;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.animal.Cow;
import net.minecraft.world.entity.animal.IronGolem;
import net.minecraft.world.entity.npc.Villager;
import net.minecraft.world.item.Item;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.ResourceKey;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.gametest.framework.GameTest;
import net.minecraft.gametest.framework.GameTestHelper;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.tags.BlockTags;
import net.minecraft.tags.FluidTags;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.biome.Biomes;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.entity.ChestBlockEntity;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;
import net.minecraft.world.level.levelgen.structure.templatesystem.StructurePlaceSettings;
import net.minecraft.world.level.levelgen.structure.templatesystem.StructureTemplate;
import net.neoforged.neoforge.gametest.GameTestHolder;
import net.neoforged.neoforge.gametest.PrefixGameTestTemplate;

/**
 * Behavioural guard rail for the generation + structure pipeline (see {@code codereview.md}). These run on the
 * {@code gameTestServer} run (or {@code /test runall}) with a live server, so {@link IslandGenerator#planIsland}
 * has the registry + biome access it needs. They assert invariants — not exact byte output — so they survive
 * refactors (the {@code IslandGenerator} split, the structure-template de-duplication) while still catching a
 * real regression (no blocks, lost determinism, a structure that lost its spawner/cage/vault).
 *
 * <p>All tests use the empty {@code skyseed:gametest/region} template (16×24×16, emitted by
 * {@link dev.gemberkoekje.skyseed.worldgen.structure.DevStructureGenerator} at dev time).
 */
@GameTestHolder(Skyseed.MODID)
@PrefixGameTestTemplate(false)
public final class SkyseedGameTests {
    private SkyseedGameTests() {}

    // The @GameTestHolder namespace (skyseed) is prepended automatically → skyseed:gametest/region.
    private static final String REGION = "gametest/region";
    private static final String BIG_REGION = "gametest/big_region";

    private static ResourceLocation skyseed(String path) {
        return ResourceLocation.fromNamespaceAndPath(Skyseed.MODID, path);
    }

    private static IslandTheme theme(ServerLevel level, String name) {
        final IslandTheme t = level.registryAccess().registryOrThrow(SkyseedRegistries.THEME).get(skyseed(name));
        if (t == null) {
            throw new IllegalStateException("theme '" + name + "' is not loaded");
        }
        return t;
    }

    /** Plan an island for {@code themeName} around the test region, with a fixed seed for reproducibility. */
    private static IslandPlan plan(GameTestHelper helper, String themeName, long seed) {
        final ServerLevel level = helper.getLevel();
        final BlockPos center = helper.absolutePos(new BlockPos(8, 8, 8));
        return IslandGenerator.planIsland(level, center, theme(level, themeName),
                level.getBiome(center), RandomSource.create(seed));
    }

    // --- generation invariants (guard the IslandGenerator split) ---

    @GameTest(template = REGION)
    public static void islandGeneratesBlocks(GameTestHelper helper) {
        final IslandPlan p = plan(helper, "rocky", 1L);
        helper.assertTrue(!p.blocks().isEmpty(), "planIsland produced no blocks for 'rocky'");
        helper.assertTrue(p.blocks().size() > 100, "a rocky island should be more than 100 blocks");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void rockyAdaptsInTheNether(GameTestHelper helper) {
        // Thrown in the Nether, the Rocky seed adapts (SKYNETHERPLAN): a netherrack body over a blackstone core
        // instead of overworld stone/cobblestone. Plan against the live the_nether level so planIsland reads
        // dimension = the_nether and the dimension-gated biome override wins.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var netherWastes = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.NETHER_WASTES);
        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40),
                theme(nether, "rocky"), netherWastes, RandomSource.create(7L));
        boolean netherrack = false;
        boolean blackstone = false;
        boolean cobblestone = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.NETHERRACK)) netherrack = true;
            if (bp.state().is(Blocks.BLACKSTONE)) blackstone = true;
            if (bp.state().is(Blocks.COBBLESTONE)) cobblestone = true;
        }
        helper.assertTrue(netherrack, "rocky in the Nether should be built from netherrack");
        helper.assertTrue(blackstone, "rocky in the Nether should have a blackstone core");
        helper.assertTrue(!cobblestone, "rocky in the Nether should not use overworld cobblestone");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void desertAdaptsInTheNether(GameTestHelper helper) {
        // Thrown in the Nether, Desert adapts (SKYNETHERPLAN): a Soul Sand Valley — soul sand over soul soil and a
        // basalt core — not the overworld sand island.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var ssv = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.SOUL_SAND_VALLEY);
        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40),
                theme(nether, "desert"), ssv, RandomSource.create(11L));
        boolean soulSand = false;
        boolean soulSoil = false;
        boolean sand = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.SOUL_SAND)) soulSand = true;
            if (bp.state().is(Blocks.SOUL_SOIL)) soulSoil = true;
            if (bp.state().is(Blocks.SAND) || bp.state().is(Blocks.SANDSTONE)) sand = true;
        }
        helper.assertTrue(soulSand, "desert in the Nether should have soul sand on top");
        helper.assertTrue(soulSoil, "desert in the Nether should have a soul soil fill");
        helper.assertTrue(!sand, "desert in the Nether should not use overworld sand/sandstone");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void badlandsAdaptsInTheNether(GameTestHelper helper) {
        // Thrown in the Nether, Badlands adapts (SKYNETHERPLAN): a Basalt Deltas fragment — blackstone + basalt,
        // with the overworld terracotta strata dropped (the override clears fill_bands).
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var deltas = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.BASALT_DELTAS);
        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40),
                theme(nether, "badlands"), deltas, RandomSource.create(5L));
        boolean blackstone = false;
        boolean basalt = false;
        boolean terracotta = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.BLACKSTONE)) blackstone = true;
            if (bp.state().is(Blocks.BASALT)) basalt = true;
            if (bp.state().is(Blocks.TERRACOTTA) || bp.state().is(Blocks.ORANGE_TERRACOTTA)) terracotta = true;
        }
        helper.assertTrue(blackstone, "badlands in the Nether should have blackstone");
        helper.assertTrue(basalt, "badlands in the Nether should have a basalt fill");
        helper.assertTrue(!terracotta, "badlands in the Nether should drop the overworld terracotta bands");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void aquaticAdaptsInTheNether(GameTestHelper helper) {
        // Thrown in the Nether, Aquatic adapts (SKYNETHERPLAN): a Lava Lagoon — the pond becomes a contained lava
        // basin on a basalt island, not the overworld water lake.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var wastes = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.NETHER_WASTES);
        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40),
                theme(nether, "aquatic"), wastes, RandomSource.create(3L));
        boolean lava = false;
        boolean basalt = false;
        boolean water = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.LAVA)) lava = true;
            if (bp.state().is(Blocks.BASALT)) basalt = true;
            if (bp.state().is(Blocks.WATER)) water = true;
        }
        helper.assertTrue(lava, "aquatic in the Nether should carve a lava lagoon");
        helper.assertTrue(basalt, "aquatic in the Nether should be a basalt island");
        helper.assertTrue(!water, "aquatic in the Nether should not place water");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void ancientAdaptsInTheNether(GameTestHelper helper) {
        // Thrown in the Nether, Ancient adapts (SKYNETHERPLAN): a haunted deep — a dark blackstone island over a
        // basalt core, not the overworld moss/deepslate.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var ssv = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.SOUL_SAND_VALLEY);
        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40),
                theme(nether, "ancient"), ssv, RandomSource.create(9L));
        boolean blackstone = false;
        boolean basalt = false;
        boolean moss = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.BLACKSTONE)) blackstone = true;
            if (bp.state().is(Blocks.BASALT)) basalt = true;
            if (bp.state().is(Blocks.MOSS_BLOCK)) moss = true;
        }
        helper.assertTrue(blackstone, "ancient in the Nether should be a blackstone island");
        helper.assertTrue(basalt, "ancient in the Nether should have a basalt core");
        helper.assertTrue(!moss, "ancient in the Nether should not use the overworld moss surface");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void mushroomAdaptsInTheNether(GameTestHelper helper) {
        // Thrown in the Nether, Mushroom adapts (SKYNETHERPLAN): a calm mycelium pocket over netherrack (the
        // mooshroom food island), not the overworld dirt-and-stone island.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var wastes = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.NETHER_WASTES);
        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40),
                theme(nether, "mushroom"), wastes, RandomSource.create(13L));
        boolean mycelium = false;
        boolean netherrack = false;
        boolean dirt = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.MYCELIUM)) mycelium = true;
            if (bp.state().is(Blocks.NETHERRACK)) netherrack = true;
            if (bp.state().is(Blocks.DIRT)) dirt = true;
        }
        helper.assertTrue(mycelium, "mushroom in the Nether should keep its mycelium surface");
        helper.assertTrue(netherrack, "mushroom in the Nether should have a netherrack body");
        helper.assertTrue(!dirt, "mushroom in the Nether should not use the overworld dirt fill");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void forestAdaptsInTheNether(GameTestHelper helper) {
        // Thrown in the Nether, Forest adapts (SKYNETHERPLAN): a fungal forest — crimson nylium over netherrack in a
        // crimson_forest biome — not the overworld grass-and-trees island.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var crimson = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.CRIMSON_FOREST);
        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40),
                theme(nether, "forest"), crimson, RandomSource.create(17L));
        boolean crimsonNylium = false;
        boolean netherrack = false;
        boolean grass = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.CRIMSON_NYLIUM)) crimsonNylium = true;
            if (bp.state().is(Blocks.NETHERRACK)) netherrack = true;
            if (bp.state().is(Blocks.GRASS_BLOCK)) grass = true;
        }
        helper.assertTrue(crimsonNylium, "forest in the Nether should have a crimson nylium surface");
        helper.assertTrue(netherrack, "forest in the Nether should have a netherrack body");
        helper.assertTrue(!grass, "forest in the Nether should not use the overworld grass surface");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void lushAdaptsInTheNether(GameTestHelper helper) {
        // Thrown in the Nether, Lush adapts (SKYNETHERPLAN): a warped-nylium vine grotto over netherrack, with no
        // pond (the override omits one, so the base water pond is dropped, not evaporated to a dry hole).
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var warped = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.WARPED_FOREST);
        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40),
                theme(nether, "lush"), warped, RandomSource.create(21L));
        boolean warpedNylium = false;
        boolean netherrack = false;
        boolean water = false;
        boolean moss = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.WARPED_NYLIUM)) warpedNylium = true;
            if (bp.state().is(Blocks.NETHERRACK)) netherrack = true;
            if (bp.state().is(Blocks.WATER)) water = true;
            if (bp.state().is(Blocks.MOSS_BLOCK)) moss = true;
        }
        helper.assertTrue(warpedNylium, "lush in the Nether should have a warped nylium surface");
        helper.assertTrue(netherrack, "lush in the Nether should have a netherrack body");
        helper.assertTrue(!water, "lush in the Nether should drop the overworld water pond");
        helper.assertTrue(!moss, "lush in the Nether should not use the overworld moss surface");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void largeSeedsAdaptInTheNether(GameTestHelper helper) {
        // Each Large terrain seed gets the same Nether form as its normal seed (just bigger). Spot-check that each
        // grows its Nether island — the expected Nether block present, no overworld block — when thrown in the Nether.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var wastes = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.NETHER_WASTES);
        record L(String theme, Block present, Block absent) {}
        final L[] cases = {
            new L("rocky_large", Blocks.NETHERRACK, Blocks.COBBLESTONE),
            new L("desert_large", Blocks.SOUL_SAND, Blocks.SAND),
            new L("badlands_large", Blocks.BLACKSTONE, Blocks.ORANGE_TERRACOTTA),
            new L("aquatic_large", Blocks.BASALT, Blocks.GRASS_BLOCK),
            new L("ancient_large", Blocks.BLACKSTONE, Blocks.MOSS_BLOCK),
            new L("mushroom_large", Blocks.MYCELIUM, Blocks.DIRT),
            new L("forest_large", Blocks.CRIMSON_NYLIUM, Blocks.GRASS_BLOCK),
            new L("lush_large", Blocks.WARPED_NYLIUM, Blocks.MOSS_BLOCK),
        };
        long seed = 100L;
        for (L c : cases) {
            final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40),
                    theme(nether, c.theme()), wastes, RandomSource.create(seed++));
            boolean present = false;
            boolean absent = false;
            for (IslandPlan.BlockPlacement bp : p.blocks()) {
                if (bp.state().is(c.present())) present = true;
                if (bp.state().is(c.absent())) absent = true;
            }
            helper.assertTrue(present, c.theme() + " should carry its Nether block " + c.present());
            helper.assertTrue(!absent, c.theme() + " must not carry the overworld block " + c.absent());
        }
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void netherRockyIsNetherNativeAndFullSize(GameTestHelper helper) {
        // The first Tier-2 Nether-NATIVE seed (SKYNETHERPLAN): unlike an overworld seed's tiny Nether foothold, this
        // grows a FULL-SIZE mining island in the Nether (radius 6-9, like an overworld normal seed). Its BASE config
        // is the_nether only (the overworld form is just an easter-egg override — see netherRockySeedMakesTinyOverworldIsland).
        final IslandTheme nr = theme(helper.getLevel(), "nether_rocky");
        helper.assertTrue(nr.baseValidIn(Level.NETHER.location()), "nether_rocky must implement the_nether");
        helper.assertTrue(!nr.baseValidIn(Level.OVERWORLD.location()), "nether_rocky's BASE must not be an overworld implementation");

        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var wastes = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.NETHER_WASTES);

        // Germination gate: valid (full base form) in the Nether.
        helper.assertTrue(IslandGenerator.formValidFor(nr, wastes, 64, Level.NETHER.location()),
                "nether_rocky should grow in the Nether");

        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40),
                theme(nether, "nether_rocky"), wastes, RandomSource.create(303L));
        helper.assertTrue(p.blocks().size() > 500,
                "a Nether Rocky island should be full-size (>500 blocks, vs a tiny adaptation's ~150), was " + p.blocks().size());
        boolean netherrack = false;
        boolean blackstone = false;
        boolean quartz = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.NETHERRACK)) netherrack = true;
            if (bp.state().is(Blocks.BLACKSTONE)) blackstone = true;
            if (bp.state().is(Blocks.NETHER_QUARTZ_ORE)) quartz = true;
        }
        helper.assertTrue(netherrack, "nether_rocky should have a netherrack body");
        helper.assertTrue(blackstone, "nether_rocky should have a blackstone core");
        helper.assertTrue(quartz, "nether_rocky should carry nether quartz ore");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void netherRockySeedMakesTinyOverworldIsland(GameTestHelper helper) {
        // Easter egg (SKYNETHERPLAN): thrown in the OVERWORLD a Nether Rocky seed does NOT fizzle — it grows a TINY
        // plain rocky island (sparse iron + gold), and deepslate if thrown low enough. "Yeah, I figured that'd happen."
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme nr = theme(overworld, "nether_rocky");
        final var plains = overworld.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.PLAINS);

        // It grows (does not fizzle) in the overworld via the easter-egg override.
        helper.assertTrue(IslandGenerator.formValidFor(nr, plains, 80, Level.OVERWORLD.location()),
                "nether_rocky should grow a tiny island in the overworld (easter egg), not fizzle");

        // High up: a tiny stone island, never netherrack.
        final IslandPlan high = IslandGenerator.planIsland(overworld, new BlockPos(40, 80, 40), nr, plains,
                RandomSource.create(11L));
        helper.assertTrue(high.blocks().size() < 500,
                "the overworld easter-egg island should be tiny, was " + high.blocks().size());
        boolean stone = false;
        boolean netherrack = false;
        for (IslandPlan.BlockPlacement bp : high.blocks()) {
            if (bp.state().is(Blocks.STONE)) stone = true;
            if (bp.state().is(Blocks.NETHERRACK)) netherrack = true;
        }
        helper.assertTrue(stone, "the overworld easter-egg island should be made of stone");
        helper.assertTrue(!netherrack, "the overworld easter-egg island should not be netherrack");

        // Thrown low (Y <= 8): the same tiny island, but deepslate.
        boolean deepslate = false;
        for (IslandPlan.BlockPlacement bp : IslandGenerator.planIsland(overworld, new BlockPos(40, 4, 40), nr, plains,
                RandomSource.create(12L)).blocks()) {
            if (bp.state().is(Blocks.DEEPSLATE) || bp.state().is(Blocks.COBBLED_DEEPSLATE)) deepslate = true;
        }
        helper.assertTrue(deepslate, "thrown low, the overworld easter-egg island should turn to deepslate");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void netherLavaIsFullSizeInBothDimensions(GameTestHelper helper) {
        // Tier-2 Lava (SKYNETHERPLAN): a full-size lava-lagoon island. Nether-native, but because the overworld has
        // no real lava island it ALSO grows full-size topside (a stone-bodied volcanic isle, NOT the tiny easter egg
        // nether_rocky makes). Base is the_nether-only; the overworld form is a full dimension override.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme nl = theme(overworld, "nether_lava");
        helper.assertTrue(nl.baseValidIn(Level.NETHER.location()), "nether_lava base must implement the_nether");
        helper.assertTrue(!nl.baseValidIn(Level.OVERWORLD.location()),
                "nether_lava base must be the_nether-only (the overworld form is an override)");

        final var plains = overworld.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.PLAINS);
        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var wastes = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.NETHER_WASTES);

        helper.assertTrue(IslandGenerator.formValidFor(nl, wastes, 64, Level.NETHER.location()),
                "nether_lava should grow in the Nether");
        helper.assertTrue(IslandGenerator.formValidFor(nl, plains, 80, Level.OVERWORLD.location()),
                "nether_lava should ALSO grow (full-size) in the overworld");

        // Nether: full-size lava lagoon over a netherrack body.
        final IslandPlan inNether = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), nl, wastes,
                RandomSource.create(51L));
        helper.assertTrue(inNether.blocks().size() > 500,
                "the Nether lava island should be full-size (>500 blocks), was " + inNether.blocks().size());
        boolean nBasalt = false;
        boolean nLava = false;
        boolean nNetherrack = false;
        for (IslandPlan.BlockPlacement bp : inNether.blocks()) {
            if (bp.state().is(Blocks.BASALT)) nBasalt = true;
            if (bp.state().is(Blocks.LAVA)) nLava = true;
            if (bp.state().is(Blocks.NETHERRACK)) nNetherrack = true;
        }
        helper.assertTrue(nBasalt, "Nether lava island should have a basalt surface");
        helper.assertTrue(nLava, "Nether lava island should have a lava lagoon");
        helper.assertTrue(nNetherrack, "Nether lava island should have a netherrack body");

        // Overworld: full-size lava island too, but stone-bodied (never netherrack).
        final IslandPlan inOverworld = IslandGenerator.planIsland(overworld, new BlockPos(40, 80, 40), nl, plains,
                RandomSource.create(52L));
        helper.assertTrue(inOverworld.blocks().size() > 500,
                "the overworld lava island should be full-size (>500 blocks), was " + inOverworld.blocks().size());
        boolean oBasalt = false;
        boolean oLava = false;
        boolean oNetherrack = false;
        for (IslandPlan.BlockPlacement bp : inOverworld.blocks()) {
            if (bp.state().is(Blocks.BASALT)) oBasalt = true;
            if (bp.state().is(Blocks.LAVA)) oLava = true;
            if (bp.state().is(Blocks.NETHERRACK)) oNetherrack = true;
        }
        helper.assertTrue(oBasalt, "overworld lava island should have a basalt surface");
        helper.assertTrue(oLava, "overworld lava island should have a lava lake");
        helper.assertTrue(!oNetherrack, "overworld lava island should NOT be netherrack (it is stone-bodied)");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void netherForestIsCrimsonWarpedWithTinyOverworld(GameTestHelper helper) {
        // Tier-2 Crimson/Warped (SKYNETHERPLAN): a full-size fungal forest. Crimson by default; warped nylium in a
        // warped_forest biome (a same-dimension biome override). Thrown topside it shrugs into a TINY grass island.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme nf = theme(overworld, "nether_forest");
        helper.assertTrue(nf.baseValidIn(Level.NETHER.location()), "nether_forest base must implement the_nether");
        helper.assertTrue(!nf.baseValidIn(Level.OVERWORLD.location()), "nether_forest base must be the_nether-only");

        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var biomes = nether.registryAccess().registryOrThrow(Registries.BIOME);
        final var crimson = biomes.getHolderOrThrow(Biomes.CRIMSON_FOREST);
        final var warped = biomes.getHolderOrThrow(Biomes.WARPED_FOREST);
        final var plains = overworld.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.PLAINS);

        // Crimson is the default Nether form: crimson nylium over a netherrack body, full-size.
        final IslandPlan c = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), nf, crimson,
                RandomSource.create(61L));
        helper.assertTrue(c.blocks().size() > 500, "the crimson forest island should be full-size, was " + c.blocks().size());
        boolean crimsonNylium = false;
        boolean cNetherrack = false;
        for (IslandPlan.BlockPlacement bp : c.blocks()) {
            if (bp.state().is(Blocks.CRIMSON_NYLIUM)) crimsonNylium = true;
            if (bp.state().is(Blocks.NETHERRACK)) cNetherrack = true;
        }
        helper.assertTrue(crimsonNylium, "the default Nether forest should have a crimson nylium surface");
        helper.assertTrue(cNetherrack, "the Nether forest should have a netherrack body");

        // In a warped_forest biome the same seed goes warped.
        final IslandPlan w = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), nf, warped,
                RandomSource.create(62L));
        boolean warpedNylium = false;
        for (IslandPlan.BlockPlacement bp : w.blocks()) {
            if (bp.state().is(Blocks.WARPED_NYLIUM)) warpedNylium = true;
        }
        helper.assertTrue(warpedNylium, "in a warped_forest biome the island should have a warped nylium surface");

        // Topside: a tiny grass island, no nylium, no netherrack.
        final IslandPlan o = IslandGenerator.planIsland(overworld, new BlockPos(40, 80, 40), nf, plains,
                RandomSource.create(63L));
        helper.assertTrue(o.blocks().size() < 500, "the overworld easter-egg island should be tiny, was " + o.blocks().size());
        boolean grass = false;
        boolean oNylium = false;
        boolean oNetherrack = false;
        for (IslandPlan.BlockPlacement bp : o.blocks()) {
            if (bp.state().is(Blocks.GRASS_BLOCK)) grass = true;
            if (bp.state().is(Blocks.CRIMSON_NYLIUM) || bp.state().is(Blocks.WARPED_NYLIUM)) oNylium = true;
            if (bp.state().is(Blocks.NETHERRACK)) oNetherrack = true;
        }
        helper.assertTrue(grass, "the overworld easter-egg island should be a grass island");
        helper.assertTrue(!oNylium, "the overworld easter-egg island should not be nylium");
        helper.assertTrue(!oNetherrack, "the overworld easter-egg island should not be netherrack");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void netherSoulIsFullSizeWithTinyDesertOverworld(GameTestHelper helper) {
        // Tier-2 Soul (SKYNETHERPLAN): a full-size Soul Sand Valley — soul sand riddled with bone fossils. Thrown
        // topside it makes a TINY desert island instead. Nether-native base; the overworld form is a dimension override.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme ns = theme(overworld, "nether_soul");
        helper.assertTrue(ns.baseValidIn(Level.NETHER.location()), "nether_soul base must implement the_nether");
        helper.assertTrue(!ns.baseValidIn(Level.OVERWORLD.location()), "nether_soul base must be the_nether-only");

        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var wastes = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.NETHER_WASTES);
        final var plains = overworld.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.PLAINS);

        helper.assertTrue(IslandGenerator.formValidFor(ns, wastes, 64, Level.NETHER.location()),
                "nether_soul should grow in the Nether");
        helper.assertTrue(IslandGenerator.formValidFor(ns, plains, 80, Level.OVERWORLD.location()),
                "nether_soul should grow a tiny desert in the overworld");

        // Nether: full-size soul sand valley with bone fossils.
        final IslandPlan inNether = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), ns, wastes,
                RandomSource.create(71L));
        helper.assertTrue(inNether.blocks().size() > 500, "the soul valley should be full-size, was " + inNether.blocks().size());
        boolean soulSand = false;
        boolean bone = false;
        boolean sand = false;
        for (IslandPlan.BlockPlacement bp : inNether.blocks()) {
            if (bp.state().is(Blocks.SOUL_SAND)) soulSand = true;
            if (bp.state().is(Blocks.BONE_BLOCK)) bone = true;
            if (bp.state().is(Blocks.SAND)) sand = true;
        }
        helper.assertTrue(soulSand, "the soul valley should have a soul sand surface");
        helper.assertTrue(bone, "the soul valley should have bone fossils");
        helper.assertTrue(!sand, "the soul valley should not have overworld sand");

        // Overworld: a tiny desert island, never soul sand.
        final IslandPlan o = IslandGenerator.planIsland(overworld, new BlockPos(40, 80, 40), ns, plains,
                RandomSource.create(72L));
        helper.assertTrue(o.blocks().size() < 500, "the overworld easter-egg island should be tiny, was " + o.blocks().size());
        boolean oSand = false;
        boolean oSoulSand = false;
        for (IslandPlan.BlockPlacement bp : o.blocks()) {
            if (bp.state().is(Blocks.SAND)) oSand = true;
            if (bp.state().is(Blocks.SOUL_SAND)) oSoulSand = true;
        }
        helper.assertTrue(oSand, "the overworld easter-egg island should be a desert (sand)");
        helper.assertTrue(!oSoulSand, "the overworld easter-egg island should not be soul sand");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void netherBasaltIsFullSizeWithTinyBadlandsOverworld(GameTestHelper helper) {
        // Tier-2 Basalt (SKYNETHERPLAN): a full-size Basalt Deltas — basalt over blackstone, gilded blackstone in the
        // core. Thrown topside it makes a TINY badlands island. Nether-native base; the overworld form is an override.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme nb = theme(overworld, "nether_basalt");
        helper.assertTrue(nb.baseValidIn(Level.NETHER.location()), "nether_basalt base must implement the_nether");
        helper.assertTrue(!nb.baseValidIn(Level.OVERWORLD.location()), "nether_basalt base must be the_nether-only");

        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var wastes = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.NETHER_WASTES);
        final var plains = overworld.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.PLAINS);

        helper.assertTrue(IslandGenerator.formValidFor(nb, wastes, 64, Level.NETHER.location()),
                "nether_basalt should grow in the Nether");
        helper.assertTrue(IslandGenerator.formValidFor(nb, plains, 80, Level.OVERWORLD.location()),
                "nether_basalt should grow a tiny badlands in the overworld");

        // Nether: full-size basalt deltas with gilded blackstone.
        final IslandPlan inNether = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), nb, wastes,
                RandomSource.create(81L));
        helper.assertTrue(inNether.blocks().size() > 500, "the basalt deltas should be full-size, was " + inNether.blocks().size());
        boolean basalt = false;
        boolean gilded = false;
        boolean redSand = false;
        for (IslandPlan.BlockPlacement bp : inNether.blocks()) {
            if (bp.state().is(Blocks.BASALT)) basalt = true;
            if (bp.state().is(Blocks.GILDED_BLACKSTONE)) gilded = true;
            if (bp.state().is(Blocks.RED_SAND)) redSand = true;
        }
        helper.assertTrue(basalt, "the basalt deltas should have a basalt surface");
        helper.assertTrue(gilded, "the basalt deltas should have gilded blackstone in the core");
        helper.assertTrue(!redSand, "the basalt deltas should not have overworld red sand");

        // Overworld: a tiny badlands island, never basalt.
        final IslandPlan o = IslandGenerator.planIsland(overworld, new BlockPos(40, 80, 40), nb, plains,
                RandomSource.create(82L));
        helper.assertTrue(o.blocks().size() < 500, "the overworld easter-egg island should be tiny, was " + o.blocks().size());
        boolean oRedSand = false;
        boolean oBasalt = false;
        for (IslandPlan.BlockPlacement bp : o.blocks()) {
            if (bp.state().is(Blocks.RED_SAND)) oRedSand = true;
            if (bp.state().is(Blocks.BASALT)) oBasalt = true;
        }
        helper.assertTrue(oRedSand, "the overworld easter-egg island should be a badlands (red sand)");
        helper.assertTrue(!oBasalt, "the overworld easter-egg island should not be basalt");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void ruinedPortalHasNetherVariantAndTwins(GameTestHelper helper) {
        // SKYNETHERPLAN (Ruined Portal twins): the ruined portal now grows in BOTH dimensions. Overworld = the
        // treasure frame (goodies pool); Nether = a small netherrack island with the no-goodies _nether frame. It is
        // flagged a twin theme, and the 8:1 linked-coordinate maths is the vanilla portal map.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme rp = theme(overworld, "ruined_portal");
        helper.assertTrue(rp.twin().isPresent(), "ruined_portal should be flagged as a cross-dimension twin theme");
        helper.assertTrue(rp.baseValidIn(Level.OVERWORLD.location()), "ruined_portal should grow in the overworld");

        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var wastes = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.NETHER_WASTES);
        final var plains = overworld.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.PLAINS);
        helper.assertTrue(IslandGenerator.formValidFor(rp, wastes, 64, Level.NETHER.location()),
                "ruined_portal should now also grow in the Nether");

        // Overworld form: the jigsaw uses the goodies pool.
        final IslandPlan ow = IslandGenerator.planIsland(overworld, new BlockPos(40, 80, 40), rp, plains,
                RandomSource.create(91L));
        helper.assertTrue(ow.jigsaws().stream().anyMatch(j -> j.pool().getPath().equals("ruined_portal/portal")),
                "the overworld ruined portal should use the goodies pool ruined_portal/portal");
        helper.assertTrue(ow.twinTheme().isPresent(), "the overworld ruined portal plan should carry a twin theme");

        // A ruined portal that rolls on a big island via rare_structures pairs too: the rare structure carries the
        // same twin theme, so planIsland routes it into the plan exactly like the dedicated seed does.
        final IslandTheme rockyLarge = theme(overworld, "rocky_large");
        helper.assertTrue(rockyLarge.rareStructures().stream().anyMatch(
                        rs -> rs.jigsaw().pool().getPath().equals("ruined_portal/portal") && rs.twin().isPresent()),
                "rocky_large's ruined-portal rare structure should carry the twin theme");

        // Nether form: a netherrack island whose jigsaw swaps to the no-goodies _nether pool.
        final IslandPlan nv = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), rp, wastes,
                RandomSource.create(92L));
        boolean netherrack = false;
        for (IslandPlan.BlockPlacement bp : nv.blocks()) {
            if (bp.state().is(Blocks.NETHERRACK)) netherrack = true;
        }
        helper.assertTrue(netherrack, "the Nether ruined portal should be a netherrack island");
        helper.assertTrue(nv.jigsaws().stream().anyMatch(j -> j.pool().getPath().equals("ruined_portal/portal_nether")),
                "the Nether ruined portal should swap to the no-goodies pool ruined_portal/portal_nether");

        // Linked-coordinate maths: overworld/8 and nether*8 (vanilla's portal map).
        final BlockPos toNether = TwinPlacer.linkedPortalPos(new BlockPos(800, 80, 80), Level.NETHER, nether);
        helper.assertTrue(toNether.getX() == 100 && toNether.getZ() == 10,
                "overworld->nether twin should divide X/Z by 8, was " + toNether);
        final BlockPos toOverworld = TwinPlacer.linkedPortalPos(new BlockPos(100, 70, 10), Level.OVERWORLD, overworld);
        helper.assertTrue(toOverworld.getX() == 800 && toOverworld.getZ() == 80,
                "nether->overworld twin should multiply X/Z by 8, was " + toOverworld);
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void largeNetherSeedsAreFullSizeNetherNative(GameTestHelper helper) {
        // The Large variants of the 5 Tier-2 Nether-native seeds (SKYNETHERPLAN): same biome content, much bigger
        // (radius 11-17). Each is the_nether-only and grows a LARGE island carrying its surface block.
        final ServerLevel overworld = helper.getLevel();
        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var wastes = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.NETHER_WASTES);
        record L(String theme, Block surface) {}
        final L[] cases = {
            new L("nether_rocky_large", Blocks.NETHERRACK),
            new L("nether_lava_large", Blocks.BASALT),
            new L("nether_forest_large", Blocks.CRIMSON_NYLIUM),
            new L("nether_soul_large", Blocks.SOUL_SAND),
            new L("nether_basalt_large", Blocks.BASALT),
        };
        long seed = 200L;
        for (L c : cases) {
            final IslandTheme t = theme(nether, c.theme());
            helper.assertTrue(t.baseValidIn(Level.NETHER.location()), c.theme() + " must implement the_nether");
            helper.assertTrue(!t.baseValidIn(Level.OVERWORLD.location()), c.theme() + " must be the_nether-only");
            final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), t, wastes,
                    RandomSource.create(seed++));
            helper.assertTrue(p.blocks().size() > 1500,
                    c.theme() + " should be a LARGE island (>1500 blocks), was " + p.blocks().size());
            boolean surface = false;
            for (IslandPlan.BlockPlacement bp : p.blocks()) {
                if (bp.state().is(c.surface())) surface = true;
            }
            helper.assertTrue(surface, c.theme() + " should carry its surface block " + c.surface());
        }
        helper.succeed();
    }

    /** The index of the first rare structure in {@code theme} whose jigsaw pool path equals {@code poolPath}, or -1. */
    private static int rareIndex(IslandTheme theme, String poolPath) {
        final var list = theme.rareStructures();
        for (int i = 0; i < list.size(); i++) {
            if (list.get(i).jigsaw().pool().getPath().equals(poolPath)) {
                return i;
            }
        }
        return -1;
    }

    @GameTest(template = REGION)
    public static void blazeRoomRollsOnLargeNetherSeeds(GameTestHelper helper) {
        // The surprise blaze spawner room (SKYNETHERPLAN): a 5% rare_structures roll on each of the 5 Large Nether
        // seeds. (Its on-demand debug seed is auto-generated from these hosts now, not a hand-made theme.)
        final ServerLevel overworld = helper.getLevel();
        for (String t : new String[] { "nether_rocky_large", "nether_lava_large", "nether_forest_large",
                "nether_soul_large", "nether_basalt_large" }) {
            final IslandTheme nt = theme(overworld, t);
            helper.assertTrue(nt.rareStructures().stream().anyMatch(
                            rs -> rs.jigsaw().pool().getPath().equals("nether_fortress/blaze_room")),
                    t + " should carry the 5% blaze spawner room rare structure");
        }
        // forcedRare germinates it on demand on a host theme — the same path the auto debug seed drives.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var wastes = nether.registryAccess().registryOrThrow(Registries.BIOME).getHolderOrThrow(Biomes.NETHER_WASTES);
        final IslandTheme host = theme(nether, "nether_rocky_large");
        final int idx = rareIndex(host, "nether_fortress/blaze_room");
        helper.assertTrue(idx >= 0, "nether_rocky_large should host the blaze room rare structure");
        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), host, wastes,
                RandomSource.create(140L), DebugForce.rare(idx));
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().getPath().equals("nether_fortress/blaze_room")),
                "forcing the blaze-room rare structure should assemble its jigsaw");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void bastionRemnantRollsOnBastionBiomeLargeSeeds(GameTestHelper helper) {
        // A ruined bastion remnant (crying obsidian + cracked polished blackstone) is a 5% rare_structures roll on the
        // three bastion-biome Large Nether seeds — the Nether-wastes Rocky, the crimson/warped Forest and the soul-sand
        // Soul — but NOT the basalt deltas or the lava sea (the vanilla rule). Its on-demand debug seed is auto-generated.
        final ServerLevel overworld = helper.getLevel();
        for (String t : new String[] { "nether_rocky_large", "nether_forest_large", "nether_soul_large" }) {
            helper.assertTrue(theme(overworld, t).rareStructures().stream().anyMatch(
                            rs -> rs.jigsaw().pool().getPath().equals("bastion/remnant")),
                    t + " should carry the 5% bastion remnant rare structure");
        }
        for (String t : new String[] { "nether_basalt_large", "nether_lava_large" }) {
            helper.assertTrue(theme(overworld, t).rareStructures().stream().noneMatch(
                            rs -> rs.jigsaw().pool().getPath().equals("bastion/remnant")),
                    t + " must not carry the bastion remnant (no bastions in the basalt deltas or the lava sea)");
        }
        // forcedRare germinates it on demand on a host theme — the same path the auto debug seed drives.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var wastes = nether.registryAccess().registryOrThrow(Registries.BIOME).getHolderOrThrow(Biomes.NETHER_WASTES);
        final IslandTheme host = theme(nether, "nether_rocky_large");
        final int idx = rareIndex(host, "bastion/remnant");
        helper.assertTrue(idx >= 0, "nether_rocky_large should host the bastion remnant rare structure");
        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), host, wastes,
                RandomSource.create(77L), DebugForce.rare(idx));
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().getPath().equals("bastion/remnant")),
                "forcing the bastion-remnant rare structure should assemble its jigsaw");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void debugStreetsSeedIsADeepJigsawSpike(GameTestHelper helper) {
        // SKYJIGSAWPLAN Phase 0 spike: a throwaway creative seed whose jigsaw recurses (depth 6) through a
        // self-connecting street pool, so the network branches, twists and — on a real island — runs out over the
        // void. The over-void sprawl and its reach are an in-world smoke test (throw the seed); here we just guard
        // the wiring — a deep jigsaw into the start pool, carried as a jigsaw site on the plan.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme streets = theme(overworld, "debug_streets");
        helper.assertTrue(streets.jigsaw().isPresent(), "debug_streets must have a jigsaw config");
        helper.assertTrue(streets.jigsaw().get().pool().getPath().equals("debug_streets/start"),
                "debug_streets must start from its start pool");
        helper.assertTrue(streets.jigsaw().get().depth() >= 5,
                "debug_streets must recurse deep enough to sprawl (got depth " + streets.jigsaw().get().depth() + ")");
        helper.assertTrue(streets.jigsaw().get().reach() > 0,
                "debug_streets must set reach > 0 so the path/bridge surfacing pass runs");
        final BlockPos c = new BlockPos(40, 80, 40);
        final IslandPlan p = IslandGenerator.planIsland(overworld, c, streets, overworld.getBiome(c),
                RandomSource.create(42L));
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().getPath().equals("debug_streets/start")),
                "the debug streets seed should carry its start jigsaw site");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void pathSurfacerResolvesMarkersIntoPathsAndBridges(GameTestHelper helper) {
        // SKYJIGSAWPLAN §3a: a connective piece leaves a PURPLE_WOOL marker one block above each path tile;
        // PathSurfacer turns a marker over ground into a dirt path and a marker over void into a self-railing
        // wooden bridge, then clears the markers. Hand-build a strip: ground for x=5..7, open void for x=8..9.
        final ServerLevel level = helper.getLevel();
        final BlockPos base = helper.absolutePos(new BlockPos(0, 0, 0));
        final int y = 5, z = 7;
        for (int x = 5; x <= 7; x++) { // ground: dirt fill under a grass deck tile
            level.setBlock(base.offset(x, y - 1, z), Blocks.DIRT.defaultBlockState(), Block.UPDATE_CLIENTS);
            level.setBlock(base.offset(x, y, z), Blocks.GRASS_BLOCK.defaultBlockState(), Block.UPDATE_CLIENTS);
        }
        for (int x = 5; x <= 9; x++) { // a marker one block above every path tile (ground x5..7, void x8..9)
            level.setBlock(base.offset(x, y + 1, z), PathSurfacer.MARKER.defaultBlockState(), Block.UPDATE_CLIENTS);
        }

        PathSurfacer.resolve(level, base.offset(7, y, z), 6);

        for (int x = 5; x <= 7; x++) { // ground tiles -> a worn dirt path
            helper.assertTrue(level.getBlockState(base.offset(x, y, z)).is(Blocks.DIRT_PATH),
                    "ground deck x=" + x + " should be a dirt path");
        }
        helper.assertTrue(level.getBlockState(base.offset(8, y, z)).is(Blocks.OAK_SLAB), "void deck should be a slab");
        helper.assertTrue(level.getBlockState(base.offset(9, y, z)).is(Blocks.OAK_SLAB), "void deck should be a slab");
        // The exposed (over-void) side of a bridge tile gets an edge beam + a fence railing.
        helper.assertTrue(level.getBlockState(base.offset(8, y, z + 1)).is(Blocks.OAK_PLANKS), "bridge edge beam");
        helper.assertTrue(level.getBlockState(base.offset(8, y + 1, z + 1)).is(Blocks.OAK_FENCE), "bridge railing");
        // Markers are all gone.
        for (int x = 5; x <= 9; x++) {
            helper.assertTrue(!level.getBlockState(base.offset(x, y + 1, z)).is(PathSurfacer.MARKER),
                    "marker at x=" + x + " should be cleared");
        }
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void pathSurfacerSupportsFloatingFloors(GameTestHelper helper) {
        // A solid lot floor over the void gets a dirt foundation (any material — here sandstone, proving it is not
        // oak-specific). Over PURE void it's a short stub (no long pillar into nothing); over ground within reach it
        // connects down to that ground. A floor already on solid ground is left alone, and an empty (air) lane deck
        // gets NO foundation, so the bridges stay floating.
        final ServerLevel level = helper.getLevel();
        final BlockPos base = helper.absolutePos(new BlockPos(0, 0, 0));
        final int deckY = 11; // supportFloatingFloors reads origin.Y - 1 as the floor level
        level.setBlock(base.offset(18, deckY, 20), Blocks.SANDSTONE.defaultBlockState(), Block.UPDATE_CLIENTS); // pure void below
        level.setBlock(base.offset(14, deckY, 20), Blocks.SPRUCE_PLANKS.defaultBlockState(), Block.UPDATE_CLIENTS); // over ground
        level.setBlock(base.offset(14, deckY - 5, 20), Blocks.STONE.defaultBlockState(), Block.UPDATE_CLIENTS);  // the ground below it
        level.setBlock(base.offset(22, deckY - 1, 20), Blocks.DIRT.defaultBlockState(), Block.UPDATE_CLIENTS);  // ground
        level.setBlock(base.offset(22, deckY, 20), Blocks.OAK_PLANKS.defaultBlockState(), Block.UPDATE_CLIENTS); // on ground
        level.setBlock(base.offset(16, deckY, 20), Blocks.AIR.defaultBlockState(), Block.UPDATE_CLIENTS); // a lane deck (air) over void

        PathSurfacer.supportFloatingFloors(level, base.offset(20, deckY + 1, 20), 6);

        helper.assertTrue(level.getBlockState(base.offset(18, deckY - 2, 20)).is(Blocks.DIRT),
                "the pure-void floor should get a short stub foundation");
        helper.assertTrue(level.getBlockState(base.offset(18, deckY - 3, 20)).isAir(),
                "the pure-void stub should stop short, not pillar into nothing");
        helper.assertTrue(level.getBlockState(base.offset(14, deckY - 4, 20)).is(Blocks.DIRT),
                "a floor over ground should connect its foundation down to that ground");
        helper.assertTrue(level.getBlockState(base.offset(22, deckY - 2, 20)).isAir(),
                "a floor already on solid ground should not be stilted");
        helper.assertTrue(level.getBlockState(base.offset(16, deckY - 1, 20)).isAir(),
                "an empty lane deck over the void should NOT be stilted (it stays a floating bridge)");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void snowCoverCapsHighestBlock(GameTestHelper helper) {
        // The snow post-pass lays a layer on the HIGHEST block of every column — so it lands on a building roof and a
        // tree canopy, not just the open ground beneath them. Full blocks / stairs / slabs / leaves receive it; a fence
        // (thin) does not, and an open-void column gets nothing.
        final ServerLevel level = helper.getLevel();
        final BlockPos base = helper.absolutePos(new BlockPos(0, 0, 0)); // region is 16x24x16 — keep everything inside
        final int y = 6;
        level.setBlock(base.offset(4, y, 4), Blocks.GRASS_BLOCK.defaultBlockState(), Block.UPDATE_CLIENTS); // open ground
        level.setBlock(base.offset(6, y + 4, 4), Blocks.OAK_PLANKS.defaultBlockState(), Block.UPDATE_CLIENTS); // a roof, higher up
        level.setBlock(base.offset(8, y + 3, 4), Blocks.OAK_LEAVES.defaultBlockState(), Block.UPDATE_CLIENTS); // a tree canopy
        level.setBlock(base.offset(10, y, 4), Blocks.OAK_FENCE.defaultBlockState(), Block.UPDATE_CLIENTS); // thin — no snow

        PathSurfacer.snowCover(level, base.offset(2, y - 2, 2), base.offset(13, y + 12, 6), 1.0f, level.getRandom());

        helper.assertTrue(level.getBlockState(base.offset(4, y + 1, 4)).is(Blocks.SNOW),
                "snow should cap the open ground");
        helper.assertTrue(level.getBlockState(base.offset(6, y + 5, 4)).is(Blocks.SNOW),
                "snow should land on the roof (the column's highest block), not the ground below it");
        helper.assertTrue(level.getBlockState(base.offset(8, y + 4, 4)).is(Blocks.SNOW),
                "snow should land on a tree canopy (leaves)");
        helper.assertTrue(level.getBlockState(base.offset(10, y + 1, 4)).isAir(),
                "a thin fence should not hold a snow layer");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void tradePostBlacksmithPlaces(GameTestHelper helper) {
        // The two large-section landmarks — the L-shaped forge and the great hall — are the FAVOURED pieces on the
        // village's large lots (weight 3 each, above the weight-1 shops), so a large lot reliably builds one of them.
        // We assert this on the pool WEIGHTS, deterministically: counting anvils/bells across an assembled village is
        // far too noisy to test reliably (the gametest origin varies per run, so the jigsaw — which seeds its assembly
        // RNG from the origin's chunk — builds a different village each run, and the rare landmark can miss all of a
        // small sample). getShuffledTemplates expands each element by its weight, so a piece's count is its weight.
        final var reg = helper.getLevel().registryAccess();
        final RandomSource rng = RandomSource.create(0L);
        final var lots = Lookup.templatePool(reg, Ids.mod("trade_post/large_lots")).value().getShuffledTemplates(rng);
        final long forge = lots.stream().filter(e -> e.toString().contains("trade_post/forge")).count();
        final long hall = lots.stream().filter(e -> e.toString().contains("trade_post/great_hall")).count();
        helper.assertTrue(forge > 0 && hall > 0,
                "the forge and great hall must both be large-lot pieces (forge=" + forge + ", hall=" + hall + ")");
        helper.assertTrue(forge > 1 && hall > 1,
                "the forge and great hall must outweigh a single (weight-1) shop on the large lots, so a large lot "
                        + "favours the bigger footprints (forge=" + forge + ", hall=" + hall + ")");
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void tradePostOverVoidUsesPiers(GameTestHelper helper) {
        // A surplus lot that lands over the void gets a plank pier (its signature: a mooring chain) from the _void
        // filler pool, instead of a floating farm; the same lot on solid ground gets the normal fields (no chain).
        // Sample several seeds (a small village may place no surplus at all) — over the void at least one should pier,
        // and on the island none ever should.
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 3, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("trade_post/start"));
        final var fillers = Lookup.templatePool(level.registryAccess(), Ids.mod("trade_post/fillers"));
        int voidChains = 0;
        int islandChains = 0;
        for (int iter = 0; iter < 8; iter++) {
            // (1) over the void — clear the whole region to air (incl. y0, so nothing solid sits below a lot)
            fillRegion(helper, true);
            Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 5, origin, false, "shop_", 4, fillers, iter);
            voidChains += countChains(helper);
            // (2) on solid ground — surplus lots should be fields, never piers
            fillRegion(helper, false);
            Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 5, origin, false, "shop_", 4, fillers, iter);
            islandChains += countChains(helper);
        }
        helper.assertTrue(voidChains > 0, "over the void, surplus lots should be piers (chain count=" + voidChains + ")");
        helper.assertTrue(islandChains == 0, "on the island, surplus lots should be fields, not piers (chain=" + islandChains + ")");
        helper.succeed();
    }

    /** Reset the big region: {@code void} clears every block to air; otherwise a solid dirt floor (y≤2) under air. */
    private static void fillRegion(GameTestHelper helper, boolean toVoid) {
        for (int x = 0; x <= 47; x++) {
            for (int z = 0; z <= 47; z++) {
                for (int y = 0; y <= 14; y++) {
                    helper.setBlock(new BlockPos(x, y, z), toVoid ? Blocks.AIR : (y <= 2 ? Blocks.DIRT : Blocks.AIR));
                }
            }
        }
    }

    private static int countChains(GameTestHelper helper) {
        int chains = 0;
        for (int x = 0; x <= 47; x++) {
            for (int z = 0; z <= 47; z++) {
                for (int y = 1; y <= 14; y++) {
                    if (helper.getBlockState(new BlockPos(x, y, z)).is(Blocks.CHAIN)) {
                        chains++;
                    }
                }
            }
        }
        return chains;
    }

    @GameTest(template = REGION)
    public static void tradePostIsAStreetVillage(GameTestHelper helper) {
        // SKYJIGSAWPLAN Phase 1: the Trade Post is now a street village — a square radiating a depth-4 street
        // network with shops + fields hung off lot connectors, surfaced by PathSurfacer (dirt paths on the island,
        // self-railing bridges over the void). Guard the wiring; the village's look is an in-world smoke test. The
        // streets/lots pools are validated by the datapack load (a bad element reference fails the run).
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme tp = theme(overworld, "trade_post");
        helper.assertTrue(tp.jigsaw().isPresent() && tp.jigsaw().get().pool().getPath().equals("trade_post/start"),
                "trade_post must start from its start pool");
        helper.assertTrue(tp.jigsaw().get().depth() >= 4, "trade_post must recurse into a street network");
        helper.assertTrue(tp.jigsaw().get().reach() > 0, "trade_post must set reach for surfacing + the bed scan");
        final BlockPos c = new BlockPos(40, 80, 40);
        final IslandPlan p = IslandGenerator.planIsland(overworld, c, tp, overworld.getBiome(c), RandomSource.create(8L));
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().getPath().equals("trade_post/start")),
                "trade_post should carry its start jigsaw site");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void villageCenterIsABigVillage(GameTestHelper helper) {
        // The village_center seed is "a bigger Trade Post" laid out as a CLUSTER: the SAME building pieces via a
        // denser street skeleton (trade_post/start_dense, whose lanes weight the large/big-building section higher),
        // a deeper street network and a guaranteed 4+ shops, spread over 3 small islands ringed around a void centre
        // (cluster_offsets). Like the trade post it's biome-styled -- a desert one pulls the desert pieces.
        final ServerLevel level = helper.getLevel();
        final IslandTheme vc = theme(level, "village_center");
        helper.assertTrue(vc.jigsaw().isPresent() && vc.jigsaw().get().pool().getPath().equals("trade_post/start_dense"),
                "village_center must reuse the trade post village pieces (via the denser start_dense skeleton)");
        helper.assertTrue(vc.jigsaw().get().depth() > 4, "village_center must run a deeper street network than the trade post (depth 4)");
        helper.assertTrue(vc.jigsaw().get().capMin() >= 4, "village_center must guarantee at least 4 shops");
        helper.assertTrue(!vc.shape().clusterOffsets().isEmpty(),
                "village_center must be a cluster of small islands (cluster_offsets), not one huge island");
        helper.assertTrue(vc.jigsaw().get().centerpiece().map(cp -> cp.getPath().equals("anvil")).orElse(false),
                "village_center must set an anvil centerpiece (the capstone at the cluster's centre)");
        final var desert = level.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(ResourceKey.create(Registries.BIOME, ResourceLocation.parse("minecraft:desert")));
        final IslandPlan p = IslandGenerator.planIsland(level, new BlockPos(40, 80, 40), vc, desert, RandomSource.create(8L));
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().getPath().equals("trade_post_desert/start_dense")),
                "a desert village_center must use the desert village pieces (biome-styled like the trade post)");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void villageCenterFavoursBigBuildings(GameTestHelper helper) {
        // The village_center assembles from start_dense, whose streets pool weights the large (big-building) section
        // higher than the trade post's — so it reads as having more forges / great halls. We assert this on the pool
        // WEIGHTS, deterministically: a per-village big-building count is far too noisy to test reliably (the gametest
        // origin varies per run; the ~1.7x bias needs a huge sample to clear the variance). getShuffledTemplates
        // expands each element by its weight, so counting the large section in that list yields exactly its weight.
        final var reg = helper.getLevel().registryAccess();
        final RandomSource rng = RandomSource.create(0L);
        final long dense = Lookup.templatePool(reg, Ids.mod("trade_post/streets_dense")).value()
                .getShuffledTemplates(rng).stream().filter(e -> e.toString().contains("street_large")).count();
        final long normal = Lookup.templatePool(reg, Ids.mod("trade_post/streets")).value()
                .getShuffledTemplates(rng).stream().filter(e -> e.toString().contains("street_large")).count();
        helper.assertTrue(dense > normal, "the village_center's dense streets must weight the large (big-building) "
                + "section higher than the trade post's (dense=" + dense + " vs trade post=" + normal + ")");
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void villageCenterpieceLandsOnSquareCentre(GameTestHelper helper) {
        // The anvil capstone (GenerationJob) is stamped at the jigsaw origin, which must be the start square's centre
        // tile — its lantern. Assemble just the start square and confirm the lantern sits exactly at origin: the spot
        // the capstone replaces. Guards against the jigsaw seating shifting and the capstone landing off-centre/floating.
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 3, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("trade_post/start"));
        Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 1, origin, false, "", 0, null, 1L);
        helper.assertTrue(helper.getBlockState(new BlockPos(24, 3, 24)).is(Blocks.LANTERN),
                "the start square's centre tile must land at origin (where the centerpiece capstone is stamped)");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void jigsawConfigWithPoolSwapsOnlyThePool(GameTestHelper helper) {
        // Backs JigsawConfig.withPool (the wither dimensionVariant uses to swap in a _nether pool): it must change ONLY
        // the pool. Round-trip — withPool(other).withPool(original) equalling the original — proves every other field
        // (depth, cap, golems, centerpiece, …) survived the copy.
        final var jc = theme(helper.getLevel(), "village_center").jigsaw().orElseThrow();
        final ResourceLocation other = ResourceLocation.parse("skyseed:trade_post_desert/start");
        final var swapped = jc.withPool(other);
        helper.assertTrue(swapped.pool().equals(other), "withPool must set the new pool");
        helper.assertTrue(swapped.withPool(jc.pool()).equals(jc),
                "withPool must change only the pool (round-trip to the original pool must equal the original)");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void tradePostDesertBiomeSelectsDesertPool(GameTestHelper helper) {
        // Biome-override wiring — what a forced-biome debug seed exercises. Planning the trade post in a desert biome
        // must select the desert jigsaw pool (and a sand surface), not the default plains/oak start.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme tp = theme(overworld, "trade_post");
        final var desert = overworld.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(ResourceKey.create(Registries.BIOME, ResourceLocation.parse("minecraft:desert")));
        final BlockPos c = new BlockPos(40, 80, 40);
        final IslandPlan p = IslandGenerator.planIsland(overworld, c, tp, desert, RandomSource.create(8L));
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().getPath().equals("trade_post_desert/start")),
                "a trade post in a desert biome should use the desert jigsaw pool");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void tradePostBiomeStylesSelectTheirPools(GameTestHelper helper) {
        // The remaining village styles: savanna gets its acacia pool; taiga and snowy share the spruce pool.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme tp = theme(overworld, "trade_post");
        final BlockPos c = new BlockPos(40, 80, 40);
        final String[][] cases = {
                {"minecraft:savanna", "trade_post_savanna/start"},
                {"minecraft:taiga", "trade_post_spruce/start"},
                {"minecraft:snowy_plains", "trade_post_spruce/start"}};
        for (String[] cs : cases) {
            final var biome = overworld.registryAccess().registryOrThrow(Registries.BIOME)
                    .getHolderOrThrow(ResourceKey.create(Registries.BIOME, ResourceLocation.parse(cs[0])));
            final IslandPlan p = IslandGenerator.planIsland(overworld, c, tp, biome, RandomSource.create(8L));
            helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().getPath().equals(cs[1])),
                    cs[0] + " trade post should use pool " + cs[1]);
        }
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void hamletBiomeStylesSelectTheirPools(GameTestHelper helper) {
        // The hamlet is biome-aware like the trade post: each biome selects its own hamlet hub pool (which in turn
        // pulls that biome's trade-post shops).
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme h = theme(overworld, "hamlet");
        final BlockPos c = new BlockPos(40, 80, 40);
        final String[][] cases = {
                {"minecraft:plains", "hamlet/start"},
                {"minecraft:desert", "hamlet_desert/start"},
                {"minecraft:savanna", "hamlet_savanna/start"},
                {"minecraft:taiga", "hamlet_spruce/start"},
                {"minecraft:snowy_plains", "hamlet_spruce/start"}};
        for (String[] cs : cases) {
            final var biome = overworld.registryAccess().registryOrThrow(Registries.BIOME)
                    .getHolderOrThrow(ResourceKey.create(Registries.BIOME, ResourceLocation.parse(cs[0])));
            final IslandPlan p = IslandGenerator.planIsland(overworld, c, h, biome, RandomSource.create(8L));
            helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().getPath().equals(cs[1])),
                    cs[0] + " hamlet should use pool " + cs[1]);
        }
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void hamletReusesTradePostShops(GameTestHelper helper) {
        // The hamlet starts from a small green whose lot connectors pull the trade post's lots pool, so it places the
        // same diverse profession shops, capped to 1–2. Five hamlets should land at least one shop (a RED_BED).
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 3, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("hamlet/start"));
        final var fillers = Lookup.templatePool(level.registryAccess(), Ids.mod("trade_post/fillers"));
        int beds = 0;
        for (int iter = 0; iter < 5; iter++) {
            for (int x = 4; x <= 44; x++) {
                for (int z = 4; z <= 44; z++) {
                    for (int y = 1; y <= 14; y++) {
                        helper.setBlock(new BlockPos(x, y, z), y <= 2 ? Blocks.DIRT : Blocks.AIR);
                    }
                }
            }
            Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 2, origin, false, "shop_", 2, fillers);
            for (int x = 4; x <= 44; x++) {
                for (int z = 4; z <= 44; z++) {
                    for (int y = 1; y <= 14; y++) {
                        if (helper.getBlockState(new BlockPos(x, y, z)).is(Blocks.RED_BED)) {
                            beds++;
                        }
                    }
                }
            }
        }
        helper.assertTrue(beds > 0, "5 hamlets placed no shops (red_bed=" + beds + ")");
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void tradePostBiomePiecesUseTheirWood(GameTestHelper helper) {
        // The savanna set is acacia and the shared spruce set is spruce — confirm the palette produced the right wood
        // (not oak) and the villages still place shops via the same cap/filler machinery.
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 3, 24));
        checkWood(helper, level, origin, "trade_post_savanna", Blocks.ACACIA_PLANKS);
        checkWood(helper, level, origin, "trade_post_spruce", Blocks.SPRUCE_PLANKS);
        helper.succeed();
    }

    private static void checkWood(GameTestHelper helper, ServerLevel level, BlockPos origin, String pool, Block wood) {
        for (int x = 4; x <= 44; x++) {
            for (int z = 4; z <= 44; z++) {
                for (int y = 1; y <= 14; y++) {
                    helper.setBlock(new BlockPos(x, y, z), y <= 2 ? Blocks.DIRT : Blocks.AIR);
                }
            }
        }
        final var startPool = Lookup.templatePool(level.registryAccess(), Ids.mod(pool + "/start"));
        final var fillers = Lookup.templatePool(level.registryAccess(), Ids.mod(pool + "/fillers"));
        Jigsaw.placeCapped(level, startPool, Ids.mc("bottom"), 5, origin, false, "shop_", 3, fillers);
        int beds = 0;
        int rightWood = 0;
        int oak = 0;
        for (int x = 4; x <= 44; x++) {
            for (int z = 4; z <= 44; z++) {
                for (int y = 1; y <= 14; y++) {
                    final BlockState s = helper.getBlockState(new BlockPos(x, y, z));
                    if (s.is(Blocks.RED_BED)) {
                        beds++;
                    } else if (s.is(wood)) {
                        rightWood++;
                    } else if (s.is(Blocks.OAK_PLANKS)) {
                        oak++;
                    }
                }
            }
        }
        helper.assertTrue(beds > 0, pool + " placed no shops (red_bed=" + beds + ")");
        helper.assertTrue(rightWood > 0, pool + " has no " + wood + " walls (count=" + rightWood + ")");
        helper.assertTrue(oak == 0, pool + " still has oak plank walls (oak=" + oak + ")");
    }

    @GameTest(template = BIG_REGION)
    public static void tradePostVillagePlacesShops(GameTestHelper helper) {
        // Regression for the shop cap. A shop carries a job-site block (composter/lectern/barrel/fletching); the cap
        // keeps the `cap` shops nearest the centre and re-stamps the surplus lots as fields/gardens. Asserting an exact
        // count off ONE assembled village is fragile — the jigsaw is seeded from the (grid-allocated) origin chunk, so
        // which village lands, and where its shops fall relative to the scan, shifts whenever the gametest SET changes.
        // Instead sample a few deterministic villages per cap, scan the WHOLE template, and assert the two robust halves
        // of the guarantee: no village ever EXCEEDS the cap (the trim), and the cap IS reachable when enough lots place
        // (the fill). (Loads dev-generated .nbt; syncDevStructures keeps the node copy current.)
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 3, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("trade_post/start"));
        final var fillers = Lookup.templatePool(level.registryAccess(), Ids.mod("trade_post/fillers"));
        int crops = 0;
        for (int cap = 2; cap <= 4; cap++) {
            int best = 0;
            for (long seed = 1; seed <= 3; seed++) {
                for (int x = 0; x < 48; x++) {
                    for (int z = 0; z < 48; z++) {
                        for (int y = 1; y <= 14; y++) {
                            helper.setBlock(new BlockPos(x, y, z), y <= 2 ? Blocks.DIRT : Blocks.AIR); // reset platform
                        }
                    }
                }
                Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 6, origin, false, "shop_", cap, fillers, seed);
                int villageShops = 0;
                for (int x = 0; x < 48; x++) {
                    for (int z = 0; z < 48; z++) {
                        for (int y = 1; y <= 14; y++) {
                            final BlockState s = helper.getBlockState(new BlockPos(x, y, z));
                            // One job-site block per capped small shop (all eight professions). The forge (furnace /
                            // smithing table / anvil), the great hall (bell) and the fillers use none of these, so the
                            // count is exactly the kept shops. Decorations must not reuse a job site (the market stall
                            // sells produce, not a barrel/composter), or they'd be miscounted as shops.
                            if (s.is(Blocks.COMPOSTER) || s.is(Blocks.LECTERN) || s.is(Blocks.BARREL)
                                    || s.is(Blocks.FLETCHING_TABLE) || s.is(Blocks.SMOKER) || s.is(Blocks.LOOM)
                                    || s.is(Blocks.STONECUTTER) || s.is(Blocks.CARTOGRAPHY_TABLE)) {
                                villageShops++;
                            } else if (s.is(Blocks.WHEAT) || s.is(Blocks.POTATOES) || s.is(Blocks.CARROTS)) {
                                crops++; // a wheat / potato / carrot field tile
                            }
                        }
                    }
                }
                helper.assertTrue(villageShops <= cap,
                        "cap " + cap + " exceeded — kept more shops than the cap (shops=" + villageShops + ")");
                best = Math.max(best, villageShops);
            }
            helper.assertTrue(best == cap,
                    "no sampled village reached the capped " + cap + " shops (best=" + best + ") — the fill is unreachable");
        }
        helper.assertTrue(crops > 0, "villages placed no crop fields (crops=" + crops + ")");
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void tradePostDesertVillageIsSandstone(GameTestHelper helper) {
        // Biome diversity: a desert biome override swaps in the sand/sandstone piece set (its own jigsaw pool), built
        // by the same generator from a desert palette. Assemble it and confirm the buildings are sandstone — not oak —
        // while the shop cap + filler machinery still place shops and fields. (Loads dev-generated .nbt.)
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 3, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("trade_post_desert/start"));
        final var fillers = Lookup.templatePool(level.registryAccess(), Ids.mod("trade_post_desert/fillers"));
        for (int x = 4; x <= 44; x++) {
            for (int z = 4; z <= 44; z++) {
                for (int y = 1; y <= 14; y++) {
                    helper.setBlock(new BlockPos(x, y, z), y <= 2 ? Blocks.DIRT : Blocks.AIR);
                }
            }
        }
        Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 5, origin, false, "shop_", 3, fillers);
        int beds = 0;
        int sandstone = 0;
        int oakWall = 0;
        for (int x = 4; x <= 44; x++) {
            for (int z = 4; z <= 44; z++) {
                for (int y = 1; y <= 14; y++) {
                    final BlockState s = helper.getBlockState(new BlockPos(x, y, z));
                    if (s.is(Blocks.RED_BED)) {
                        beds++;
                    } else if (s.is(Blocks.SMOOTH_SANDSTONE)) {
                        sandstone++;
                    } else if (s.is(Blocks.OAK_PLANKS)) {
                        oakWall++;
                    }
                }
            }
        }
        helper.assertTrue(beds > 0, "desert village placed no shops (red_bed=" + beds + ")");
        helper.assertTrue(sandstone > 0, "desert village has no sandstone walls (smooth_sandstone=" + sandstone + ")");
        helper.assertTrue(oakWall == 0, "desert village still has oak plank walls (oak_planks=" + oakWall + ")");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void netherFortressIsNetherNativeWithFortressJigsaw(GameTestHelper helper) {
        // Nether-native fortress island (SKYNETHERPLAN): a netherrack island that assembles the fortress jigsaw — a keep
        // with a caged blaze spawner, self-connecting arcaded bridge spans out over the void, wart-garden ends. The
        // structure itself is placed later by the generation job, so the plan carries it as a jigsaw site.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme nf = theme(overworld, "nether_fortress");
        helper.assertTrue(nf.baseValidIn(Level.NETHER.location()), "nether_fortress must implement the_nether");
        helper.assertTrue(!nf.baseValidIn(Level.OVERWORLD.location()), "nether_fortress must be the_nether-only");

        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var wastes = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.NETHER_WASTES);
        helper.assertTrue(IslandGenerator.formValidFor(nf, wastes, 64, Level.NETHER.location()),
                "nether_fortress should grow in the Nether");

        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), nf, wastes,
                RandomSource.create(131L));
        boolean netherrack = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.NETHERRACK)) netherrack = true;
        }
        helper.assertTrue(netherrack, "the nether fortress island should be a netherrack island");
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().getPath().equals("nether_fortress/start")),
                "the nether fortress island should assemble the fortress jigsaw (start pool = the keep)");
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void netherFortressAssemblesABoundedBridge(GameTestHelper helper) {
        // Assemble the fortress jigsaw with the production span_ cap and confirm the pieces actually chain AND stay
        // bounded: the keep places exactly one (blaze) spawner, the self-connecting arcaded spans (incl. branching
        // crossings) lay a run of nether brick out from it, and the cap (≤ 8 span_ pieces, surplus re-stamped as
        // wart-garden ends) keeps it a compact fortress — not a runaway sprawl that fills the 48² template. (Loads
        // dev-generated .nbt; syncDevStructures keeps the node copy current.)
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 3, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("nether_fortress/start"));
        final var ends = Lookup.templatePool(level.registryAccess(), Ids.mod("nether_fortress/ends"));
        Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 5, origin, false, "span_", 8, ends, 1L);
        int spawners = 0, netherBrick = 0;
        for (int x = 0; x < 48; x++) {
            for (int z = 0; z < 48; z++) {
                for (int y = 1; y <= 16; y++) {
                    final BlockState s = helper.getBlockState(new BlockPos(x, y, z));
                    if (s.is(Blocks.SPAWNER)) spawners++;
                    else if (s.is(Blocks.NETHER_BRICKS)) netherBrick++;
                }
            }
        }
        helper.assertTrue(spawners == 1, "the keep must place exactly one blaze spawner (got " + spawners + ")");
        helper.assertTrue(netherBrick > 60, "the keep + bridge should lay nether brick (got " + netherBrick + ")");
        helper.assertTrue(netherBrick < 1400, "the span_ cap should keep it bounded, not sprawling (got " + netherBrick + ")");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void bastionIsNetherNativeWithBastionJigsaw(GameTestHelper helper) {
        // Nether-native bastion remnant island: a blackstone island that assembles the hand-built bastion (a
        // lodestone treasure plinth, a magma-cube spawner, bastion loot). The structure is placed later by the
        // generation job, so the plan carries it as a jigsaw site.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme bastion = theme(overworld, "bastion");
        helper.assertTrue(bastion.baseValidIn(Level.NETHER.location()), "bastion must implement the_nether");
        helper.assertTrue(!bastion.baseValidIn(Level.OVERWORLD.location()), "bastion must be the_nether-only");

        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var wastes = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.NETHER_WASTES);
        helper.assertTrue(IslandGenerator.formValidFor(bastion, wastes, 64, Level.NETHER.location()),
                "bastion should grow in the Nether");
        final var deltas = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.BASALT_DELTAS);
        helper.assertTrue(!IslandGenerator.formValidFor(bastion, deltas, 64, Level.NETHER.location()),
                "the bastion should fizzle in the basalt deltas (the vanilla rule)");

        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), bastion, wastes,
                RandomSource.create(7L));
        boolean blackstone = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.BLACKSTONE)) {
                blackstone = true;
            }
        }
        helper.assertTrue(blackstone, "the bastion island should be a blackstone island");
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().getPath().equals("bastion/bastion")),
                "the bastion should assemble the bastion jigsaw");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void piglinTradingPostIsNetherNativeWithItsJigsaw(GameTestHelper helper) {
        // Nether-native Piglin Trading Post: a blackstone island that assembles the hand-built trading-post hall and
        // grows anywhere in the Nether — including the basalt deltas, since (unlike the bastion) it has no fizzle rule.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme post = theme(overworld, "piglin_trading_post");
        helper.assertTrue(post.baseValidIn(Level.NETHER.location()), "trading post must implement the_nether");
        helper.assertTrue(!post.baseValidIn(Level.OVERWORLD.location()), "trading post must be the_nether-only");

        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var wastes = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.NETHER_WASTES);
        helper.assertTrue(IslandGenerator.formValidFor(post, wastes, 64, Level.NETHER.location()),
                "trading post should grow in the Nether");
        final var deltas = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.BASALT_DELTAS);
        helper.assertTrue(IslandGenerator.formValidFor(post, deltas, 64, Level.NETHER.location()),
                "the trading post has no fizzle rule — it should grow in the basalt deltas too");

        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), post, wastes,
                RandomSource.create(11L));
        boolean blackstone = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.BLACKSTONE)) {
                blackstone = true;
            }
        }
        helper.assertTrue(blackstone, "the trading post island should be a blackstone island");
        helper.assertTrue(p.jigsaws().stream()
                        .anyMatch(j -> j.pool().getPath().equals("piglin_trading_post/trading_post")),
                "the trading post should assemble its jigsaw");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void piglinTradingPostOverworldEasterEggGrowsTheCottage(GameTestHelper helper) {
        // Easter egg: thrown topside the Nether-native trading post doesn't fizzle — an overworld biome_override grows
        // a grass island and an overworld-dimensioned rare structure (chance 1.0) swaps the hall for the abandoned
        // cottage (the Hamlet's 10% rare structure).
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme post = theme(overworld, "piglin_trading_post");
        final var plains = overworld.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.PLAINS);
        helper.assertTrue(IslandGenerator.formValidFor(post, plains, 80, Level.OVERWORLD.location()),
                "the trading post should grow (not fizzle) in the overworld");

        final IslandPlan p = IslandGenerator.planIsland(overworld, new BlockPos(40, 80, 40), post, plains,
                RandomSource.create(5L));
        boolean grass = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.GRASS_BLOCK)) {
                grass = true;
            }
        }
        helper.assertTrue(grass, "the overworld easter egg should be a grass island");
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().getPath().equals("abandoned/cottage")),
                "the overworld easter egg should assemble the abandoned cottage");
        helper.assertTrue(p.jigsaws().stream()
                        .noneMatch(j -> j.pool().getPath().equals("piglin_trading_post/trading_post")),
                "the trading-post hall must not appear in the overworld");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void witherArenaIsNetherNativeWithItsJigsaw(GameTestHelper helper) {
        // Nether-native Wither Arena: the capstone venue. A blackstone island that assembles the hand-built obsidian
        // arena jigsaw, and (like the other nether structures) grows only in the Nether.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme arena = theme(overworld, "wither_arena");
        helper.assertTrue(arena.baseValidIn(Level.NETHER.location()), "wither arena must implement the_nether");
        helper.assertTrue(!arena.baseValidIn(Level.OVERWORLD.location()), "wither arena must be the_nether-only");

        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var wastes = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.NETHER_WASTES);
        helper.assertTrue(IslandGenerator.formValidFor(arena, wastes, 64, Level.NETHER.location()),
                "wither arena should grow in the Nether");

        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), arena, wastes,
                RandomSource.create(13L));
        boolean blackstone = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.BLACKSTONE)) {
                blackstone = true;
            }
        }
        helper.assertTrue(blackstone, "the wither arena island should be a blackstone island");
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().getPath().equals("wither_arena/wither_arena")),
                "the wither arena should assemble its jigsaw");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void forestInSnowDefersGroundCoverPastTrees(GameTestHelper helper) {
        // Regression: a Forest seed on snowy_plains came up bare — the 90% snow ground cover was placed before the
        // (deferred) spruce sites, and a snow layer fails the vanilla tree feature's valid-position check. Ground cover
        // is now recorded as scatter and placed AFTER the trees (GenerationJob), so it can never block one.
        final ServerLevel level = helper.getLevel();
        final IslandTheme forest = theme(level, "forest");
        final var snowy = level.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.SNOWY_PLAINS);
        final IslandPlan p = IslandGenerator.planIsland(level, new BlockPos(40, 80, 40), forest, snowy,
                RandomSource.create(3L));
        helper.assertTrue(!p.trees().isEmpty(), "a Forest island must always plan at least one tree");
        // every snow layer must be deferred scatter (placed after the trees), never an eager block that blocks them
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.SNOW)) {
                helper.assertTrue(p.scatterPositions().contains(bp.pos()),
                        "snow at " + bp.pos() + " is not deferred scatter — it would be placed before the trees");
            }
        }
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void structureConnectionsLinkAfterPlacement(GameTestHelper helper) {
        // Jigsaw placement copies blockstates verbatim, so panes/fences land unconnected; GenerationJob.linkConnections
        // re-derives them. Place three default-state glass panes in a row and confirm the middle one links E/W.
        final ServerLevel level = helper.getLevel();
        final BlockPos mid = helper.absolutePos(new BlockPos(5, 2, 8));
        // UPDATE_KNOWN_SHAPE suppresses the neighbour-shape update, leaving the panes unconnected the way a pasted
        // structure does (plain UPDATE_CLIENTS would re-link them on placement).
        for (int dx = -1; dx <= 1; dx++) {
            level.setBlock(mid.offset(dx, 0, 0), Blocks.GLASS_PANE.defaultBlockState(),
                    Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE);
        }
        final BlockState before = level.getBlockState(mid);
        helper.assertTrue(!before.getValue(BlockStateProperties.EAST) && !before.getValue(BlockStateProperties.WEST),
                "a pane placed with UPDATE_CLIENTS should start unconnected");
        GenerationJob.linkConnections(level, mid);
        final BlockState after = level.getBlockState(mid);
        helper.assertTrue(after.getValue(BlockStateProperties.EAST) && after.getValue(BlockStateProperties.WEST),
                "linkConnections should connect the middle pane to both neighbours");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void dimensionGateGrowsOrFizzlesByImplementation(GameTestHelper helper) {
        // The adapt-or-fizzle matrix (SKYNETHERPLAN): a seed grows only in dimensions it implements — its base
        // `dimensions` or a dimension-keyed override — and fizzles elsewhere rather than growing the foreign base.
        final ServerLevel level = helper.getLevel();
        final var biome = level.registryAccess().registryOrThrow(Registries.BIOME).getHolderOrThrow(Biomes.PLAINS);
        final ResourceLocation ow = Level.OVERWORLD.location();
        final ResourceLocation nether = Level.NETHER.location();
        final ResourceLocation end = Level.END.location();

        record Case(String theme, boolean ow, boolean nether, boolean end) {}
        final Case[] cases = {
            new Case("gametest/dim_overworld", true, false, false),
            new Case("gametest/dim_nether", false, true, false),
            new Case("gametest/dim_end", false, false, true),
            new Case("gametest/dim_ow_nether", true, true, false),
            new Case("gametest/dim_all", true, true, true),
        };
        for (Case c : cases) {
            final IslandTheme t = theme(level, c.theme());
            helper.assertTrue(IslandGenerator.formValidFor(t, biome, 100, ow) == c.ow(),
                    c.theme() + ": overworld should " + (c.ow() ? "grow" : "fizzle"));
            helper.assertTrue(IslandGenerator.formValidFor(t, biome, 100, nether) == c.nether(),
                    c.theme() + ": nether should " + (c.nether() ? "grow" : "fizzle"));
            helper.assertTrue(IslandGenerator.formValidFor(t, biome, 100, end) == c.end(),
                    c.theme() + ": end should " + (c.end() ? "grow" : "fizzle"));
        }
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void dimensionOverrideNeverInheritsOverworld(GameTestHelper helper) {
        // A Nether/End override is a complete spec — an unset field must NOT fall back to the overworld base. This
        // theme's base has coal ore + grass; its bare Nether override sets only the surface, so the Nether island
        // must carry none of that overworld content (neutral netherrack body, no coal, no grass).
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final var wastes = nether.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(Biomes.NETHER_WASTES);
        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40),
                theme(nether, "gametest/dim_leak"), wastes, RandomSource.create(1L));
        boolean netherrack = false;
        boolean coal = false;
        boolean grass = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.NETHERRACK)) netherrack = true;
            if (bp.state().is(Blocks.COAL_ORE)) coal = true;
            if (bp.state().is(Blocks.GRASS_BLOCK)) grass = true;
        }
        helper.assertTrue(netherrack, "a bare nether override should give a neutral netherrack body");
        helper.assertTrue(!coal, "a nether override must NOT inherit the base's overworld coal ore");
        helper.assertTrue(!grass, "a nether override must NOT inherit the base's overworld grass");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void biomeOverrideReplacesBodyFields(GameTestHelper helper) {
        // planIsland's per-field resolution, OVERRIDE-WINS branch (the merge that Finding 2 collapses into a helper):
        // a matching biome override replaces the base palette's surface/fill/core and the snow chance, while a
        // non-matching biome keeps the base. (The no-leak NEUTRAL branch is covered by dimensionOverrideNeverInherits-
        // Overworld; the BASE branch by everyThemePlansWithoutError and the plains tests.)
        final ServerLevel level = helper.getLevel();
        final IslandTheme t = theme(level, "gametest/override_wins");
        final var biomes = level.registryAccess().registryOrThrow(Registries.BIOME);
        final var desert = biomes.getHolderOrThrow(ResourceKey.create(Registries.BIOME, ResourceLocation.parse("minecraft:desert")));
        final var plains = biomes.getHolderOrThrow(ResourceKey.create(Registries.BIOME, ResourceLocation.parse("minecraft:plains")));

        // Desert matches the override: sand surface, sandstone fill, red_sandstone core, snow 1.0 — and no base blocks.
        final IslandPlan d = IslandGenerator.planIsland(level, new BlockPos(40, 80, 40), t, desert, RandomSource.create(3L));
        boolean sand = false, sandstone = false, redSandstone = false, baseLeakInDesert = false;
        for (IslandPlan.BlockPlacement bp : d.blocks()) {
            if (bp.state().is(Blocks.SAND)) sand = true;
            else if (bp.state().is(Blocks.SANDSTONE)) sandstone = true;
            else if (bp.state().is(Blocks.RED_SANDSTONE)) redSandstone = true;
            else if (bp.state().is(Blocks.GRASS_BLOCK) || bp.state().is(Blocks.DIRT) || bp.state().is(Blocks.STONE)) baseLeakInDesert = true;
        }
        helper.assertTrue(sand && sandstone && redSandstone,
                "desert override must replace surface/fill/core (sand=" + sand + " sandstone=" + sandstone + " red_sandstone=" + redSandstone + ")");
        helper.assertTrue(!baseLeakInDesert, "desert override must replace the base palette entirely (found grass/dirt/stone)");
        helper.assertTrue(d.snow() == 1.0f, "desert override must set snow=1.0 (got " + d.snow() + ")");

        // Plains does NOT match: the base palette stands, no desert blocks, snow stays off.
        final IslandPlan p = IslandGenerator.planIsland(level, new BlockPos(40, 80, 40), t, plains, RandomSource.create(3L));
        boolean grass = false, dirt = false, stone = false, desertLeakInPlains = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.GRASS_BLOCK)) grass = true;
            else if (bp.state().is(Blocks.DIRT)) dirt = true;
            else if (bp.state().is(Blocks.STONE)) stone = true;
            else if (bp.state().is(Blocks.SAND) || bp.state().is(Blocks.SANDSTONE) || bp.state().is(Blocks.RED_SANDSTONE)) desertLeakInPlains = true;
        }
        helper.assertTrue(grass && dirt && stone, "plains (no override) must keep the base grass/dirt/stone palette");
        helper.assertTrue(!desertLeakInPlains, "plains must not pick up the desert override's blocks");
        helper.assertTrue(p.snow() == 0.0f, "plains must keep the base snow=0 (got " + p.snow() + ")");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void shapeBuilderCapsSurfaceAndBuriesCore(GameTestHelper helper) {
        // Backs ShapeBuilder.build's terrain + column-list outputs (the buffers a parameter-object refactor bundles and
        // must keep distinct): the SURFACE block caps each column (never buried), and ORE — placed by OrePlanner from
        // the CORE column list — is always buried inside the body, never surfaced. A core/surface list mix-up would
        // expose the ore (or bury the grass), so this guards the refactor without reaching into the package-private
        // builder. (dim_leak in the overworld = grass/dirt/stone body, coal in the core, no decoration to muddy it.)
        final ServerLevel level = helper.getLevel();
        final IslandTheme t = theme(level, "gametest/dim_leak");
        final var plains = level.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolderOrThrow(ResourceKey.create(Registries.BIOME, ResourceLocation.parse("minecraft:plains")));
        final IslandPlan p = IslandGenerator.planIsland(level, new BlockPos(40, 80, 40), t, plains, RandomSource.create(5L));
        final java.util.Set<BlockPos> solid = new java.util.HashSet<>();
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            solid.add(bp.pos());
        }
        int grass = 0, coal = 0;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.GRASS_BLOCK)) {
                grass++;
                helper.assertTrue(!solid.contains(bp.pos().above()),
                        "a surface (grass) block must cap its column — found one buried under solid");
            } else if (bp.state().is(Blocks.COAL_ORE)) {
                coal++;
                helper.assertTrue(solid.contains(bp.pos().above()),
                        "a core ore block must be buried (solid above it), never surfaced");
            }
        }
        helper.assertTrue(grass > 0 && coal > 0,
                "expected a grass-capped body with buried coal (grass=" + grass + " coal=" + coal + ")");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void islandIsDeterministic(GameTestHelper helper) {
        final IslandPlan a = plan(helper, "rocky", 42L);
        final IslandPlan b = plan(helper, "rocky", 42L);
        helper.assertTrue(a.blocks().size() == b.blocks().size(),
                "same seed gave different block counts (" + a.blocks().size() + " vs " + b.blocks().size() + ")");
        helper.assertTrue(a.blocks().get(0).pos().equals(b.blocks().get(0).pos()),
                "same seed gave a different first block position");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void islandBlocksSortedBottomUp(GameTestHelper helper) {
        final IslandPlan p = plan(helper, "rocky", 7L);
        int prevY = Integer.MIN_VALUE;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            helper.assertTrue(bp.pos().getY() >= prevY, "block list is not sorted bottom-up (grow-in animation relies on it)");
            prevY = bp.pos().getY();
        }
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void rockyHasOre(GameTestHelper helper) {
        // Rocky carries ores; the exact set varies with germination Y (deepslate variants when deep), so match
        // any "*_ore" block rather than a fixed list. Over several seeds at least one vein should land.
        boolean anyOre = false;
        for (long seed = 0; seed < 6 && !anyOre; seed++) {
            for (IslandPlan.BlockPlacement bp : plan(helper, "rocky", seed).blocks()) {
                if (BuiltInRegistries.BLOCK.getKey(bp.state().getBlock()).getPath().endsWith("_ore")) {
                    anyOre = true;
                    break;
                }
            }
        }
        helper.assertTrue(anyOre, "rocky islands produced no ore across 6 seeds");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void placementRejectsOverlapAndPlayers(GameTestHelper helper) {
        // The fit check must allow open sky (so islands can sit adjacent), reject real overlap (engulfment), and
        // reject burying a player.
        final IslandPlan island = plan(helper, "rocky", 1L);
        final BlockPos center = helper.absolutePos(new BlockPos(8, 8, 8));
        final java.util.List<Vec3> noPlayers = java.util.List.of();

        helper.assertTrue(IslandPlacement.check(island, noPlayers, (x, y, z) -> false).ok(),
                "an island in open sky was wrongly rejected");

        // A solid column through the centre = real overlap (e.g. another island). Even though it's small relative to
        // the island, it must be rejected — the old 5%-of-size tolerance let big islands swallow small ones — and the
        // blocked centroid must point back at the column so the caller can push off it.
        final IslandPlacement.Occupancy column = (x, y, z) ->
                Math.abs(x - center.getX()) <= 3 && Math.abs(z - center.getZ()) <= 3;
        final IslandPlacement.Fit blocked = IslandPlacement.check(island, noPlayers, column);
        helper.assertTrue(!blocked.ok(), "an island overlapping solid was not rejected (engulfment)");
        helper.assertTrue(Math.abs(blocked.blockedX() - center.getX()) <= 3 && Math.abs(blocked.blockedZ() - center.getZ()) <= 3,
                "the blocked centroid did not point at the obstruction");

        // A player whose body is where the island would place blocks -> buried, must not fit.
        helper.assertTrue(!IslandPlacement.check(island, java.util.List.of(Vec3.atCenterOf(center)), (x, y, z) -> false).ok(),
                "germinating with a block on the player was not rejected");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void bankSugarCaneStandsInWater(GameTestHelper helper) {
        // Every sugar cane the carver places must be able to survive — stacked on cane, or on dirt/sand with water
        // horizontally beside its *supporting* block. (The bug placed it on steep banks 3 up from the water, where
        // it instantly pops.)
        int totalCane = 0;
        for (long seed = 1; seed <= 8; seed++) {
            final java.util.Map<BlockPos, BlockState> map = new java.util.HashMap<>();
            for (IslandPlan.BlockPlacement bp : plan(helper, "gametest/water", seed).blocks()) {
                map.put(bp.pos(), bp.state());
            }
            for (var e : map.entrySet()) {
                if (!e.getValue().is(Blocks.SUGAR_CANE)) {
                    continue;
                }
                totalCane++;
                final BlockPos below = e.getKey().below();
                final BlockState ground = map.get(below);
                final boolean onCane = ground != null && ground.is(Blocks.SUGAR_CANE);
                final boolean onWetSoil = ground != null
                        && (ground.is(BlockTags.DIRT) || ground.is(Blocks.SAND) || ground.is(Blocks.RED_SAND))
                        && (isWaterAt(map, below.east()) || isWaterAt(map, below.west())
                            || isWaterAt(map, below.north()) || isWaterAt(map, below.south()));
                helper.assertTrue(onCane || onWetSoil,
                        "sugar cane at " + e.getKey() + " would pop — no cane below and no water beside its support");
            }
        }
        helper.assertTrue(totalCane > 0, "no sugar cane grew across 8 water-island seeds to verify");
        helper.succeed();
    }

    private static boolean isWaterAt(java.util.Map<BlockPos, BlockState> map, BlockPos p) {
        final BlockState s = map.get(p);
        return s != null && s.getFluidState().is(FluidTags.WATER);
    }

    @GameTest(template = REGION)
    public static void everySeedRecipeAndBookEntryMatchesSeedKind(GameTestHelper helper) {
        // Auto-discovered from the registry maps, so a new seed is covered with no test edit. A regular seed must be
        // craftable, have a field-notes entry that carries its recipe, and a `gathered_<seed>` advancement (the page
        // gate that reveals the recipe once the player holds the makings); a debug seed must have none of those.
        final ServerLevel level = helper.getLevel();
        final java.util.Set<Item> craftable = new java.util.HashSet<>();
        for (var r : level.getRecipeManager().getRecipes()) {
            craftable.add(r.value().getResultItem(level.registryAccess()).getItem());
        }
        for (var e : ModItems.SEEDS.entrySet()) {
            final String theme = e.getKey();
            helper.assertTrue(craftable.contains(e.getValue().get()), "seed '" + theme + "' has no crafting recipe");
            final String entry = readResource(entryPath(theme));
            helper.assertTrue(entry != null, "seed '" + theme + "' has no field-notes entry (" + entryPath(theme) + ")");
            helper.assertTrue(entry.contains(theme + "_skyseed"),
                    "seed '" + theme + "' field-notes entry does not carry its crafting recipe");
            helper.assertTrue(resourceExists(gatheredPath(theme)),
                    "seed '" + theme + "' has no gathered-materials advancement (" + gatheredPath(theme) + ")");
            // Every seed but the Forest root is gated by a reveal advancement (crafted prereq OR held all ingredients).
            if (theme.equals("forest")) {
                helper.assertTrue(!resourceExists(revealPath(theme)), "the Forest root must not be reveal-gated");
            } else {
                helper.assertTrue(resourceExists(revealPath(theme)),
                        "seed '" + theme + "' has no reveal advancement (" + revealPath(theme) + ")");
                helper.assertTrue(entry.contains("reveal_" + theme),
                        "seed '" + theme + "' entry is not gated by its reveal advancement");
            }
        }
        for (var e : ModItems.DEBUG_SEEDS.entrySet()) {
            final String theme = e.getKey();
            helper.assertTrue(!craftable.contains(e.getValue().get()), "debug seed '" + theme + "' must not be craftable");
            helper.assertTrue(!resourceExists(entryPath(theme)), "debug seed '" + theme + "' must not have a field-notes entry");
            helper.assertTrue(!resourceExists(gatheredPath(theme)), "debug seed '" + theme + "' must not have a gathered advancement");
            helper.assertTrue(!resourceExists(revealPath(theme)), "debug seed '" + theme + "' must not have a reveal advancement");
        }
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void everyCraftableSeedHasUniqueIcon(GameTestHelper helper) {
        // Each regular seed's item model must point at its own texture (no two share one — that is how the Ocean
        // Monument seed slipped through reusing the generic island_seed icon), and that texture must exist.
        final java.util.Map<String, String> byTexture = new java.util.HashMap<>();
        for (String theme : ModItems.SEEDS.keySet()) {
            final String layer0 = modelLayer0(theme);
            helper.assertTrue(layer0 != null, "seed '" + theme + "' item model has no layer0 texture");
            helper.assertTrue(resourceExists(texturePath(layer0)), "seed '" + theme + "' icon texture is missing (" + layer0 + ")");
            final String other = byTexture.put(layer0, theme);
            helper.assertTrue(other == null, "seeds '" + theme + "' and '" + other + "' share the icon '" + layer0 + "'");
        }
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void legacyDimensionResetRewritesGeneratorSettings(GameTestHelper helper) {
        // /emptynether|/emptyend rewrite one settings string in level.dat; guard the NBT navigation (the bit that
        // would silently no-op or, worse, corrupt a save, if the path were wrong) against a synthetic level.dat.
        CompoundTag root = legacyLevelDat("minecraft:the_nether", "minecraft:nether");
        helper.assertTrue(SkyseedCommands.swapDimensionSettings(root, "minecraft:the_nether", "skyseed:void_nether"),
                "swap should report a change when the generator is still vanilla");
        String now = root.getCompound("Data").getCompound("WorldGenSettings").getCompound("dimensions")
                .getCompound("minecraft:the_nether").getCompound("generator").getString("settings");
        helper.assertTrue("skyseed:void_nether".equals(now), "settings should now be skyseed:void_nether but was " + now);
        // Idempotent: a second run is a no-op (so a re-armed reset never re-wipes already-void chunks).
        helper.assertTrue(!SkyseedCommands.swapDimensionSettings(root, "minecraft:the_nether", "skyseed:void_nether"),
                "swap should report no change when already void");
        // Safe: an unexpected structure is left alone rather than half-written.
        helper.assertTrue(!SkyseedCommands.swapDimensionSettings(new CompoundTag(), "minecraft:the_nether", "skyseed:void_nether"),
                "swap should refuse an empty/foreign level.dat");
        helper.succeed();
    }

    /** A minimal {@code level.dat} root holding one dimension's generator, mirroring the real nesting. */
    private static CompoundTag legacyLevelDat(String dimKey, String settingsId) {
        CompoundTag generator = new CompoundTag();
        generator.putString("settings", settingsId);
        CompoundTag dimension = new CompoundTag();
        dimension.put("generator", generator);
        CompoundTag dimensions = new CompoundTag();
        dimensions.put(dimKey, dimension);
        CompoundTag worldGen = new CompoundTag();
        worldGen.put("dimensions", dimensions);
        CompoundTag data = new CompoundTag();
        data.put("WorldGenSettings", worldGen);
        CompoundTag root = new CompoundTag();
        root.put("Data", data);
        return root;
    }

    // --- book/icon coverage helpers ---

    /** Patchouli field-notes entry path for a theme — note large variants flip to a {@code large_} prefix. */
    private static String entryPath(String theme) {
        final String name = theme.endsWith("_large")
                ? "large_" + theme.substring(0, theme.length() - "_large".length()) + "_island"
                : theme + "_island";
        return "/assets/skyseed/patchouli_books/guide/en_us/entries/" + name + ".json";
    }

    /** Path to a seed's gathered-materials advancement — the Patchouli page gate that reveals its recipe. */
    private static String gatheredPath(String theme) {
        return "/data/skyseed/advancement/gathered_" + theme + ".json";
    }

    /** Path to a seed's reveal advancement — the gate that unhides its book entry (crafted prereq or held makings). */
    private static String revealPath(String theme) {
        return "/data/skyseed/advancement/reveal_" + theme + ".json";
    }

    /** The {@code layer0} texture id from a seed's item model, or {@code null}. */
    private static String modelLayer0(String theme) {
        final String json = readResource("/assets/skyseed/models/item/" + theme + "_skyseed.json");
        if (json == null) {
            return null;
        }
        final com.google.gson.JsonObject root = com.google.gson.JsonParser.parseString(json).getAsJsonObject();
        if (!root.has("textures")) {
            return null;
        }
        final com.google.gson.JsonObject tex = root.getAsJsonObject("textures");
        return tex.has("layer0") ? tex.get("layer0").getAsString() : null;
    }

    /** Resource path of the PNG a {@code namespace:item/name} texture id refers to. */
    private static String texturePath(String textureId) {
        final int colon = textureId.indexOf(':');
        return "/assets/" + textureId.substring(0, colon) + "/textures/" + textureId.substring(colon + 1) + ".png";
    }

    private static boolean resourceExists(String path) {
        return SkyseedGameTests.class.getResource(path) != null;
    }

    private static String readResource(String path) {
        try (java.io.InputStream in = SkyseedGameTests.class.getResourceAsStream(path)) {
            return in == null ? null : new String(in.readAllBytes(), java.nio.charset.StandardCharsets.UTF_8);
        } catch (java.io.IOException ex) {
            return null;
        }
    }

    @GameTest(template = REGION)
    public static void structureThemeRecordsJigsaw(GameTestHelper helper) {
        // Hamlet is a jigsaw village; planning it must record a JigsawSite for GenerationJob to assemble.
        final IslandPlan p = plan(helper, "hamlet", 3L);
        helper.assertTrue(!p.jigsaws().isEmpty(), "hamlet plan recorded no jigsaw site");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void mansionGarrisonPlanned(GameTestHelper helper) {
        // The Woodland Mansion's evoker→totem garrison comes from the theme animals pack; it must be planned.
        final IslandPlan p = plan(helper, "woodland_mansion", 5L);
        boolean evoker = false;
        for (IslandPlan.AnimalSpawn a : p.animals()) {
            if (a.type() == net.minecraft.world.entity.EntityType.EVOKER) {
                evoker = true;
                break;
            }
        }
        helper.assertTrue(evoker, "woodland mansion planned no evoker (the guaranteed totem source)");
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void mansionAssemblesWithFlushWings(GameTestHelper helper) {
        // Assemble the mansion jigsaw and confirm the wings attach FLUSH (the overlap fix): the core lays a birch floor,
        // dark-oak walls and glass-pane windows, and the wings actually connect — the jigsaw only accepts a wing whose
        // box doesn't overlap the core's, so a wing-specific block (iron bars / lectern / barrel) landing beyond the
        // core proves the connection resolved instead of being rejected. (Loads dev-generated .nbt.)
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 4, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("woodland_mansion/start"));
        Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 2, origin, false, "", 0, null, 1L);
        int birch = 0, darkOak = 0, glass = 0, wingBlocks = 0;
        for (int x = 0; x < 48; x++) {
            for (int z = 0; z < 48; z++) {
                for (int y = 1; y <= 18; y++) {
                    final BlockState s = helper.getBlockState(new BlockPos(x, y, z));
                    if (s.is(Blocks.BIRCH_PLANKS)) birch++;
                    else if (s.is(Blocks.DARK_OAK_PLANKS)) darkOak++;
                    else if (s.is(Blocks.GLASS_PANE)) glass++;
                    else if (s.is(Blocks.IRON_BARS) || s.is(Blocks.LECTERN) || s.is(Blocks.BARREL)) wingBlocks++;
                }
            }
        }
        helper.assertTrue(birch > 80, "the mansion should lay a birch-plank floor (got " + birch + ")");
        helper.assertTrue(darkOak > 80, "the mansion should have dark-oak walls (got " + darkOak + ")");
        helper.assertTrue(glass > 0, "the mansion should have glass-pane windows (got " + glass + ")");
        helper.assertTrue(wingBlocks > 0,
                "at least one wing must attach flush — no wing block found means the wings were rejected for overlap");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void mansionCoresHaveDistinctFootprints(GameTestHelper helper) {
        // Real footprint variety: the start pool offers three core shapes, so the silhouette differs each throw.
        // Load each core template and require three distinct X×Z footprints. (Loads dev-generated .nbt.)
        final var mgr = helper.getLevel().getServer().getStructureManager();
        final var footprints = new java.util.HashSet<String>();
        for (final String name : new String[]{"core_square", "core_long", "core_wide"}) {
            final var t = mgr.get(Ids.mod("woodland_mansion/" + name));
            helper.assertTrue(t.isPresent(), "missing mansion core template: " + name);
            final var sz = t.get().getSize();
            footprints.add(sz.getX() + "x" + sz.getZ());
        }
        helper.assertTrue(footprints.size() == 3,
                "the three mansion cores must have distinct footprints, saw " + footprints);
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void villageHousesUseVanillaBlocks(GameTestHelper helper) {
        // The village houses follow the vanilla village frame: stripped-log corner posts, a cobblestone foundation,
        // and glass-pane windows. Assemble a hamlet cottage and confirm all three landed. (Loads dev-generated .nbt.)
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 4, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("hamlet/cottages"));
        Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 1, origin, false, "", 0, null, 1L);
        boolean strippedPost = false, pane = false, cobble = false;
        for (int x = 0; x < 48; x++) {
            for (int z = 0; z < 48; z++) {
                for (int y = 1; y <= 12; y++) {
                    final BlockState s = helper.getBlockState(new BlockPos(x, y, z));
                    if (s.is(Blocks.STRIPPED_OAK_LOG) || s.is(Blocks.STRIPPED_SPRUCE_LOG) || s.is(Blocks.STRIPPED_BIRCH_LOG)) strippedPost = true;
                    else if (s.is(Blocks.GLASS_PANE)) pane = true;
                    else if (s.is(Blocks.COBBLESTONE)) cobble = true;
                }
            }
        }
        helper.assertTrue(strippedPost, "village house must use stripped-log corner posts (the vanilla frame)");
        helper.assertTrue(pane, "village house windows must be glass panes");
        helper.assertTrue(cobble, "village house must sit on a cobblestone foundation");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void endPortalEdgesCraftFromShardAndRelics(GameTestHelper helper) {
        // Phase-1 (End chapter) collect-a-thon: each of the four portal edges is a shapeless craft of one Portal Frame
        // Shard + two structure relics. Resolving the recipe also proves every ingredient id is a registered item.
        final ServerLevel level = helper.getLevel();
        final String[][] edges = {
                {"grand_edge", "mansion_relic", "monument_relic"}, {"temple_edge", "desert_relic", "jungle_relic"},
                {"camp_edge", "trial_relic", "outpost_relic"}, {"nether_edge", "fortress_relic", "bastion_relic"}};
        for (final String[] e : edges) {
            final net.minecraft.world.item.crafting.CraftingInput input =
                    net.minecraft.world.item.crafting.CraftingInput.of(3, 1, java.util.List.of(
                            new net.minecraft.world.item.ItemStack(ModItems.PARTS.get("portal_frame_shard").get()),
                            new net.minecraft.world.item.ItemStack(ModItems.PARTS.get(e[1]).get()),
                            new net.minecraft.world.item.ItemStack(ModItems.PARTS.get(e[2]).get())));
            final var recipe = level.getRecipeManager().getRecipeFor(
                    net.minecraft.world.item.crafting.RecipeType.CRAFTING, input, level);
            helper.assertTrue(recipe.isPresent(), "no crafting recipe for " + e[0] + " (shard + " + e[1] + " + " + e[2] + ")");
            helper.assertTrue(recipe.get().value().assemble(input, level.registryAccess()).is(ModItems.PARTS.get(e[0]).get()),
                    e[0] + " recipe did not produce the edge");
        }
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void endPortalSeedCraftsFromFourEdges(GameTestHelper helper) {
        // Phase-1 payoff: the four portal edges set in a cross forge the End Portal Seed (which grows the portal chamber).
        final ServerLevel level = helper.getLevel();
        final net.minecraft.world.item.ItemStack none = net.minecraft.world.item.ItemStack.EMPTY;
        final java.util.function.Function<String, net.minecraft.world.item.ItemStack> part =
                id -> new net.minecraft.world.item.ItemStack(ModItems.PARTS.get(id).get());
        final net.minecraft.world.item.crafting.CraftingInput input =
                net.minecraft.world.item.crafting.CraftingInput.of(3, 3, java.util.List.of(
                        none, part.apply("grand_edge"), none,
                        part.apply("camp_edge"), none, part.apply("temple_edge"),
                        none, part.apply("nether_edge"), none));
        final var recipe = level.getRecipeManager().getRecipeFor(
                net.minecraft.world.item.crafting.RecipeType.CRAFTING, input, level);
        helper.assertTrue(recipe.isPresent(), "no crafting recipe for the End Portal Seed from the four edges");
        helper.assertTrue(recipe.get().value().assemble(input, level.registryAccess())
                        .is(ModItems.SEEDS.get("end_portal").get()),
                "the edge cross did not produce the End Portal Seed");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void endPortalDropsSeedIntoStructureLoot(GameTestHelper helper) {
        // Phase-1 collect-a-thon: a global loot modifier seeds the Portal Frame Shard into dungeon loot and each relic
        // into its structure's loot table. Roll a couple of the targeted tables across fixed seeds (deterministic) and
        // confirm the modifier's item appears — proving the GLM is registered, loaded, and gated to the right table.
        final ServerLevel level = helper.getLevel();
        final String[][] cases = {
                {"minecraft:chests/simple_dungeon", "portal_frame_shard"},
                {"minecraft:chests/woodland_mansion", "mansion_relic"}};
        for (final String[] c : cases) {
            final var table = level.getServer().reloadableRegistries().getLootTable(
                    net.minecraft.resources.ResourceKey.create(net.minecraft.core.registries.Registries.LOOT_TABLE,
                            net.minecraft.resources.ResourceLocation.parse(c[0])));
            final var params = new net.minecraft.world.level.storage.loot.LootParams.Builder(level)
                    .withParameter(net.minecraft.world.level.storage.loot.parameters.LootContextParams.ORIGIN,
                            net.minecraft.world.phys.Vec3.ZERO)
                    .create(net.minecraft.world.level.storage.loot.parameters.LootContextParamSets.CHEST);
            boolean found = false;
            for (long seed = 1; seed <= 80 && !found; seed++) {
                for (final net.minecraft.world.item.ItemStack s : table.getRandomItems(params, seed)) {
                    if (s.is(ModItems.PARTS.get(c[1]).get())) { found = true; break; }
                }
            }
            helper.assertTrue(found, "the loot modifier never added " + c[1] + " to " + c[0] + " over 80 rolls");
        }
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void endPortalChamberHasTwelveEmptyFrames(GameTestHelper helper) {
        // The End Portal Seed grows the portal chamber: a stronghold room with the vanilla 12-frame End portal ring,
        // frames empty so the player lights it with Eyes of Ender. Assert exactly 12 frames, all empty, AND that every
        // frame faces the ring's centre — the inward-facing layout vanilla requires, so dropping the 12th eye in-game
        // actually opens the portal (a flipped frame would leave 12 frames that never form a portal). (Loads .nbt.)
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 4, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("end_portal/chamber"));
        Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 1, origin, false, "", 0, null, 1L);
        final java.util.List<BlockPos> frames = new java.util.ArrayList<>();
        final java.util.List<net.minecraft.core.Direction> facings = new java.util.ArrayList<>();
        int eyed = 0;
        for (int x = 0; x < 48; x++) {
            for (int z = 0; z < 48; z++) {
                for (int y = 1; y <= 12; y++) {
                    final BlockState s = helper.getBlockState(new BlockPos(x, y, z));
                    if (s.is(Blocks.END_PORTAL_FRAME)) {
                        frames.add(new BlockPos(x, y, z));
                        facings.add(s.getValue(net.minecraft.world.level.block.state.properties.BlockStateProperties.HORIZONTAL_FACING));
                        if (s.getValue(net.minecraft.world.level.block.state.properties.BlockStateProperties.EYE)) {
                            eyed++;
                        }
                    }
                }
            }
        }
        helper.assertTrue(frames.size() == 12, "the portal chamber must have exactly 12 end-portal frames (got " + frames.size() + ")");
        helper.assertTrue(eyed == 0, "the frames must start empty so the player lights the portal (eyed=" + eyed + ")");
        double sx = 0;
        double sz = 0;
        for (final BlockPos p : frames) {
            sx += p.getX();
            sz += p.getZ();
        }
        final double cx = sx / frames.size(); // the ring centre (rotation-invariant — works at any placed orientation)
        final double cz = sz / frames.size();
        for (int i = 0; i < frames.size(); i++) {
            final BlockPos p = frames.get(i);
            final double dx = p.getX() - cx;
            final double dz = p.getZ() - cz;
            final net.minecraft.core.Direction want = Math.abs(dz) > Math.abs(dx)
                    ? (dz < 0 ? net.minecraft.core.Direction.SOUTH : net.minecraft.core.Direction.NORTH)
                    : (dx < 0 ? net.minecraft.core.Direction.EAST : net.minecraft.core.Direction.WEST);
            helper.assertTrue(facings.get(i) == want,
                    "frame at " + p + " faces " + facings.get(i) + " but must face the ring centre (" + want + ") to form the portal");
        }
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void returnPortalShrineHasEndPortal(GameTestHelper helper) {
        // The Return Portal Seed (End-only) grows an end-stone shrine around an End exit portal; in the End that
        // end_portal block sends the player home to the overworld. Verify the 3x3 portal assembles. (Loads .nbt.)
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 4, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("return_portal/shrine"));
        Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 1, origin, false, "", 0, null, 1L);
        int portal = 0;
        for (int x = 0; x < 48; x++) {
            for (int z = 0; z < 48; z++) {
                for (int y = 1; y <= 6; y++) {
                    if (helper.getBlockState(new BlockPos(x, y, z)).is(Blocks.END_PORTAL)) {
                        portal++;
                    }
                }
            }
        }
        helper.assertTrue(portal == 9, "the return shrine should hold a 3x3 End exit portal (got " + portal + ")");
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void endCityHasPurpurTowerAndShipChest(GameTestHelper helper) {
        // Phase 3 flagship, now a full jigsaw (SKYENDCITYPLAN Phase 1): a base section + stacking overhanging tiers + a
        // terraced roof, plus the interim End ship. Verify it assembles AND stacks vertically (not one box) — purpur
        // built, magenta-glass windows, two loot chests (tower treasure + ship reward) and the bow's dragon head, with
        // purpur rising in tiers well above the base. Placed low so the tower fits the scanned region. (Loads .nbt.)
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 2, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("end_city/start"));
        Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 3, origin, false, "", 0, null, 1L);
        int purpur = 0, chests = 0, maxPurpurY = Integer.MIN_VALUE;
        boolean dragon = false, glass = false;
        for (int x = 0; x < 48; x++) {
            for (int z = 0; z < 48; z++) {
                for (int y = 0; y < 24; y++) {
                    final var st = helper.getBlockState(new BlockPos(x, y, z));
                    if (st.is(Blocks.PURPUR_BLOCK) || st.is(Blocks.PURPUR_PILLAR) || st.is(Blocks.PURPUR_STAIRS) || st.is(Blocks.PURPUR_SLAB)) {
                        purpur++;
                        maxPurpurY = Math.max(maxPurpurY, y);
                    } else if (st.is(Blocks.CHEST)) {
                        chests++;
                    } else if (st.is(Blocks.DRAGON_HEAD)) {
                        dragon = true;
                    } else if (st.is(Blocks.MAGENTA_STAINED_GLASS)) {
                        glass = true;
                    }
                }
            }
        }
        helper.assertTrue(purpur > 80, "the End City should be built of purpur (got " + purpur + ")");
        helper.assertTrue(chests >= 2, "the End City needs a tower chest and a ship chest (got " + chests + ")");
        helper.assertTrue(dragon, "the End ship needs a dragon head at the bow");
        helper.assertTrue(glass, "the tiers should carry magenta-glass windows");
        // The base shell's ceiling sits ~8 above its floor (region y2); a stacked tier/roof puts purpur well above that.
        helper.assertTrue(maxPurpurY > 13, "the End City should stack tiers above its base, not be one box "
                + "(top purpur Y " + maxPurpurY + ")");
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void endCitySproutsTowers(GameTestHelper helper) {
        // Phase 2: thin towers branch off the tiers' side connectors and rise as spires. Position-seeded, so sample
        // seeds and assert the best sprouts a WEST tower that rises. The tiers' lip reaches region x19 and the start
        // sits at x20+; the ship is to the EAST — so any purpur WEST of x18 is a side tower, and its vertical span
        // proves it rose (not just a stub). Placed low so the spires fit the scanned region.
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 2, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("end_city/start"));
        int bestWestSpan = 0;
        for (long seed = 1; seed <= 5; seed++) {
            for (int x = 0; x < 48; x++) {
                for (int z = 0; z < 48; z++) {
                    for (int y = 0; y < 24; y++) {
                        helper.setBlock(new BlockPos(x, y, z), Blocks.AIR);
                    }
                }
            }
            Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 6, origin, false, "", 0, null, seed);
            int wMinY = Integer.MAX_VALUE, wMaxY = Integer.MIN_VALUE;
            for (int x = 0; x < 18; x++) {      // west of the tiers' lip (x19) and start (x20) → tower territory
                for (int z = 0; z < 48; z++) {
                    for (int y = 0; y < 24; y++) {
                        final var st = helper.getBlockState(new BlockPos(x, y, z));
                        if (st.is(Blocks.PURPUR_BLOCK) || st.is(Blocks.PURPUR_PILLAR)) {
                            wMinY = Math.min(wMinY, y);
                            wMaxY = Math.max(wMaxY, y);
                        }
                    }
                }
            }
            if (wMaxY > wMinY) {
                bestWestSpan = Math.max(bestWestSpan, wMaxY - wMinY);
            }
        }
        helper.assertTrue(bestWestSpan > 6, "the End City should sprout side towers that rise as spires, not stubs "
                + "(best west-tower purpur Y-span " + bestWestSpan + ")");
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void dragonTrophyMonumentHasEmptyEggPedestal(GameTestHelper helper) {
        // Phase 6 capstone: the Dragon Trophy grows a monument — a purpur-capped pedestal + four dragon heads — but
        // NO dragon egg (the egg is unique; the player sets their own). Verify it assembles and carries no egg. (Loads .nbt.)
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 4, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("dragon_trophy/monument"));
        Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 1, origin, false, "", 0, null, 1L);
        int heads = 0;
        boolean purpur = false, egg = false, brick = false;
        for (int x = 0; x < 48; x++) {
            for (int z = 0; z < 48; z++) {
                for (int y = 0; y < 24; y++) {
                    final var st = helper.getBlockState(new BlockPos(x, y, z));
                    if (st.is(Blocks.DRAGON_HEAD)) {
                        heads++;
                    } else if (st.is(Blocks.PURPUR_BLOCK)) {
                        purpur = true;
                    } else if (st.is(Blocks.DRAGON_EGG)) {
                        egg = true;
                    } else if (st.is(Blocks.END_STONE_BRICKS)) {
                        brick = true;
                    }
                }
            }
        }
        helper.assertTrue(brick, "the trophy dais should be end-stone bricks");
        helper.assertTrue(purpur, "the trophy pedestal should have a purpur cap");
        helper.assertTrue(heads >= 4, "the trophy should have four dragon heads (got " + heads + ")");
        helper.assertTrue(!egg, "the trophy must NOT include a dragon egg (it would duplicate the unique egg)");
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void grandOceanMonumentPlaces(GameTestHelper helper) {
        // SKYHUGEPLAN: the bigger (19x19) Ocean Monument — the rare 5% bonus on the Huge Aquatic seed. Verify it
        // assembles: a big prismarine basin, the eight-block gold cache, two buried-treasure chests, a sponge. (Loads .nbt.)
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 4, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("ocean_monument/grand"));
        Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 1, origin, false, "", 0, null, 1L);
        int prismarine = 0, gold = 0, chests = 0;
        boolean sponge = false;
        for (int x = 0; x < 48; x++) {
            for (int z = 0; z < 48; z++) {
                for (int y = 0; y < 24; y++) {
                    final var st = helper.getBlockState(new BlockPos(x, y, z));
                    if (st.is(Blocks.PRISMARINE) || st.is(Blocks.PRISMARINE_BRICKS) || st.is(Blocks.DARK_PRISMARINE)) {
                        prismarine++;
                    } else if (st.is(Blocks.GOLD_BLOCK)) {
                        gold++;
                    } else if (st.is(Blocks.CHEST)) {
                        chests++;
                    } else if (st.is(Blocks.WET_SPONGE)) {
                        sponge = true;
                    }
                }
            }
        }
        helper.assertTrue(prismarine > 250, "the grand monument should be a big prismarine basin (got " + prismarine + ")");
        helper.assertTrue(gold == 8, "the grand monument should have an eight-block gold cache (got " + gold + ")");
        helper.assertTrue(chests == 2, "the grand monument should have two buried-treasure chests (got " + chests + ")");
        helper.assertTrue(sponge, "the grand monument should have a sponge niche");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void oceanMonumentWaterSitsFlush(GameTestHelper helper) {
        // The monument is sunk (sink = its 5-deep water height) so the pool surface lands flush with the island top, not
        // as a raised basin. The jigsaw assembles during placement (not in the plan), so check its recorded site: the
        // anchor lands at origin.y - 1 (the basin floor), the pool is 5 deep, so its surface (floor + 5) must be flush
        // with the island's surface Y — never above it (the old "weird pool"). Also catches an off-by-one in the sink.
        final ServerLevel level = helper.getLevel();
        final IslandTheme t = theme(level, "aquatic_large");
        final int idx = rareIndex(t, "ocean_monument/start");
        helper.assertTrue(idx >= 0, "aquatic_large should host the ocean monument rare structure");
        final BlockPos c = new BlockPos(40, 80, 40);
        final IslandPlan p = IslandGenerator.planIsland(level, c, t, level.getBiome(c),
                RandomSource.create(3L), DebugForce.rare(idx));
        final IslandPlan.JigsawSite site = p.jigsaws().stream()
                .filter(j -> j.pool().getPath().equals("ocean_monument/start")).findFirst().orElse(null);
        helper.assertTrue(site != null, "the forced monument should record a jigsaw site");
        int maxLandY = Integer.MIN_VALUE; // the island's own surface (the monument isn't in the plan, only its site)
        for (final IslandPlan.BlockPlacement bp : p.blocks()) {
            if (!bp.state().isAir() && !bp.state().is(Blocks.WATER)) {
                maxLandY = Math.max(maxLandY, bp.pos().getY());
            }
        }
        final int poolTop = (site.origin().getY() - 1) + 5;
        helper.assertTrue(poolTop <= maxLandY, "the monument pool must sit flush, not above the island surface "
                + "(pool top " + poolTop + " vs land top " + maxLandY + ")");
        helper.assertTrue(poolTop >= maxLandY - 1, "the monument pool should reach the island surface "
                + "(pool top " + poolTop + " vs land top " + maxLandY + ")");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void sunkTankAnimalsSpawnSubmerged(GameTestHelper helper) {
        // Regression: the flush (sunk) Ocean Monument's water sits a block BELOW the rolled spawn spot (which assumes a
        // raised surface pond), so every guardian used to be skipped — beached as dry air above the basin. submergedSpot
        // must walk down to the real water column and return a spot inside it.
        final ServerLevel level = helper.getLevel();
        final BlockPos surf = helper.absolutePos(new BlockPos(4, 9, 4));     // the flush water surface
        for (int dy = 0; dy <= 4; dy++) {
            level.setBlock(surf.below(dy), Blocks.WATER.defaultBlockState(), 3);   // a 5-deep basin
        }
        level.setBlock(surf.below(5), Blocks.PRISMARINE.defaultBlockState(), 3);   // basin floor
        final BlockPos rolled = surf.above();                               // the buggy rolled spot: air above the surface
        helper.assertTrue(level.getFluidState(rolled).isEmpty(), "setup: the rolled spot must be dry air");
        final BlockPos wet = GenerationJob.submergedSpot(level, rolled);
        helper.assertTrue(wet != null, "submergedSpot must find the sunk basin water below the dry rolled spot");
        helper.assertTrue(!level.getFluidState(wet).isEmpty(), "the chosen spawn spot must be water");
        helper.assertTrue(wet.getY() < surf.getY(), "the spawn spot must be submerged below the water surface");
        // Over dry land (no tank water within reach) it must return null, so we skip rather than beach the mob on sand.
        final BlockPos dry = helper.absolutePos(new BlockPos(10, 9, 10));
        helper.assertTrue(GenerationJob.submergedSpot(level, dry) == null, "dry land must yield no spawn spot");
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void ancientCityPlaces(GameTestHelper helper) {
        // SKYHUGEPLAN §3: the Ancient City — the rare 6% bonus on the Huge Ancient seed. Verify it assembles: a
        // deepslate plaza, three ancient_city loot chests, a can-summon sculk shrieker (the Warden danger), the blue
        // soul fire, and a sculk catalyst. (Loads the .nbt.)
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 4, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("ancient_city/plaza"));
        Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 1, origin, false, "", 0, null, 1L);
        int deepslate = 0, chests = 0;
        boolean canSummon = false, soulFire = false, catalyst = false;
        for (int x = 0; x < 48; x++) {
            for (int z = 0; z < 48; z++) {
                for (int y = 0; y < 24; y++) {
                    final var st = helper.getBlockState(new BlockPos(x, y, z));
                    if (st.is(Blocks.DEEPSLATE_TILES) || st.is(Blocks.DEEPSLATE_BRICKS)
                            || st.is(Blocks.CRACKED_DEEPSLATE_TILES) || st.is(Blocks.CRACKED_DEEPSLATE_BRICKS)) {
                        deepslate++;
                    } else if (st.is(Blocks.CHEST)) {
                        chests++;
                    } else if (st.is(Blocks.SCULK_SHRIEKER)
                            && st.getValue(net.minecraft.world.level.block.state.properties.BlockStateProperties.CAN_SUMMON)) {
                        canSummon = true;
                    } else if (st.is(Blocks.SOUL_FIRE)) {
                        soulFire = true;
                    } else if (st.is(Blocks.SCULK_CATALYST)) {
                        catalyst = true;
                    }
                }
            }
        }
        helper.assertTrue(deepslate > 150, "the ancient city should be a big deepslate plaza (got " + deepslate + ")");
        helper.assertTrue(chests == 3, "the ancient city should have three loot chests (got " + chests + ")");
        helper.assertTrue(canSummon, "the ancient city should have a can-summon sculk shrieker (the Warden danger)");
        helper.assertTrue(soulFire, "the ancient city should keep its blue soul fire");
        helper.assertTrue(catalyst, "the ancient city should have a sculk catalyst");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void autoDebugSeedsCoverOverridesAndRares(GameTestHelper helper) {
        // The debug seeds are derived at construction by ThemeScanner from the theme JSON — one per biome override and
        // per rare structure. Confirm the scan actually produced both kinds (not a silent empty result).
        int biomeForced = 0, rareForced = 0;
        for (var holder : ModItems.DEBUG_SEEDS.values()) {
            final var item = holder.get();
            if (item.forcedRareIndex() >= 0) {
                rareForced++;
            } else if (item.forcedBiome() != null) {
                biomeForced++;
            }
        }
        helper.assertTrue(biomeForced > 10, "auto scan should make many biome-override debug seeds (got " + biomeForced + ")");
        helper.assertTrue(rareForced > 0, "auto scan should make rare-structure debug seeds (got " + rareForced + ")");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void debugForcedRareGerminatesItsStructure(GameTestHelper helper) {
        // forcedRare bypasses the chance roll: huge_ancient's rare structure #0 (the Ancient City) must land in the plan.
        final ServerLevel level = helper.getLevel();
        final BlockPos c = helper.absolutePos(new BlockPos(8, 4, 8));
        final IslandPlan p = IslandGenerator.planIsland(level, c, theme(level, "huge_ancient"), level.getBiome(c),
                RandomSource.create(1L), DebugForce.rare(0));
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().getPath().startsWith("ancient_city")),
                "forcedRare=0 on huge_ancient should germinate the ancient_city structure");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void debugForcedWaterfallGerminatesWaterColumn(GameTestHelper helper) {
        // forcedWaterfall pins the ladder island's rare water-column variant: the centre shaft comes up as water, not ladders.
        final ServerLevel level = helper.getLevel();
        final BlockPos c = helper.absolutePos(new BlockPos(8, 4, 8));
        final IslandPlan p = IslandGenerator.planIsland(level, c, theme(level, "ladder_small"), level.getBiome(c),
                RandomSource.create(1L), new DebugForce(-1, true));
        helper.assertTrue(p.blocks().stream().anyMatch(b -> b.state().is(Blocks.WATER)),
                "a forced-waterfall ladder island should drop a water column");
        helper.assertTrue(p.blocks().stream().noneMatch(b -> b.state().is(Blocks.LADDER)),
                "a forced-waterfall ladder island should have no ladders");
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void sprawlingDungeonAssembles(GameTestHelper helper) {
        // SKYDUNGEONPLAN Part A: the dungeon_complex jigsaw must sprawl past its start hub — i.e. the doorway connectors
        // align so corridors/rooms actually attach (a misalignment would leave only the start, ~300 cobble). Assert the
        // sprawl is bigger than the lone hub and carries dungeon content (a spawner and/or a loot chest). Loads .nbt.
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 4, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("dungeon_complex/start"));
        Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 4, origin, false, "", 0, null, 7L);
        int cobble = 0;
        for (int x = 0; x < 48; x++) {
            for (int z = 0; z < 48; z++) {
                for (int y = 0; y < 24; y++) {
                    final var st = helper.getBlockState(new BlockPos(x, y, z));
                    if (st.is(Blocks.COBBLESTONE) || st.is(Blocks.MOSSY_COBBLESTONE)) {
                        cobble++;
                    }
                }
            }
        }
        // The lone hub is ~300 cobble; more than that means corridors/rooms attached (the doorway connectors align).
        // Which rooms land is position-seeded, so room content is verified by direct placement, not by scanning a run.
        helper.assertTrue(cobble > 400, "the dungeon should sprawl past its start hub (cobble " + cobble
                + " — connectors likely misaligned if ~300)");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void dungeonComplexRoomsCarryContent(GameTestHelper helper) {
        // The pieces bake their content by construction; verify directly (the assembled run's room mix is position-
        // seeded, so it isn't deterministic across the test set). A spawner room = a spawner + loot chests.
        final BlockPos sp = place(helper, "skyseed:dungeon_complex/spawner_zombie");
        helper.assertTrue(contains(helper, sp, 6, 4, 6, Blocks.SPAWNER), "spawner_zombie should bake a mob spawner");
        helper.assertTrue(contains(helper, sp, 6, 4, 6, Blocks.CHEST), "spawner_zombie should bake loot chests");
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void dungeonComplexGoesVertical(GameTestHelper helper) {
        // With descending staircase + ladder-shaft pieces in the pool, the complex should drop below its start level —
        // verticality, not just sprawl. The run is position-seeded, so sample a few seeds and assert the best descends:
        // the assembled cobble spans well beyond a single room's height (a stair drops ~4, a shaft ~6).
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 14, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("dungeon_complex/start"));
        int bestSpan = 0;
        for (long seed = 1; seed <= 4; seed++) {
            for (int x = 0; x < 48; x++) {
                for (int z = 0; z < 48; z++) {
                    for (int y = 0; y < 24; y++) {
                        helper.setBlock(new BlockPos(x, y, z), Blocks.AIR);
                    }
                }
            }
            Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 5, origin, false, "", 0, null, seed);
            int minY = Integer.MAX_VALUE;
            int maxY = Integer.MIN_VALUE;
            for (int x = 0; x < 48; x++) {
                for (int z = 0; z < 48; z++) {
                    for (int y = 0; y < 24; y++) {
                        final var st = helper.getBlockState(new BlockPos(x, y, z));
                        if (st.is(Blocks.COBBLESTONE) || st.is(Blocks.MOSSY_COBBLESTONE)) {
                            minY = Math.min(minY, y);
                            maxY = Math.max(maxY, y);
                        }
                    }
                }
            }
            if (maxY > minY) {
                bestSpan = Math.max(bestSpan, maxY - minY);
            }
        }
        helper.assertTrue(bestSpan > 9, "the dungeon should descend via stairs/shafts, not just sprawl flat "
                + "(best cobble Y-span " + bestSpan + ")");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void dungeonStairsOnlyDescend(GameTestHelper helper) {
        // Stairs/shafts must only go DOWN: the entry connector (skyseed:dungeon, which any parent targets) sits at the
        // piece TOP, and the descending exit uses a one-way name (skyseed:dungeon_down, which nothing targets) at the
        // BOTTOM — so a parent can never enter from the low connector and make the stair climb. Checked on the templates.
        final ServerLevel level = helper.getLevel();
        for (final String id : new String[]{"dungeon_complex/stairs_down", "dungeon_complex/shaft"}) {
            final StructureTemplate t = level.getStructureManager().get(skyseed(id)).orElseThrow();
            int entryY = Integer.MIN_VALUE;
            int downY = Integer.MAX_VALUE;
            int entries = 0;
            int downs = 0;
            for (final var j : t.filterBlocks(BlockPos.ZERO, new StructurePlaceSettings(), Blocks.JIGSAW)) {
                final String name = j.nbt() == null ? "" : j.nbt().getString("name");
                if (name.equals("skyseed:dungeon")) {
                    entries++;
                    entryY = Math.max(entryY, j.pos().getY());
                } else if (name.equals("skyseed:dungeon_down")) {
                    downs++;
                    downY = Math.min(downY, j.pos().getY());
                }
            }
            helper.assertTrue(entries == 1, id + " should have exactly one entry connector (got " + entries + ")");
            helper.assertTrue(downs == 1, id + " should have exactly one one-way descending exit (got " + downs + ")");
            helper.assertTrue(downY < entryY,
                    id + " descending exit (y" + downY + ") must sit below the entry (y" + entryY + ")");
        }
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void abandonedMineshaftAssembles(GameTestHelper helper) {
        // SKYDUNGEONPLAN Part B: the mineshaft jigsaw must sprawl past its start hub (the rail connectors align). The
        // jigsaw is seeded from the origin chunk, so a single placement can roll the terminator on every connector;
        // sample a few seeds (resetting the platform between) and assert the BEST sprawled — an oak-plank floor well past
        // the ~32-plank lone hub, with the support-arch fences the hub (log pillars) lacks. Loads .nbt.
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 4, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("mineshaft/start"));
        int bestPlanks = 0;
        int bestFences = 0;
        for (long seed = 1; seed <= 4; seed++) {
            for (int x = 0; x < 48; x++) {
                for (int z = 0; z < 48; z++) {
                    for (int y = 0; y < 24; y++) {
                        helper.setBlock(new BlockPos(x, y, z), y <= 2 ? Blocks.DIRT : Blocks.AIR);
                    }
                }
            }
            Jigsaw.placeCapped(level, pool, Ids.mc("bottom"), 4, origin, false, "", 0, null, seed);
            int planks = 0;
            int fences = 0;
            for (int x = 0; x < 48; x++) {
                for (int z = 0; z < 48; z++) {
                    for (int y = 0; y < 24; y++) {
                        final var st = helper.getBlockState(new BlockPos(x, y, z));
                        if (st.is(Blocks.OAK_PLANKS)) {
                            planks++;
                        } else if (st.is(Blocks.OAK_FENCE)) {
                            fences++;
                        }
                    }
                }
            }
            bestPlanks = Math.max(bestPlanks, planks);
            bestFences = Math.max(bestFences, fences);
        }
        helper.assertTrue(bestPlanks > 60, "no sampled mineshaft sprawled past its hub (best planks " + bestPlanks + ")");
        helper.assertTrue(bestFences > 0, "no sampled mineshaft raised support arches (best fences " + bestFences + ")");
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void mineshaftMinecartEntitySpawns(GameTestHelper helper) {
        // The corridor_loot piece bakes a chest-minecart ENTITY (via the extended StructureWriter / Built.entities).
        // Placing the piece must spawn the minecart — proving the entities list round-trips through the .nbt.
        final BlockPos origin = place(helper, "skyseed:mineshaft/corridor_loot");
        helper.assertTrue(near(helper.getLevel(), origin, net.minecraft.world.entity.vehicle.MinecartChest.class),
                "the corridor_loot piece should spawn a chest minecart from its baked entity");
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void mineshaftTrestlesSupportOverVoid(GameTestHelper helper) {
        // SKYDUNGEONPLAN Part C: a mineshaft floor left over the void grows oak-fence trestle legs (the wood variant of
        // the village's dirt foundation, selected by the jigsaw's trestles flag). Lay a floating oak-plank deck over air
        // and run supportTrestles; legs should hang below it.
        final ServerLevel level = helper.getLevel();
        final BlockPos base = helper.absolutePos(new BlockPos(24, 12, 24)); // a deck floating high over air
        for (int dx = -1; dx <= 1; dx++) {
            for (int dz = -1; dz <= 1; dz++) {
                level.setBlock(base.offset(dx, 0, dz), Blocks.OAK_PLANKS.defaultBlockState(), 2);
            }
        }
        dev.gemberkoekje.skyseed.worldgen.structure.PathSurfacer.supportTrestles(level, base.above(), 4); // deck = origin.y-1
        boolean leg = false;
        for (int dx = -1; dx <= 1; dx++) {
            for (int dz = -1; dz <= 1; dz++) {
                if (level.getBlockState(base.offset(dx, -1, dz)).is(Blocks.OAK_FENCE)) {
                    leg = true;
                }
            }
        }
        helper.assertTrue(leg, "an over-void mineshaft floor should hang oak-fence trestle legs");
        helper.succeed();
    }

    @GameTest(template = BIG_REGION)
    public static void mesaMineshaftIsDarkOakWithGold(GameTestHelper helper) {
        // SKYDUNGEONPLAN Phase 4: the mesa variant reuses the mineshaft geometry in dark-oak with sprinkled gold (the
        // vanilla badlands mineshaft). Place a mesa room and assert dark-oak + gold, and NO oak.
        final BlockPos o = place(helper, "skyseed:mineshaft_mesa/room");
        helper.assertTrue(contains(helper, o, 6, 4, 6, Blocks.DARK_OAK_PLANKS), "the mesa mineshaft should be dark-oak");
        helper.assertTrue(contains(helper, o, 6, 4, 6, Blocks.GOLD_BLOCK), "the mesa mineshaft should sprinkle gold");
        helper.assertTrue(!contains(helper, o, 6, 4, 6, Blocks.OAK_PLANKS), "the mesa mineshaft should have no oak");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void returnPortalSeedCraftsFromEndStoneAndPearls(GameTestHelper helper) {
        // The End-only Return Portal Seed crafts from end stone + ender pearls — both obtainable in the End, so a
        // stranded player can build their way home without a trip back to the Nether.
        final ServerLevel level = helper.getLevel();
        final net.minecraft.world.item.ItemStack s = new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.END_STONE);
        final net.minecraft.world.item.ItemStack p = new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.ENDER_PEARL);
        final net.minecraft.world.item.crafting.CraftingInput input =
                net.minecraft.world.item.crafting.CraftingInput.of(3, 3, java.util.List.of(s, p, s, p, s, p, s, p, s));
        final var recipe = level.getRecipeManager().getRecipeFor(
                net.minecraft.world.item.crafting.RecipeType.CRAFTING, input, level);
        helper.assertTrue(recipe.isPresent(), "no crafting recipe for the Return Portal Seed from end stone + ender pearls");
        helper.assertTrue(recipe.get().value().assemble(input, level.registryAccess())
                        .is(ModItems.SEEDS.get("return_portal").get()),
                "the end stone + ender pearl ring did not produce the Return Portal Seed");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void chorusForestSeedCraftsFromChorusAndEndStone(GameTestHelper helper) {
        // Phase 3 (End-native content): the Chorus Forest seed crafts from chorus fruit (bootstrapped off the End's
        // outer islands) ringing end stone — both End-obtainable, so you grow a renewable chorus/purpur farm.
        final ServerLevel level = helper.getLevel();
        final net.minecraft.world.item.ItemStack c = new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.CHORUS_FRUIT);
        final net.minecraft.world.item.ItemStack e = new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.END_STONE);
        final net.minecraft.world.item.crafting.CraftingInput input =
                net.minecraft.world.item.crafting.CraftingInput.of(3, 3, java.util.List.of(c, e, c, e, c, e, c, e, c));
        final var recipe = level.getRecipeManager().getRecipeFor(
                net.minecraft.world.item.crafting.RecipeType.CRAFTING, input, level);
        helper.assertTrue(recipe.isPresent(), "no crafting recipe for the Chorus Forest Seed from chorus fruit + end stone");
        helper.assertTrue(recipe.get().value().assemble(input, level.registryAccess())
                        .is(ModItems.SEEDS.get("chorus_forest").get()),
                "the chorus fruit + end stone ring did not produce the Chorus Forest Seed");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void endCitySeedCraftsFromShulkerShellAndPurpur(GameTestHelper helper) {
        // Phase 3 flagship: the End City Seed crafts from purpur framing shulker shells around end stone — all
        // End-obtainable (bootstrapped from the outer islands' vanilla End Cities), so your own city is renewable.
        final ServerLevel level = helper.getLevel();
        final net.minecraft.world.item.ItemStack p = new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.PURPUR_BLOCK);
        final net.minecraft.world.item.ItemStack s = new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.SHULKER_SHELL);
        final net.minecraft.world.item.ItemStack e = new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.END_STONE);
        final net.minecraft.world.item.crafting.CraftingInput input =
                net.minecraft.world.item.crafting.CraftingInput.of(3, 3, java.util.List.of(p, s, p, p, e, p, p, s, p));
        final var recipe = level.getRecipeManager().getRecipeFor(
                net.minecraft.world.item.crafting.RecipeType.CRAFTING, input, level);
        helper.assertTrue(recipe.isPresent(), "no crafting recipe for the End City Seed from purpur + shulker shells + end stone");
        helper.assertTrue(recipe.get().value().assemble(input, level.registryAccess())
                        .is(ModItems.SEEDS.get("end_city").get()),
                "the purpur + shulker shell + end stone frame did not produce the End City Seed");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void dragonTrophySeedCraftsFromDragonBreath(GameTestHelper helper) {
        // Phase 6 capstone: the Dragon Trophy seed crafts from dragon's breath (the fight souvenir) ringed in obsidian
        // and end stone — post-dragon by construction, so the trophy is earned, not handed out.
        final ServerLevel level = helper.getLevel();
        final net.minecraft.world.item.ItemStack e = new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.END_STONE);
        final net.minecraft.world.item.ItemStack o = new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.OBSIDIAN);
        final net.minecraft.world.item.ItemStack d = new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.DRAGON_BREATH);
        final net.minecraft.world.item.crafting.CraftingInput input =
                net.minecraft.world.item.crafting.CraftingInput.of(3, 3, java.util.List.of(e, o, e, o, d, o, e, o, e));
        final var recipe = level.getRecipeManager().getRecipeFor(
                net.minecraft.world.item.crafting.RecipeType.CRAFTING, input, level);
        helper.assertTrue(recipe.isPresent(), "no crafting recipe for the Dragon Trophy Seed from dragon's breath + obsidian + end stone");
        helper.assertTrue(recipe.get().value().assemble(input, level.registryAccess())
                        .is(ModItems.SEEDS.get("dragon_trophy").get()),
                "the dragon's breath + obsidian + end stone ring did not produce the Dragon Trophy Seed");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void overworldBiomeThemesHaveEndForm(GameTestHelper helper) {
        // The 10 overworld biome themes (+ their large variants) grow in the End as same-size, pure end-stone islands
        // via a the_end biome override (end stone is the End's neutral default block; ores/decoration default off).
        // Guard that every one carries that override, so a newly-added biome theme can't silently fail to grow there.
        final ServerLevel level = helper.getLevel();
        final String[] bases = {"forest", "rocky", "desert", "mushroom", "frozen",
                                "meadow", "badlands", "ancient", "lush", "aquatic"};
        for (final String base : bases) {
            for (final String name : new String[]{base, base + "_large"}) {
                final IslandTheme t = theme(level, name);
                helper.assertTrue(t != null, "missing theme '" + name + "'");
                final boolean hasEnd = t.biomeOverrides().stream()
                        .anyMatch(o -> o.dimension().map("minecraft:the_end"::equals).orElse(false));
                helper.assertTrue(hasEnd, "theme '" + name + "' has no the_end biome override — it won't grow in the End");
            }
        }
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void forestAndLushEndFormsSplitByBiome(GameTestHelper helper) {
        // A Forest/Lush seed in the End splits by biome: the central the_end biome (the dragon-fight area) grows an
        // EMPTY normal end-stone island (a building platform), while the outer End biomes grow a SMALL island with a
        // little chorus (+ the rare shulker) — bootstrapping the End-City run but steering bulk chorus to the dedicated
        // Chorus Forest seed. (Chorus is a deferred tree feature, so this checks the override config, not placed blocks.)
        final ServerLevel level = helper.getLevel();
        for (final String name : new String[]{"forest", "forest_large", "lush", "lush_large"}) {
            final IslandTheme t = theme(level, name);
            helper.assertTrue(t != null, "missing theme '" + name + "'");
            final BiomeOverride central = t.biomeOverrides().stream()
                    .filter(o -> o.dimension().map("minecraft:the_end"::equals).orElse(false)
                            && o.biomes().contains("minecraft:the_end"))
                    .findFirst().orElse(null);
            final BiomeOverride outer = t.biomeOverrides().stream()
                    .filter(o -> o.dimension().map("minecraft:the_end"::equals).orElse(false) && o.biomes().isEmpty())
                    .findFirst().orElse(null);
            helper.assertTrue(central != null, "theme '" + name + "' has no central (the_end biome) End override");
            helper.assertTrue(outer != null, "theme '" + name + "' has no outer (dimension the_end) End override");
            helper.assertTrue(!endFormHasChorus(central), "the central End form of '" + name + "' must be EMPTY (no chorus)");
            helper.assertTrue(endFormHasChorus(outer), "the outer End form of '" + name + "' must grow a little chorus");
            final boolean shulker = outer.mobs().map(ms -> ms.stream().anyMatch(mo -> mo.entity().getPath().equals("shulker")))
                    .orElse(false);
            helper.assertTrue(shulker, "the outer End form of '" + name + "' must carry a shulker chance (shell bootstrap)");
            helper.assertTrue(central.shape().get().radius().min() > outer.shape().get().radius().min(),
                    "the central End island of '" + name + "' must be larger than the small outer chorus island");
        }
        helper.succeed();
    }

    /** Whether a biome override's variants grow chorus plants (the End-form bootstrap decoration). */
    private static boolean endFormHasChorus(BiomeOverride ov) {
        return ov.variants().map(vs -> vs.stream().anyMatch(
                v -> v.decoration().trees().stream().anyMatch(tr -> tr.feature().getPath().equals("chorus_plant"))))
                .orElse(false);
    }

    @GameTest(template = REGION)
    public static void endCityGatesToHighlandsAndMidlands(GameTestHelper helper) {
        // Phase 5 — End-biome gating: the End City grows only in its native biomes (end_highlands / end_midlands, where
        // vanilla End Cities live) and fizzles — with a hint — elsewhere in the End (central the_end, barrens, small islands).
        final ServerLevel level = helper.getLevel();
        final IslandTheme city = theme(level, "end_city");
        helper.assertTrue(city != null, "missing theme 'end_city'");
        final var lookup = level.registryAccess().lookupOrThrow(Registries.BIOME);
        final ResourceLocation end = Ids.mc("the_end");
        for (final ResourceKey<net.minecraft.world.level.biome.Biome> k : java.util.List.of(Biomes.END_HIGHLANDS, Biomes.END_MIDLANDS)) {
            helper.assertTrue(IslandGenerator.formValidFor(city, lookup.getOrThrow(k), 64, end),
                    "End City should grow in " + k.location());
        }
        for (final ResourceKey<net.minecraft.world.level.biome.Biome> k : java.util.List.of(Biomes.THE_END, Biomes.END_BARRENS, Biomes.SMALL_END_ISLANDS)) {
            final var b = lookup.getOrThrow(k);
            helper.assertTrue(!IslandGenerator.formValidFor(city, b, 64, end), "End City should fizzle in " + k.location());
            helper.assertTrue(city.fizzlesIn(b), "End City should show a fizzle hint in " + k.location());
        }
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void hugeForestSeedCraftsFromGate(GameTestHelper helper) {
        // SKYHUGEPLAN Phase 1: a huge seed's middle row is ender pearl / <theme>_large seed / blaze powder (the End +
        // Nether farm gate), wrapped in the theme's bulk block. Huge Forest = dirt around that gate.
        final ServerLevel level = helper.getLevel();
        final net.minecraft.world.item.ItemStack t = new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.DIRT);
        final net.minecraft.world.item.ItemStack e = new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.ENDER_PEARL);
        final net.minecraft.world.item.ItemStack l = new net.minecraft.world.item.ItemStack(ModItems.SEEDS.get("forest_large").get());
        final net.minecraft.world.item.ItemStack p = new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.BLAZE_POWDER);
        final net.minecraft.world.item.crafting.CraftingInput input =
                net.minecraft.world.item.crafting.CraftingInput.of(3, 3, java.util.List.of(t, t, t, e, l, p, t, t, t));
        final var recipe = level.getRecipeManager().getRecipeFor(net.minecraft.world.item.crafting.RecipeType.CRAFTING, input, level);
        helper.assertTrue(recipe.isPresent(), "no crafting recipe for the Huge Forest Seed (dirt + ender pearl + large + blaze powder)");
        helper.assertTrue(recipe.get().value().assemble(input, level.registryAccess()).is(ModItems.SEEDS.get("huge_forest").get()),
                "the gate recipe did not produce the Huge Forest Seed");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void hugeAquaticSeedCraftsFromGate(GameTestHelper helper) {
        // Same gate, sand-wrapped, consuming the Large Aquatic seed.
        final ServerLevel level = helper.getLevel();
        final net.minecraft.world.item.ItemStack t = new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.SAND);
        final net.minecraft.world.item.ItemStack e = new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.ENDER_PEARL);
        final net.minecraft.world.item.ItemStack l = new net.minecraft.world.item.ItemStack(ModItems.SEEDS.get("aquatic_large").get());
        final net.minecraft.world.item.ItemStack p = new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.BLAZE_POWDER);
        final net.minecraft.world.item.crafting.CraftingInput input =
                net.minecraft.world.item.crafting.CraftingInput.of(3, 3, java.util.List.of(t, t, t, e, l, p, t, t, t));
        final var recipe = level.getRecipeManager().getRecipeFor(net.minecraft.world.item.crafting.RecipeType.CRAFTING, input, level);
        helper.assertTrue(recipe.isPresent(), "no crafting recipe for the Huge Aquatic Seed");
        helper.assertTrue(recipe.get().value().assemble(input, level.registryAccess()).is(ModItems.SEEDS.get("huge_aquatic").get()),
                "the gate recipe did not produce the Huge Aquatic Seed");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void hugeIslandsAreSignificantlyBiggerThanLarge(GameTestHelper helper) {
        // SKYHUGEPLAN Phase 1: a huge island must dwarf its *_large counterpart, and plan without error — a basic
        // sizing + perf guard (single forest + the aquatic cluster) before the rest of the tier rolls out.
        final ServerLevel level = helper.getLevel();
        final BlockPos c = helper.absolutePos(new BlockPos(8, 8, 8));
        final var b = level.getBiome(c);
        final int largeF = IslandGenerator.planIsland(level, c, theme(level, "forest_large"), b, RandomSource.create(1L)).blocks().size();
        final int largeA = IslandGenerator.planIsland(level, c, theme(level, "aquatic_large"), b, RandomSource.create(1L)).blocks().size();
        final int hugeF = IslandGenerator.planIsland(level, c, theme(level, "huge_forest"), b, RandomSource.create(1L)).blocks().size();
        final int hugeA = IslandGenerator.planIsland(level, c, theme(level, "huge_aquatic"), b, RandomSource.create(1L)).blocks().size();
        helper.assertTrue(hugeF > largeF * 3 / 2, "huge_forest (" + hugeF + ") should dwarf forest_large (" + largeF + ")");
        helper.assertTrue(hugeA > largeA * 3 / 2, "huge_aquatic (" + hugeA + ") should dwarf aquatic_large (" + largeA + ")");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void hugeIslandsCarveDecoratedCaves(GameTestHelper helper) {
        // SKYHUGEPLAN Phase 2: huge land islands carve an internal cave system, dressed from the underside palette (or
        // a default of dripstone + glow lichen). huge_forest has no other dripstone/glow-lichen source, so any in its
        // plan came from the caves — assert some appear (the carver ran, the caves were decorated).
        final ServerLevel level = helper.getLevel();
        final BlockPos c = helper.absolutePos(new BlockPos(8, 8, 8));
        final var biome = level.getBiome(c);
        int caveDeco = 0;
        for (long seed = 1; seed <= 6; seed++) {
            final IslandPlan p = IslandGenerator.planIsland(level, c, theme(level, "huge_forest"), biome, RandomSource.create(seed));
            for (final var bp : p.blocks()) {
                if (bp.state().is(Blocks.POINTED_DRIPSTONE) || bp.state().is(Blocks.GLOW_LICHEN)) {
                    caveDeco++;
                }
            }
        }
        helper.assertTrue(caveDeco > 0, "huge_forest should carve decorated caves (dripstone/glow lichen) — got " + caveDeco);
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void hugeAquaticIsMostlyWater(GameTestHelper helper) {
        // Huge Aquatic should be mostly its central lake/ocean (Pond.extent 0.80), not a sand bank with a tiny pond.
        // Plan it and assert a large body of water — guards the "silly tiny ocean" regression (pond radius < extent cap).
        final ServerLevel level = helper.getLevel();
        final BlockPos c = helper.absolutePos(new BlockPos(8, 8, 8));
        final IslandPlan p = IslandGenerator.planIsland(level, c, theme(level, "huge_aquatic"), level.getBiome(c), RandomSource.create(1L));
        int water = 0;
        for (final var bp : p.blocks()) {
            if (bp.state().is(Blocks.WATER)) water++;
        }
        helper.assertTrue(water > 1500, "huge_aquatic should be mostly water (a big lake/ocean) — got " + water + " water blocks");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void everyThemePlansWithoutError(GameTestHelper helper) {
        // The broadest guard: plan every registered theme and require non-empty output. If the IslandGenerator
        // refactor breaks any theme (a codec field, a pond, a structure), this fails loudly.
        final ServerLevel level = helper.getLevel();
        final BlockPos center = helper.absolutePos(new BlockPos(8, 8, 8));
        final var themes = level.registryAccess().registryOrThrow(SkyseedRegistries.THEME);
        int n = 0;
        for (final ResourceLocation id : themes.keySet()) {
            final IslandPlan p = IslandGenerator.planIsland(level, center, themes.get(id),
                    level.getBiome(center), RandomSource.create(id.hashCode()));
            helper.assertTrue(!p.blocks().isEmpty(), "theme '" + id + "' planned no blocks");
            n++;
        }
        helper.assertTrue(n >= 10, "expected the full theme catalogue, only saw " + n);
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void riverPondCarvesWater(GameTestHelper helper) {
        // A river-style pond exercises riverColumns + the pond-plant/bank/water-mob paths.
        final IslandPlan p = plan(helper, "gametest/water", 4L);
        helper.assertTrue(planHas(p, Blocks.WATER), "the river carved no water (riverColumns)");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void mangroveAndWaterfallGenerate(GameTestHelper helper) {
        // A hand-built mangrove (buildMangrove/leafBlob) + a biome-override waterfall (placeWaterfalls).
        final IslandPlan p = plan(helper, "gametest/features", 4L);
        helper.assertTrue(planHas(p, Blocks.MANGROVE_LOG), "no hand-built mangrove (buildMangrove)");
        helper.assertTrue(planHas(p, Blocks.WATER), "no waterfall water (placeWaterfalls)");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void unknownThemeIdsFallBack(GameTestHelper helper) {
        // Every id in this theme is bogus; the generator must warn-and-skip and still build a grass island
        // (covers the resolveBlock/resolveScatter/resolveBands/resolveEntity/unknown-feature fallbacks).
        final IslandPlan p = plan(helper, "gametest/bad", 4L);
        helper.assertTrue(!p.blocks().isEmpty(), "the bad theme produced no blocks");
        helper.assertTrue(planHas(p, Blocks.GRASS_BLOCK), "surface did not fall back to grass");
        helper.succeed();
    }

    private static boolean planHas(IslandPlan p, Block b) {
        for (final IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(b)) {
                return true;
            }
        }
        return false;
    }

    @GameTest(template = REGION)
    public static void aquaticSubZeroHasLavaLake(GameTestHelper helper) {
        // Below Y0, an Aquatic island comes up as stone/deepslate with a 100% lava lake — not a water one.
        final ServerLevel level = helper.getLevel();
        final BlockPos base = helper.absolutePos(new BlockPos(8, 8, 8));
        final BlockPos deep = new BlockPos(base.getX(), -16, base.getZ());
        final IslandPlan plan = IslandGenerator.planIsland(level, deep, theme(level, "aquatic"),
                level.getBiome(deep), RandomSource.create(1L));
        helper.assertTrue(planHas(plan, Blocks.LAVA), "sub-zero Aquatic did not generate its lava lake");
        helper.assertTrue(!planHas(plan, Blocks.WATER), "sub-zero Aquatic still has water — the lava lake should replace it");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void islandOutputIsStable(GameTestHelper helper) {
        // Golden master: locks the EXACT generation output for a set of biome-independent themes, so a
        // behaviour-preserving refactor (the IslandGenerator split) is provably byte-identical, not just
        // "still produces an island". Update the GOLDEN constants ONLY for an intentional generation change.
        final String[][] cases = {
                {"gametest/island", "1"}, {"gametest/water", "4"}, {"gametest/features", "4"},
                {"gametest/structure", "11"}, {"gametest/bad", "4"},
        };
        final ServerLevel level = helper.getLevel();
        final BlockPos center = helper.absolutePos(new BlockPos(8, 8, 8));
        for (final String[] c : cases) {
            final IslandPlan p = IslandGenerator.planIsland(level, center, theme(level, c[0]),
                    level.getBiome(center), RandomSource.create(Long.parseLong(c[1])));
            long sum = 1L;
            for (final IslandPlan.BlockPlacement bp : p.blocks()) {
                // positions RELATIVE to the island centre so the fingerprint is run-location independent
                sum = sum * 1000003L + (bp.pos().getX() - center.getX());
                sum = sum * 1000003L + (bp.pos().getY() - center.getY());
                sum = sum * 1000003L + (bp.pos().getZ() - center.getZ());
                sum = sum * 1000003L + bp.state().toString().hashCode();
            }
            final String fp = p.blocks().size() + "/" + sum + "/" + p.trees().size() + "/" + p.mobs().size()
                    + "/" + p.animals().size() + "/" + p.jigsaws().size() + "/" + p.hives().size();
            final String key = c[0] + "#" + c[1];
            final String golden = GOLDEN.get(key);
            if (golden == null) {
                Skyseed.LOGGER.info("[golden] CAPTURE {} -> {}", key, fp);
            } else {
                helper.assertTrue(golden.equals(fp), "generation output changed for " + key + ": want " + golden + " got " + fp);
            }
        }
        helper.succeed();
    }

    /** Recorded fingerprints "blocks/checksum/trees/mobs/animals/jigsaws/hives" — the generation golden master. */
    private static final java.util.Map<String, String> GOLDEN = java.util.Map.of(
            "gametest/island#1", "213/-3285534759166012883/1/2/0/0/23",
            "gametest/water#4", "1240/-3972436084849772311/0/1/0/0/0",
            "gametest/features#4", "1356/2766402466658160625/0/0/0/0/0",
            "gametest/structure#11", "566/-538726431172054277/0/0/2/1/0",
            "gametest/bad#4", "197/5964512207029114459/0/0/0/1/0"
    );

    // --- structure templates (guard the template de-duplication) ---

    @GameTest(template = REGION)
    public static void outpostHasSpawnerAndCage(GameTestHelper helper) {
        final BlockPos o = place(helper, "skyseed:outpost/tower");
        helper.assertTrue(contains(helper, o, 14, 18, 14, Blocks.SPAWNER), "outpost lost its pillager spawner");
        helper.assertTrue(contains(helper, o, 14, 18, 14, Blocks.DARK_OAK_FENCE), "outpost lost its golem cage");
        helper.assertTrue(contains(helper, o, 14, 18, 14, Blocks.CHEST), "outpost lost its loot chest");
        // The cage (and railing) fences must link up — a cage-edge fence connects to its neighbour posts.
        final BlockState cageFence = helper.getLevel().getBlockState(o.offset(6, 1, 5));
        helper.assertTrue(cageFence.is(Blocks.DARK_OAK_FENCE)
                        && (cageFence.getValue(BlockStateProperties.EAST) || cageFence.getValue(BlockStateProperties.WEST)),
                "the golem-cage fences are not linked");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void outpostGolemFitsInCage(GameTestHelper helper) {
        // Guards the golem-suffocation fix: seated on the cage floor (where GenerationJob now spawns it), the
        // ~1.4-wide, ~2.7-tall golem must have no suffocating block in its eye-box — needs the all-fence pen
        // (a corner log there would suffocate it) and the floor-level spawn (one up jams its head in the ceiling).
        final BlockPos o = place(helper, "skyseed:outpost/tower");
        final IronGolem golem = EntityType.IRON_GOLEM.create(helper.getLevel());
        helper.assertTrue(golem != null, "could not create an iron golem");
        golem.moveTo(o.getX() + 6.5, o.getY() + 1, o.getZ() + 6.5, 0.0F, 0.0F);
        helper.getLevel().addFreshEntity(golem);
        helper.assertTrue(!golem.isInWall(), "the outpost golem is suffocating in its cage");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void trialHubHasBossAndOminousVault(GameTestHelper helper) {
        final BlockPos o = place(helper, "skyseed:trial_chamber/hub");
        helper.assertTrue(contains(helper, o, 8, 8, 8, Blocks.TRIAL_SPAWNER), "trial hub lost its breeze boss spawner");
        helper.assertTrue(contains(helper, o, 8, 8, 8, Blocks.VAULT), "trial hub lost its ominous vault");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void oceanMonumentHasPrismarineAndTreasure(GameTestHelper helper) {
        final BlockPos o = place(helper, "skyseed:ocean_monument/monument");
        helper.assertTrue(contains(helper, o, 14, 10, 14, Blocks.PRISMARINE_BRICKS), "monument lost its prismarine");
        helper.assertTrue(contains(helper, o, 14, 10, 14, Blocks.SEA_LANTERN), "monument lost its sea lanterns");
        helper.assertTrue(contains(helper, o, 14, 10, 14, Blocks.WATER), "monument's pool is not filled");
        helper.assertTrue(contains(helper, o, 14, 10, 14, Blocks.GOLD_BLOCK), "monument lost its gold treasure");
        helper.assertTrue(contains(helper, o, 14, 10, 14, Blocks.CHEST), "monument lost its treasure chest");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void oceanMonumentPlansSubmergedGuardian(GameTestHelper helper) {
        // The monument's guardians must be flagged to spawn in water (the AQUATIC set), or they'd beach and die.
        final IslandPlan p = plan(helper, "ocean_monument", 1L);
        boolean guardian = false;
        for (final IslandPlan.AnimalSpawn a : p.animals()) {
            if (a.type() == EntityType.GUARDIAN || a.type() == EntityType.ELDER_GUARDIAN) {
                helper.assertTrue(a.inWater(), "monument guardian is not flagged to spawn submerged");
                guardian = true;
            }
        }
        helper.assertTrue(guardian, "ocean monument planned no guardians");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void starterIslandBonusChest(GameTestHelper helper) {
        // The starter island places a stocked chest beside the spawn iff the "Generate Bonus Chest" option is on.
        final ServerLevel level = helper.getLevel();
        final BlockPos center = helper.absolutePos(new BlockPos(8, 5, 8));
        final BlockPos chestPos = center.offset(-1, 1, 0);
        StartIsland.build(level, center, false);
        helper.assertTrue(!level.getBlockState(chestPos).is(Blocks.CHEST), "bonus chest placed when the option is off");
        StartIsland.build(level, center, true);
        helper.assertTrue(level.getBlockState(chestPos).is(Blocks.CHEST), "bonus chest missing when the option is on");
        helper.assertTrue(level.getBlockEntity(chestPos) instanceof ChestBlockEntity c && !c.isEmpty(),
                "the bonus chest is empty");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void forestInBambooBiomeGrowsBamboo(GameTestHelper helper) {
        // Thrown over a bamboo jungle, a Forest island comes up as a bamboo forest.
        final ServerLevel level = helper.getLevel();
        final BlockPos center = helper.absolutePos(new BlockPos(8, 8, 8));
        final var bamboo = level.registryAccess().registryOrThrow(Registries.BIOME).getHolderOrThrow(Biomes.BAMBOO_JUNGLE);
        final IslandPlan p = IslandGenerator.planIsland(level, center, theme(level, "forest"), bamboo, RandomSource.create(3L));
        helper.assertTrue(planHas(p, Blocks.BAMBOO), "a Forest over a bamboo jungle grew no bamboo");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void lushHangsGlowLichen(GameTestHelper helper) {
        // The multiface glow lichen gets a valid UP face (not an empty state), and lush islands hang it underneath.
        helper.assertTrue(Blocks.GLOW_LICHEN.defaultBlockState().setValue(BlockStateProperties.UP, true)
                .getValue(BlockStateProperties.UP), "glow lichen has no UP face property");
        boolean found = false;
        for (long seed = 1; seed <= 8 && !found; seed++) {
            found = planHas(plan(helper, "lush", seed), Blocks.GLOW_LICHEN);
        }
        helper.assertTrue(found, "no lush island hung glow lichen across 8 seeds");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void aquaticReefHasCoral(GameTestHelper helper) {
        // A warm-ocean Aquatic reef grows small coral plants (and fans). Use Y 64 so the sub-zero stone override
        // doesn't win, and a warm-ocean biome so the reef override does.
        final ServerLevel level = helper.getLevel();
        final BlockPos base = helper.absolutePos(new BlockPos(8, 8, 8));
        final BlockPos center = new BlockPos(base.getX(), 64, base.getZ());
        final var warm = level.registryAccess().registryOrThrow(Registries.BIOME).getHolderOrThrow(Biomes.WARM_OCEAN);
        boolean coral = false;
        for (long seed = 1; seed <= 10 && !coral; seed++) {
            final IslandPlan p = IslandGenerator.planIsland(level, center, theme(level, "aquatic_large"), warm, RandomSource.create(seed));
            for (final IslandPlan.BlockPlacement bp : p.blocks()) {
                if (bp.state().getBlock() instanceof net.minecraft.world.level.block.CoralPlantBlock) {
                    coral = true;
                    break;
                }
            }
        }
        helper.assertTrue(coral, "warm-ocean Aquatic reef grew no small coral plants across 10 seeds");
        helper.succeed();
    }

    // --- world-apply: the throw → germinate → GenerationJob pipeline (covers IslandSeedEntity + GenerationJob) ---

    @GameTest(template = REGION, timeoutTicks = 200)
    public static void seedGerminatesIntoIsland(GameTestHelper helper) {
        // End-to-end: a thrown seed arms (~40 ticks), germinates, and IslandGrowth drains the GenerationJob.
        final ServerLevel level = helper.getLevel();
        final BlockPos center = helper.absolutePos(new BlockPos(8, 12, 8));
        final IslandSeedEntity seed = new IslandSeedEntity(ModEntities.ISLAND_SEED.get(), level);
        seed.setPos(center.getX() + 0.5, center.getY() + 0.5, center.getZ() + 0.5);
        seed.setTheme(skyseed("gametest/island"));
        seed.setNoGravity(true); // rest in place and arm rather than fall through the region floor
        level.addFreshEntity(seed);
        helper.succeedWhen(() -> {
            boolean grown = false;
            for (final BlockPos p : BlockPos.betweenClosed(center.offset(-6, -6, -6), center.offset(6, 4, 6))) {
                if (level.getBlockState(p).is(Blocks.GRASS_BLOCK)) {
                    grown = true;
                    break;
                }
            }
            helper.assertTrue(grown, "the thrown seed has not germinated into an island yet");
        });
    }

    @GameTest(template = REGION, timeoutTicks = 200)
    public static void generationJobBuildsStructureIsland(GameTestHelper helper) {
        // Drain a GenerationJob for a structure island directly: covers the block stream, placeStructures
        // (jigsaw cottage), the bed→villager scan, the iron-golem spawn and the animal pack.
        final ServerLevel level = helper.getLevel();
        final BlockPos center = helper.absolutePos(new BlockPos(8, 12, 8));
        final IslandPlan plan = IslandGenerator.planIsland(level, center, theme(level, "gametest/structure"),
                level.getBiome(center), RandomSource.create(11L));
        final GenerationJob job = new GenerationJob(level, plan);
        int guard = 0;
        while (!job.tick() && guard++ < 2000) {
            // drain the whole job synchronously (each tick() does a bounded slice)
        }
        helper.assertTrue(guard < 2000, "GenerationJob never reported completion");
        helper.assertTrue(contains(helper, center.offset(-8, -3, -8), 16, 12, 16, Blocks.RED_BED),
                "the cottage's bed was not placed (placeStructures)");
        helper.assertTrue(near(level, center, Villager.class), "no villager spawned at the bed");
        helper.assertTrue(near(level, center, IronGolem.class), "no iron golem spawned (iron_golems)");
        helper.assertTrue(near(level, center, Cow.class), "no animal-pack cow spawned (spawnEnclosureAnimals)");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void ladderIslandPunchesAShaftToALanding(GameTestHelper helper) {
        // The Ladder Island's whole point: a climbable shaft through the centre, hanging ~20 blocks below the island
        // to a 5x5 cobblestone landing at mining level. 5% of the time it comes up as a waterfall instead — a single
        // surface water source that physics carries down the open shaft. Verified on the (deterministic) plan.
        final ServerLevel level = helper.getLevel();
        final BlockPos center = helper.absolutePos(new BlockPos(8, 40, 8));
        final IslandTheme theme = theme(level, "ladder_small");
        boolean sawLadders = false;
        boolean sawWaterfall = false;
        for (long seed = 0; seed < 200 && !(sawLadders && sawWaterfall); seed++) {
            final IslandPlan p = IslandGenerator.planIsland(level, center, theme,
                    level.getBiome(center), RandomSource.create(seed));
            int centreLadders = 0;
            int centreWater = 0;
            final java.util.Set<Long> cobble = new java.util.HashSet<>();
            for (final IslandPlan.BlockPlacement bp : p.blocks()) {
                final boolean centreColumn = bp.pos().getX() == center.getX() && bp.pos().getZ() == center.getZ();
                if (bp.state().is(Blocks.LADDER)) {
                    if (centreColumn) {
                        centreLadders++;
                    }
                } else if (bp.state().is(Blocks.WATER)) {
                    if (centreColumn) {
                        centreWater++;
                    }
                } else if (bp.state().is(Blocks.COBBLESTONE)) {
                    cobble.add(bp.pos().asLong());
                }
            }
            // The landing level, found from a cobble block at the 5x5's edge (clear of the shaft + backing column).
            int landingY = Integer.MIN_VALUE;
            for (int y = center.getY(); y > center.getY() - 60; y--) {
                if (cobble.contains(new BlockPos(center.getX() + 2, y, center.getZ()).asLong())) {
                    landingY = y;
                    break;
                }
            }
            helper.assertTrue(landingY != Integer.MIN_VALUE && center.getY() - landingY > 18,
                    "landing should hang ~20 below the island (seed " + seed + "), landing dY=" + (center.getY() - landingY));
            final long centreLanding = new BlockPos(center.getX(), landingY, center.getZ()).asLong();
            final long centreCap = new BlockPos(center.getX(), landingY - 1, center.getZ()).asLong();
            if (centreLadders > 0) {
                sawLadders = true;
                helper.assertTrue(centreLadders > 15,
                        "ladder shaft too short (seed " + seed + "): " + centreLadders + " ladders");
                helper.assertTrue(cobble.contains(centreLanding),
                        "the ladder variant's landing centre should be solid (seed " + seed + ")");
                helper.assertTrue(p.fluidTicks().isEmpty(),
                        "the ladder variant should not schedule a waterfall (seed " + seed + ")");
            }
            if (centreWater > 0) {
                sawWaterfall = true;
                helper.assertTrue(centreWater == 1,
                        "the waterfall should be a single surface source, was " + centreWater + " (seed " + seed + ")");
                helper.assertTrue(p.fluidTicks().size() == 1,
                        "the waterfall source should be recorded for a fluid tick (seed " + seed + ")");
                helper.assertTrue(!cobble.contains(centreLanding),
                        "the waterfall landing centre should be an open drain (seed " + seed + ")");
                helper.assertTrue(cobble.contains(centreCap),
                        "the waterfall drain should be capped one block below (seed " + seed + ")");
            }
        }
        helper.assertTrue(sawLadders, "no seed produced the ladder shaft");
        helper.assertTrue(sawWaterfall, "the 5% waterfall easter egg never rolled in 200 seeds");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void largeLadderIslandPunchesDeeper(GameTestHelper helper) {
        // The Large Ladder Island drops further — its cobblestone landing hangs ~30 blocks below the island (vs ~20
        // for the small one). The landing depth is the same whether it comes up as ladders or a waterfall.
        final ServerLevel level = helper.getLevel();
        final BlockPos center = helper.absolutePos(new BlockPos(8, 50, 8));
        final IslandPlan p = IslandGenerator.planIsland(level, center, theme(level, "ladder_large"),
                level.getBiome(center), RandomSource.create(7L));
        int lowestCobbleY = Integer.MAX_VALUE;
        for (final IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.COBBLESTONE)) {
                lowestCobbleY = Math.min(lowestCobbleY, bp.pos().getY());
            }
        }
        helper.assertTrue(lowestCobbleY != Integer.MAX_VALUE && center.getY() - lowestCobbleY > 27,
                "the large ladder landing should hang ~30 below the island, dY=" + (center.getY() - lowestCobbleY));
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void ladderShaftRotationVaries(GameTestHelper helper) {
        // The shaft (and its cobblestone backing) faces a random one of the 4 directions per island, so they aren't
        // all aligned. Sweep seeds until we see at least two different ladder facings.
        final ServerLevel level = helper.getLevel();
        final BlockPos center = helper.absolutePos(new BlockPos(8, 40, 8));
        final IslandTheme theme = theme(level, "ladder_small");
        final java.util.Set<net.minecraft.core.Direction> facings = new java.util.HashSet<>();
        for (long seed = 0; seed < 60 && facings.size() < 2; seed++) {
            final IslandPlan p = IslandGenerator.planIsland(level, center, theme, level.getBiome(center),
                    RandomSource.create(seed));
            for (final IslandPlan.BlockPlacement bp : p.blocks()) {
                if (bp.state().is(Blocks.LADDER)) {
                    facings.add(bp.state().getValue(net.minecraft.world.level.block.LadderBlock.FACING));
                    break;
                }
            }
        }
        helper.assertTrue(facings.size() >= 2,
                "the ladder shaft should face different directions across islands, saw " + facings);
        helper.succeed();
    }

    @GameTest(template = REGION, timeoutTicks = 200)
    public static void preciseSeedGerminatesAtTarget(GameTestHelper helper) {
        // A Precise throw germinates at its chosen target, not where the seed sits.
        final ServerLevel level = helper.getLevel();
        final BlockPos spawn = helper.absolutePos(new BlockPos(3, 18, 3));
        final BlockPos target = helper.absolutePos(new BlockPos(10, 10, 10));
        final IslandSeedEntity seed = new IslandSeedEntity(ModEntities.ISLAND_SEED.get(), level);
        seed.setPos(spawn.getX() + 0.5, spawn.getY() + 0.5, spawn.getZ() + 0.5);
        seed.setTheme(skyseed("gametest/island"));
        seed.setPreciseTarget(new Vec3(target.getX() + 0.5, target.getY() + 0.5, target.getZ() + 0.5));
        seed.setNoGravity(true);
        level.addFreshEntity(seed);
        helper.succeedWhen(() -> {
            boolean atTarget = false;
            for (final BlockPos p : BlockPos.betweenClosed(target.offset(-6, -6, -6), target.offset(6, 4, 6))) {
                if (level.getBlockState(p).is(Blocks.GRASS_BLOCK)) {
                    atTarget = true;
                    break;
                }
            }
            helper.assertTrue(atTarget, "the precise seed has not germinated at its target yet");
        });
    }

    @GameTest(template = REGION)
    public static void seedStateRoundTripsThroughNbt(GameTestHelper helper) {
        // Save/load: theme + precise target must survive addAdditionalSaveData → readAdditionalSaveData.
        final ServerLevel level = helper.getLevel();
        final ResourceLocation theme = skyseed("gametest/island");
        final IslandSeedEntity a = new IslandSeedEntity(ModEntities.ISLAND_SEED.get(), level);
        a.setTheme(theme);
        a.setPreciseTarget(new Vec3(1.5, 2.5, 3.5));
        final CompoundTag tag = new CompoundTag();
        a.addAdditionalSaveData(tag);

        final IslandSeedEntity b = new IslandSeedEntity(ModEntities.ISLAND_SEED.get(), level);
        b.readAdditionalSaveData(tag);
        helper.assertTrue(theme.equals(b.getTheme()), "theme did not round-trip through NBT");
        helper.assertTrue(tag.getBoolean("Precise") && tag.getDouble("TY") == 2.5, "precise target did not round-trip");
        helper.succeed();
    }

    /** True if an entity of {@code cls} is within ~14 blocks of {@code c}. */
    private static boolean near(ServerLevel level, BlockPos c, Class<? extends Entity> cls) {
        return !level.getEntitiesOfClass(cls, new AABB(c).inflate(14)).isEmpty();
    }

    /** Place a single structure template (no rotation) at region-relative (1,1,1); returns its world origin. */
    private static BlockPos place(GameTestHelper helper, String id) {
        final ServerLevel level = helper.getLevel();
        final StructureTemplate tmpl = level.getStructureManager().get(skyseed(id.substring(id.indexOf(':') + 1)))
                .orElseThrow(() -> new IllegalStateException("structure '" + id + "' not found"));
        final BlockPos origin = helper.absolutePos(new BlockPos(1, 1, 1));
        tmpl.placeInWorld(level, origin, origin, new StructurePlaceSettings(), level.getRandom(), Block.UPDATE_CLIENTS);
        return origin;
    }

    /** True if any block in the [origin, origin+(dx,dy,dz)] box is {@code block}. */
    private static boolean contains(GameTestHelper helper, BlockPos origin, int dx, int dy, int dz, Block block) {
        final ServerLevel level = helper.getLevel();
        for (BlockPos p : BlockPos.betweenClosed(origin, origin.offset(dx, dy, dz))) {
            final BlockState state = level.getBlockState(p);
            if (state.is(block)) {
                return true;
            }
        }
        return false;
    }
}
