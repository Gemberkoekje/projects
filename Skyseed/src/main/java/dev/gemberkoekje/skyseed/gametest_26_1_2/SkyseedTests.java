package dev.gemberkoekje.skyseed.gametest_26_1_2;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.compat.Id;
import dev.gemberkoekje.skyseed.compat.Ids;
import dev.gemberkoekje.skyseed.compat.Lookup;
import dev.gemberkoekje.skyseed.registry.SkyseedRegistries;
import dev.gemberkoekje.skyseed.worldgen.IslandGenerator;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan;
import dev.gemberkoekje.skyseed.worldgen.theme.IslandTheme;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.Registries;
import net.minecraft.gametest.framework.GameTestHelper;
import net.minecraft.gametest.framework.TestData;
import net.minecraft.gametest.framework.TestEnvironmentDefinition;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.biome.Biome;
import net.minecraft.world.level.biome.Biomes;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.Rotation;
import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.fml.common.EventBusSubscriber;
import net.neoforged.neoforge.event.RegisterGameTestsEvent;

import java.util.HashSet;
import java.util.Set;
import java.util.function.Consumer;

/**
 * Skyseed's gametest suite for the <b>26.1.2+ node</b>, on the new {@link net.minecraft.gametest.framework.GameTestInstance}
 * framework (the {@code @GameTest}/{@code @GameTestHolder} annotation API was removed in 1.21.5+). This suite is fully
 * isolated from the 1.21.1 suite ({@code dev.gemberkoekje.skyseed.gametest}) by package + a per-version build.gradle
 * exclude, so the 1.21.1 suite stays the unchanging golden-master witness. Tests register in code via
 * {@link RegisterGameTestsEvent}; bodies are ported from the 1.21.1 suite. Coverage target + rollout in GAMETESTPLAN.md.
 *
 * <p>Because this suite is 26.1.2-only it is written natively against the 26.1.2 API (no {@code //?} directives): the
 * two version-specific idioms the 1.21.1 bodies use are funnelled through helpers — {@link #biome} (was
 * {@code registryAccess().registryOrThrow(BIOME).getHolderOrThrow(key)}) and {@link Lookup#dimensionId} (was
 * {@code Level.X.location().toString()}). Everything else ports verbatim.
 *
 * <p>All tests use the empty {@code skyseed:gametest/region} (16×24×16) / {@code big_region} templates — the same
 * committed {@code .nbt} resources the 1.21.1 suite uses (version-independent, so they ship unchanged on 26.1.2).
 */
@EventBusSubscriber(modid = Skyseed.MODID)
public final class SkyseedTests {
    private SkyseedTests() {}

    private static final Identifier REGION = Ids.mod("gametest/region");
    private static final Identifier BIG_REGION = Ids.mod("gametest/big_region");

    private static Holder<TestEnvironmentDefinition<?>> env;

    @SubscribeEvent
    static void onRegisterGameTests(RegisterGameTestsEvent event) {
        env = event.registerEnvironment(Ids.mod("default")); // one empty AllOf() batch — no special setup

        // --- generation invariants (guard the IslandGenerator split) ---
        reg(event, "island_generates_blocks", REGION, SkyseedTests::islandGeneratesBlocks);
        reg(event, "rocky_adapts_in_the_nether", REGION, SkyseedTests::rockyAdaptsInTheNether);
        reg(event, "rocky_rolls_whole_body_stone_type_variants", REGION, SkyseedTests::rockyRollsWholeBodyStoneTypeVariants);
        reg(event, "sand_and_gravel_always_have_solid_support", REGION, SkyseedTests::sandAndGravelAlwaysHaveSolidSupport);
        reg(event, "desert_adapts_in_the_nether", REGION, SkyseedTests::desertAdaptsInTheNether);
        reg(event, "badlands_adapts_in_the_nether", REGION, SkyseedTests::badlandsAdaptsInTheNether);
        reg(event, "aquatic_adapts_in_the_nether", REGION, SkyseedTests::aquaticAdaptsInTheNether);
        reg(event, "ancient_adapts_in_the_nether", REGION, SkyseedTests::ancientAdaptsInTheNether);
        reg(event, "mushroom_adapts_in_the_nether", REGION, SkyseedTests::mushroomAdaptsInTheNether);
        reg(event, "forest_adapts_in_the_nether", REGION, SkyseedTests::forestAdaptsInTheNether);
        reg(event, "lush_adapts_in_the_nether", REGION, SkyseedTests::lushAdaptsInTheNether);
        reg(event, "large_seeds_adapt_in_the_nether", REGION, SkyseedTests::largeSeedsAdaptInTheNether);
        reg(event, "nether_rocky_is_nether_native_and_full_size", REGION, SkyseedTests::netherRockyIsNetherNativeAndFullSize);
        reg(event, "nether_rocky_seed_makes_tiny_overworld_island", REGION, SkyseedTests::netherRockySeedMakesTinyOverworldIsland);
        reg(event, "nether_lava_is_full_size_in_both_dimensions", REGION, SkyseedTests::netherLavaIsFullSizeInBothDimensions);
        reg(event, "nether_forest_is_crimson_warped_with_tiny_overworld", REGION, SkyseedTests::netherForestIsCrimsonWarpedWithTinyOverworld);
        reg(event, "nether_soul_is_full_size_with_tiny_desert_overworld", REGION, SkyseedTests::netherSoulIsFullSizeWithTinyDesertOverworld);
        reg(event, "nether_basalt_is_full_size_with_tiny_badlands_overworld", REGION, SkyseedTests::netherBasaltIsFullSizeWithTinyBadlandsOverworld);
    }

    /** Build the standard per-test config and register it under {@code skyseed:<name>}. */
    private static void reg(RegisterGameTestsEvent event, String name, Identifier structure, Consumer<GameTestHelper> body) {
        final TestData<Holder<TestEnvironmentDefinition<?>>> data = new TestData<>(
                env, structure, /*maxTicks*/ 100, /*setupTicks*/ 0, /*required*/ true,
                Rotation.NONE, /*manualOnly*/ false, /*maxAttempts*/ 1, /*requiredSuccesses*/ 1,
                /*skyAccess*/ false, /*padding*/ 0);
        event.registerTest(Ids.mod(name), new SkyseedTest(data, body));
    }

    // ===== shared helpers (ported from the 1.21.1 suite) =====

    private static IslandTheme theme(ServerLevel level, String name) {
        final Registry<IslandTheme> reg = Lookup.registry(level.registryAccess(), SkyseedRegistries.THEME);
        final IslandTheme t = Lookup.byId(reg, Id.of(Ids.mod(name).toString()));
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

    /** The biome holder for a vanilla biome key on {@code level} (26.1.2: lookupOrThrow + getOrThrow). */
    private static Holder<Biome> biome(ServerLevel level, ResourceKey<Biome> key) {
        return level.registryAccess().lookupOrThrow(Registries.BIOME).getOrThrow(key);
    }

    // ===== tests =====

    static void islandGeneratesBlocks(GameTestHelper helper) {
        final IslandPlan p = plan(helper, "rocky", 1L);
        helper.assertTrue(!p.blocks().isEmpty(), "planIsland produced no blocks for 'rocky'");
        helper.assertTrue(p.blocks().size() > 100, "a rocky island should be more than 100 blocks");
        helper.succeed();
    }

    static void rockyAdaptsInTheNether(GameTestHelper helper) {
        // Thrown in the Nether, the Rocky seed adapts (SKYNETHERPLAN): a netherrack body over a blackstone core
        // instead of overworld stone/cobblestone. Plan against the live the_nether level so planIsland reads
        // dimension = the_nether and the dimension-gated biome override wins.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> netherWastes = biome(nether, Biomes.NETHER_WASTES);
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

    static void rockyRollsWholeBodyStoneTypeVariants(GameTestHelper helper) {
        // The Rocky line carries stone-type variants (stone/diorite/granite/andesite/tuff). A non-stone roll must
        // re-skin the WHOLE body through fill_override/core_override — not just the surface — so mining it actually
        // yields that block. Plan plains islands at a mid-altitude Y (clear of the deepslate/snow bands, which set
        // their own variants) across a deterministic seed range: plain stone stays the common case, and at least one
        // alternative appears with a fully converted body (no leftover cobblestone fill or stone core).
        final ServerLevel level = helper.getLevel();
        final Holder<Biome> plains = biome(level, Biomes.PLAINS);
        int stoneIslands = 0, altIslands = 0;
        boolean sawFullyConvertedAlt = false;
        for (long seed = 1; seed <= 40; seed++) {
            final IslandPlan p = IslandGenerator.planIsland(level, new BlockPos(40, 64, 40),
                    theme(level, "rocky"), plains, RandomSource.create(seed));
            boolean cobble = false, stone = false, alt = false;
            for (IslandPlan.BlockPlacement bp : p.blocks()) {
                if (bp.state().is(Blocks.COBBLESTONE)) cobble = true;
                else if (bp.state().is(Blocks.STONE)) stone = true;
                else if (bp.state().is(Blocks.DIORITE) || bp.state().is(Blocks.GRANITE)
                        || bp.state().is(Blocks.ANDESITE) || bp.state().is(Blocks.TUFF)) alt = true;
            }
            if (alt) {
                altIslands++;
                if (!cobble && !stone) sawFullyConvertedAlt = true;  // fill+core were overridden, not just the surface
            } else if (stone && cobble) {
                stoneIslands++;
            }
        }
        helper.assertTrue(stoneIslands > 0, "expected plain-stone Rocky islands (stone + cobblestone) across the range");
        helper.assertTrue(altIslands > 0, "expected stone-type-alternative Rocky islands (diorite/granite/andesite/tuff)");
        helper.assertTrue(sawFullyConvertedAlt,
                "an alternative Rocky island must convert its whole body (no leftover cobblestone fill / stone core)");
        helper.assertTrue(stoneIslands > altIslands,
                "plain stone must stay more common than the alternatives, got stone=" + stoneIslands + " alt=" + altIslands);
        helper.succeed();
    }

    static void sandAndGravelAlwaysHaveSolidSupport(GameTestHelper helper) {
        // Standing rule: no sand/gravel-type block may hang over the void. Plan islands that lay down sand/gravel
        // (desert = sand body, badlands = red sand, aquatic = gravel/clay veins + sandy pond beds) across seeds and
        // assert every gravity block has a non-air block directly below it in the plan (the supportFallingBlocks pass).
        int seen = 0;
        for (final String themeName : new String[]{"desert", "badlands", "aquatic", "aquatic_large"}) {
            for (long seed = 1; seed <= 6; seed++) {
                final IslandPlan p = plan(helper, themeName, seed);
                final Set<BlockPos> solid = new HashSet<>();
                for (final IslandPlan.BlockPlacement bp : p.blocks()) {
                    if (!bp.state().isAir()) solid.add(bp.pos());
                }
                for (final IslandPlan.BlockPlacement bp : p.blocks()) {
                    if (bp.state().is(Blocks.SAND) || bp.state().is(Blocks.RED_SAND) || bp.state().is(Blocks.GRAVEL)
                            || bp.state().is(Blocks.SUSPICIOUS_SAND) || bp.state().is(Blocks.SUSPICIOUS_GRAVEL)) {
                        seen++;
                        helper.assertTrue(solid.contains(bp.pos().below()),
                                themeName + " seed " + seed + ": " + bp.state().getBlock() + " at " + bp.pos()
                                        + " hangs over the void (no solid block below)");
                    }
                }
            }
        }
        helper.assertTrue(seen > 0, "test planted no sand/gravel to check — themes/biome no longer produce it");
        helper.succeed();
    }

    static void desertAdaptsInTheNether(GameTestHelper helper) {
        // Thrown in the Nether, Desert adapts (SKYNETHERPLAN): a Soul Sand Valley — soul sand over soul soil and a
        // basalt core — not the overworld sand island.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> ssv = biome(nether, Biomes.SOUL_SAND_VALLEY);
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

    static void badlandsAdaptsInTheNether(GameTestHelper helper) {
        // Thrown in the Nether, Badlands adapts (SKYNETHERPLAN): a Basalt Deltas fragment — blackstone + basalt,
        // with the overworld terracotta strata dropped (the override clears fill_bands).
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> deltas = biome(nether, Biomes.BASALT_DELTAS);
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

    static void aquaticAdaptsInTheNether(GameTestHelper helper) {
        // Thrown in the Nether, Aquatic adapts (SKYNETHERPLAN): a Lava Lagoon — the pond becomes a contained lava
        // basin on a basalt island, not the overworld water lake.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> wastes = biome(nether, Biomes.NETHER_WASTES);
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

    static void ancientAdaptsInTheNether(GameTestHelper helper) {
        // Thrown in the Nether, Ancient adapts (SKYNETHERPLAN): a haunted deep — a dark blackstone island over a
        // basalt core, not the overworld moss/deepslate.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> ssv = biome(nether, Biomes.SOUL_SAND_VALLEY);
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

    static void mushroomAdaptsInTheNether(GameTestHelper helper) {
        // Thrown in the Nether, Mushroom adapts (SKYNETHERPLAN): a calm mycelium pocket over netherrack (the
        // mooshroom food island), not the overworld dirt-and-stone island.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> wastes = biome(nether, Biomes.NETHER_WASTES);
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

    static void forestAdaptsInTheNether(GameTestHelper helper) {
        // Thrown in the Nether, Forest adapts (SKYNETHERPLAN): a fungal forest — crimson nylium over netherrack in a
        // crimson_forest biome — not the overworld grass-and-trees island.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> crimson = biome(nether, Biomes.CRIMSON_FOREST);
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

    static void lushAdaptsInTheNether(GameTestHelper helper) {
        // Thrown in the Nether, Lush adapts (SKYNETHERPLAN): a warped-nylium vine grotto over netherrack, with no
        // pond (the override omits one, so the base water pond is dropped, not evaporated to a dry hole).
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> warped = biome(nether, Biomes.WARPED_FOREST);
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

    static void largeSeedsAdaptInTheNether(GameTestHelper helper) {
        // Each Large terrain seed gets the same Nether form as its normal seed (just bigger). Spot-check that each
        // grows its Nether island — the expected Nether block present, no overworld block — when thrown in the Nether.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> wastes = biome(nether, Biomes.NETHER_WASTES);
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

    static void netherRockyIsNetherNativeAndFullSize(GameTestHelper helper) {
        // The first Tier-2 Nether-NATIVE seed (SKYNETHERPLAN): unlike an overworld seed's tiny Nether foothold, this
        // grows a FULL-SIZE mining island in the Nether (radius 6-9, like an overworld normal seed). Its BASE config
        // is the_nether only (the overworld form is just an easter-egg override — see netherRockySeedMakesTinyOverworldIsland).
        final IslandTheme nr = theme(helper.getLevel(), "nether_rocky");
        helper.assertTrue(nr.baseValidIn(Lookup.dimensionId(Level.NETHER)), "nether_rocky must implement the_nether");
        helper.assertTrue(!nr.baseValidIn(Lookup.dimensionId(Level.OVERWORLD)), "nether_rocky's BASE must not be an overworld implementation");

        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> wastes = biome(nether, Biomes.NETHER_WASTES);

        // Germination gate: valid (full base form) in the Nether.
        helper.assertTrue(IslandGenerator.formValidFor(nr, wastes, 64, Lookup.dimensionId(Level.NETHER)),
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

    static void netherRockySeedMakesTinyOverworldIsland(GameTestHelper helper) {
        // Easter egg (SKYNETHERPLAN): thrown in the OVERWORLD a Nether Rocky seed does NOT fizzle — it grows a TINY
        // plain rocky island (sparse iron + gold), and deepslate if thrown low enough. "Yeah, I figured that'd happen."
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme nr = theme(overworld, "nether_rocky");
        final Holder<Biome> plains = biome(overworld, Biomes.PLAINS);

        // It grows (does not fizzle) in the overworld via the easter-egg override.
        helper.assertTrue(IslandGenerator.formValidFor(nr, plains, 80, Lookup.dimensionId(Level.OVERWORLD)),
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

    static void netherLavaIsFullSizeInBothDimensions(GameTestHelper helper) {
        // Tier-2 Lava (SKYNETHERPLAN): a full-size lava-lagoon island. Nether-native, but because the overworld has
        // no real lava island it ALSO grows full-size topside (a stone-bodied volcanic isle, NOT the tiny easter egg
        // nether_rocky makes). Base is the_nether-only; the overworld form is a full dimension override.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme nl = theme(overworld, "nether_lava");
        helper.assertTrue(nl.baseValidIn(Lookup.dimensionId(Level.NETHER)), "nether_lava base must implement the_nether");
        helper.assertTrue(!nl.baseValidIn(Lookup.dimensionId(Level.OVERWORLD)),
                "nether_lava base must be the_nether-only (the overworld form is an override)");

        final Holder<Biome> plains = biome(overworld, Biomes.PLAINS);
        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> wastes = biome(nether, Biomes.NETHER_WASTES);

        helper.assertTrue(IslandGenerator.formValidFor(nl, wastes, 64, Lookup.dimensionId(Level.NETHER)),
                "nether_lava should grow in the Nether");
        helper.assertTrue(IslandGenerator.formValidFor(nl, plains, 80, Lookup.dimensionId(Level.OVERWORLD)),
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

    static void netherForestIsCrimsonWarpedWithTinyOverworld(GameTestHelper helper) {
        // Tier-2 Crimson/Warped (SKYNETHERPLAN): a full-size fungal forest. Crimson by default; warped nylium in a
        // warped_forest biome (a same-dimension biome override). Thrown topside it shrugs into a TINY grass island.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme nf = theme(overworld, "nether_forest");
        helper.assertTrue(nf.baseValidIn(Lookup.dimensionId(Level.NETHER)), "nether_forest base must implement the_nether");
        helper.assertTrue(!nf.baseValidIn(Lookup.dimensionId(Level.OVERWORLD)), "nether_forest base must be the_nether-only");

        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> crimson = biome(nether, Biomes.CRIMSON_FOREST);
        final Holder<Biome> warped = biome(nether, Biomes.WARPED_FOREST);
        final Holder<Biome> plains = biome(overworld, Biomes.PLAINS);

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

    static void netherSoulIsFullSizeWithTinyDesertOverworld(GameTestHelper helper) {
        // Tier-2 Soul (SKYNETHERPLAN): a full-size Soul Sand Valley — soul sand riddled with bone fossils. Thrown
        // topside it makes a TINY desert island instead. Nether-native base; the overworld form is a dimension override.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme ns = theme(overworld, "nether_soul");
        helper.assertTrue(ns.baseValidIn(Lookup.dimensionId(Level.NETHER)), "nether_soul base must implement the_nether");
        helper.assertTrue(!ns.baseValidIn(Lookup.dimensionId(Level.OVERWORLD)), "nether_soul base must be the_nether-only");

        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> wastes = biome(nether, Biomes.NETHER_WASTES);
        final Holder<Biome> plains = biome(overworld, Biomes.PLAINS);

        helper.assertTrue(IslandGenerator.formValidFor(ns, wastes, 64, Lookup.dimensionId(Level.NETHER)),
                "nether_soul should grow in the Nether");
        helper.assertTrue(IslandGenerator.formValidFor(ns, plains, 80, Lookup.dimensionId(Level.OVERWORLD)),
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

    static void netherBasaltIsFullSizeWithTinyBadlandsOverworld(GameTestHelper helper) {
        // Tier-2 Basalt (SKYNETHERPLAN): a full-size Basalt Deltas — basalt over blackstone, gilded blackstone in the
        // core. Thrown topside it makes a TINY badlands island. Nether-native base; the overworld form is an override.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme nb = theme(overworld, "nether_basalt");
        helper.assertTrue(nb.baseValidIn(Lookup.dimensionId(Level.NETHER)), "nether_basalt base must implement the_nether");
        helper.assertTrue(!nb.baseValidIn(Lookup.dimensionId(Level.OVERWORLD)), "nether_basalt base must be the_nether-only");

        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> wastes = biome(nether, Biomes.NETHER_WASTES);
        final Holder<Biome> plains = biome(overworld, Biomes.PLAINS);

        helper.assertTrue(IslandGenerator.formValidFor(nb, wastes, 64, Lookup.dimensionId(Level.NETHER)),
                "nether_basalt should grow in the Nether");
        helper.assertTrue(IslandGenerator.formValidFor(nb, plains, 80, Lookup.dimensionId(Level.OVERWORLD)),
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
}
