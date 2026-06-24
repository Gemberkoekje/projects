package dev.gemberkoekje.skyseed.gametest;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.command.SkyseedCommands;
import dev.gemberkoekje.skyseed.entity.IslandSeedEntity;
import dev.gemberkoekje.skyseed.registry.ModEntities;
import dev.gemberkoekje.skyseed.registry.ModItems;
import dev.gemberkoekje.skyseed.registry.SkyseedRegistries;
import dev.gemberkoekje.skyseed.worldgen.GenerationJob;
import dev.gemberkoekje.skyseed.worldgen.IslandGenerator;
import dev.gemberkoekje.skyseed.worldgen.IslandPlacement;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan;
import dev.gemberkoekje.skyseed.worldgen.StartIsland;
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
        final BlockPos toNether = IslandSeedEntity.linkedPortalPos(new BlockPos(800, 80, 80), Level.NETHER, nether);
        helper.assertTrue(toNether.getX() == 100 && toNether.getZ() == 10,
                "overworld->nether twin should divide X/Z by 8, was " + toNether);
        final BlockPos toOverworld = IslandSeedEntity.linkedPortalPos(new BlockPos(100, 70, 10), Level.OVERWORLD, overworld);
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

    @GameTest(template = REGION)
    public static void blazeRoomRollsOnLargeNetherSeedsAndDebugSeed(GameTestHelper helper) {
        // The surprise blaze spawner room (SKYNETHERPLAN): a 5% rare_structures roll on each of the 5 Large Nether
        // seeds, and the dedicated debug_blaze_spawner seed germinates it as a whole island.
        final ServerLevel overworld = helper.getLevel();
        for (String t : new String[] { "nether_rocky_large", "nether_lava_large", "nether_forest_large",
                "nether_soul_large", "nether_basalt_large" }) {
            final IslandTheme nt = theme(overworld, t);
            helper.assertTrue(nt.rareStructures().stream().anyMatch(
                            rs -> rs.jigsaw().pool().getPath().equals("nether_fortress/blaze_room")),
                    t + " should carry the 5% blaze spawner room rare structure");
        }
        final IslandTheme dbg = theme(overworld, "debug_blaze_spawner");
        final BlockPos c = new BlockPos(40, 80, 40);
        final IslandPlan p = IslandGenerator.planIsland(overworld, c, dbg, overworld.getBiome(c),
                RandomSource.create(140L));
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().getPath().equals("nether_fortress/blaze_room")),
                "the debug blaze spawner seed should assemble the blaze room jigsaw");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void netherFortressIsNetherNativeWithFortressJigsaw(GameTestHelper helper) {
        // Nether-native fortress island (SKYNETHERPLAN): a netherrack island that assembles the hand-built fortress
        // (arcaded bridge + keep with a caged blaze spawner). The structure itself is placed later by the generation
        // job, so the plan carries it as a jigsaw site.
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
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().getPath().equals("nether_fortress/fortress")),
                "the nether fortress island should assemble the fortress jigsaw");
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
        // craftable, listed in the recipe almanac, and have a field-notes entry; a debug seed must have none of those.
        final ServerLevel level = helper.getLevel();
        final java.util.Set<Item> craftable = new java.util.HashSet<>();
        for (var r : level.getRecipeManager().getRecipes()) {
            craftable.add(r.value().getResultItem(level.registryAccess()).getItem());
        }
        final String almanac = readResource("/assets/skyseed/patchouli_books/guide/en_us/entries/recipes.json");
        helper.assertTrue(almanac != null, "could not read the recipe almanac (recipes.json) from the classpath");

        for (var e : ModItems.SEEDS.entrySet()) {
            final String theme = e.getKey();
            final String id = e.getValue().getId().toString();
            helper.assertTrue(craftable.contains(e.getValue().get()), "seed '" + theme + "' has no crafting recipe");
            helper.assertTrue(almanac.contains(id), "seed '" + theme + "' is missing from the recipe almanac (recipes.json)");
            helper.assertTrue(resourceExists(entryPath(theme)), "seed '" + theme + "' has no field-notes entry (" + entryPath(theme) + ")");
        }
        for (var e : ModItems.DEBUG_SEEDS.entrySet()) {
            final String theme = e.getKey();
            final String id = e.getValue().getId().toString();
            helper.assertTrue(!craftable.contains(e.getValue().get()), "debug seed '" + theme + "' must not be craftable");
            helper.assertTrue(!almanac.contains(id), "debug seed '" + theme + "' must not be in the recipe almanac");
            helper.assertTrue(!resourceExists(entryPath(theme)), "debug seed '" + theme + "' must not have a field-notes entry");
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
