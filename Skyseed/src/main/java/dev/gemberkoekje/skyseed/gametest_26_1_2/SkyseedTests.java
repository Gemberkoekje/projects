package dev.gemberkoekje.skyseed.gametest_26_1_2;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.compat.Id;
import dev.gemberkoekje.skyseed.compat.Ids;
import dev.gemberkoekje.skyseed.compat.Jigsaw;
import dev.gemberkoekje.skyseed.compat.Lookup;
import dev.gemberkoekje.skyseed.registry.SkyseedRegistries;
import dev.gemberkoekje.skyseed.worldgen.DebugForce;
import dev.gemberkoekje.skyseed.worldgen.GenerationJob;
import dev.gemberkoekje.skyseed.worldgen.IslandGenerator;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan;
import dev.gemberkoekje.skyseed.worldgen.TwinPlacer;
import dev.gemberkoekje.skyseed.worldgen.structure.PathSurfacer;
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
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;
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
        reg(event, "ruined_portal_has_nether_variant_and_twins", REGION, SkyseedTests::ruinedPortalHasNetherVariantAndTwins);
        reg(event, "large_nether_seeds_are_full_size_nether_native", REGION, SkyseedTests::largeNetherSeedsAreFullSizeNetherNative);
        reg(event, "blaze_room_rolls_on_large_nether_seeds", REGION, SkyseedTests::blazeRoomRollsOnLargeNetherSeeds);
        reg(event, "bastion_remnant_rolls_on_bastion_biome_large_seeds", REGION, SkyseedTests::bastionRemnantRollsOnBastionBiomeLargeSeeds);
        reg(event, "debug_streets_seed_is_a_deep_jigsaw_spike", REGION, SkyseedTests::debugStreetsSeedIsADeepJigsawSpike);
        reg(event, "path_surfacer_resolves_markers_into_paths_and_bridges", REGION, SkyseedTests::pathSurfacerResolvesMarkersIntoPathsAndBridges);
        reg(event, "path_surfacer_supports_floating_floors", REGION, SkyseedTests::pathSurfacerSupportsFloatingFloors);
        reg(event, "snow_cover_caps_highest_block", REGION, SkyseedTests::snowCoverCapsHighestBlock);
        reg(event, "trade_post_blacksmith_places", REGION, SkyseedTests::tradePostBlacksmithPlaces);
        reg(event, "trade_post_over_void_uses_piers", BIG_REGION, SkyseedTests::tradePostOverVoidUsesPiers);
        reg(event, "trade_post_is_a_street_village", REGION, SkyseedTests::tradePostIsAStreetVillage);
        reg(event, "village_center_is_a_big_village", REGION, SkyseedTests::villageCenterIsABigVillage);
        reg(event, "village_center_favours_big_buildings", REGION, SkyseedTests::villageCenterFavoursBigBuildings);
        reg(event, "village_centerpiece_lands_on_square_centre", BIG_REGION, SkyseedTests::villageCenterpieceLandsOnSquareCentre);
        reg(event, "jigsaw_config_with_pool_swaps_only_the_pool", REGION, SkyseedTests::jigsawConfigWithPoolSwapsOnlyThePool);
        reg(event, "trade_post_desert_biome_selects_desert_pool", REGION, SkyseedTests::tradePostDesertBiomeSelectsDesertPool);
        reg(event, "trade_post_biome_styles_select_their_pools", REGION, SkyseedTests::tradePostBiomeStylesSelectTheirPools);
        reg(event, "hamlet_biome_styles_select_their_pools", REGION, SkyseedTests::hamletBiomeStylesSelectTheirPools);
        reg(event, "hamlet_reuses_trade_post_shops", BIG_REGION, SkyseedTests::hamletReusesTradePostShops);
        reg(event, "trade_post_biome_pieces_use_their_wood", BIG_REGION, SkyseedTests::tradePostBiomePiecesUseTheirWood);
        reg(event, "trade_post_village_places_shops", BIG_REGION, SkyseedTests::tradePostVillagePlacesShops);
        reg(event, "trade_post_desert_village_is_sandstone", BIG_REGION, SkyseedTests::tradePostDesertVillageIsSandstone);
        reg(event, "nether_fortress_is_nether_native_with_fortress_jigsaw", REGION, SkyseedTests::netherFortressIsNetherNativeWithFortressJigsaw);
        reg(event, "nether_fortress_assembles_a_bounded_bridge", BIG_REGION, SkyseedTests::netherFortressAssemblesABoundedBridge);
        reg(event, "bastion_is_nether_native_with_bastion_jigsaw", REGION, SkyseedTests::bastionIsNetherNativeWithBastionJigsaw);
        reg(event, "piglin_trading_post_is_nether_native_with_its_jigsaw", REGION, SkyseedTests::piglinTradingPostIsNetherNativeWithItsJigsaw);
        reg(event, "piglin_trading_post_overworld_easter_egg_grows_the_cottage", REGION, SkyseedTests::piglinTradingPostOverworldEasterEggGrowsTheCottage);
        reg(event, "wither_arena_is_nether_native_with_its_jigsaw", REGION, SkyseedTests::witherArenaIsNetherNativeWithItsJigsaw);
        reg(event, "forest_in_snow_defers_ground_cover_past_trees", REGION, SkyseedTests::forestInSnowDefersGroundCoverPastTrees);
        reg(event, "structure_connections_link_after_placement", REGION, SkyseedTests::structureConnectionsLinkAfterPlacement);
        reg(event, "dimension_gate_grows_or_fizzles_by_implementation", REGION, SkyseedTests::dimensionGateGrowsOrFizzlesByImplementation);
        reg(event, "dimension_override_never_inherits_overworld", REGION, SkyseedTests::dimensionOverrideNeverInheritsOverworld);
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

    /** The biome holder for a string biome id (e.g. {@code "minecraft:desert"}) — those outside the {@link Biomes} constants. */
    private static Holder<Biome> biome(ServerLevel level, String id) {
        return biome(level, ResourceKey.create(Registries.BIOME, Identifier.parse(id)));
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

    static void ruinedPortalHasNetherVariantAndTwins(GameTestHelper helper) {
        // SKYNETHERPLAN (Ruined Portal twins): the ruined portal now grows in BOTH dimensions. Overworld = the
        // treasure frame (goodies pool); Nether = a small netherrack island with the no-goodies _nether frame. It is
        // flagged a twin theme, and the 8:1 linked-coordinate maths is the vanilla portal map.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme rp = theme(overworld, "ruined_portal");
        helper.assertTrue(rp.twin().isPresent(), "ruined_portal should be flagged as a cross-dimension twin theme");
        helper.assertTrue(rp.baseValidIn(Lookup.dimensionId(Level.OVERWORLD)), "ruined_portal should grow in the overworld");

        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> wastes = biome(nether, Biomes.NETHER_WASTES);
        final Holder<Biome> plains = biome(overworld, Biomes.PLAINS);
        helper.assertTrue(IslandGenerator.formValidFor(rp, wastes, 64, Lookup.dimensionId(Level.NETHER)),
                "ruined_portal should now also grow in the Nether");

        // Overworld form: the jigsaw uses the goodies pool.
        final IslandPlan ow = IslandGenerator.planIsland(overworld, new BlockPos(40, 80, 40), rp, plains,
                RandomSource.create(91L));
        helper.assertTrue(ow.jigsaws().stream().anyMatch(j -> j.pool().path().equals("ruined_portal/portal")),
                "the overworld ruined portal should use the goodies pool ruined_portal/portal");
        helper.assertTrue(ow.twinTheme().isPresent(), "the overworld ruined portal plan should carry a twin theme");

        // A ruined portal that rolls on a big island via rare_structures pairs too: the rare structure carries the
        // same twin theme, so planIsland routes it into the plan exactly like the dedicated seed does.
        final IslandTheme rockyLarge = theme(overworld, "rocky_large");
        helper.assertTrue(rockyLarge.rareStructures().stream().anyMatch(
                        rs -> rs.jigsaw().pool().path().equals("ruined_portal/portal") && rs.twin().isPresent()),
                "rocky_large's ruined-portal rare structure should carry the twin theme");

        // Nether form: a netherrack island whose jigsaw swaps to the no-goodies _nether pool.
        final IslandPlan nv = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), rp, wastes,
                RandomSource.create(92L));
        boolean netherrack = false;
        for (IslandPlan.BlockPlacement bp : nv.blocks()) {
            if (bp.state().is(Blocks.NETHERRACK)) netherrack = true;
        }
        helper.assertTrue(netherrack, "the Nether ruined portal should be a netherrack island");
        helper.assertTrue(nv.jigsaws().stream().anyMatch(j -> j.pool().path().equals("ruined_portal/portal_nether")),
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

    static void largeNetherSeedsAreFullSizeNetherNative(GameTestHelper helper) {
        // The Large variants of the 5 Tier-2 Nether-native seeds (SKYNETHERPLAN): same biome content, much bigger
        // (radius 11-17). Each is the_nether-only and grows a LARGE island carrying its surface block.
        final ServerLevel overworld = helper.getLevel();
        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> wastes = biome(nether, Biomes.NETHER_WASTES);
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
            helper.assertTrue(t.baseValidIn(Lookup.dimensionId(Level.NETHER)), c.theme() + " must implement the_nether");
            helper.assertTrue(!t.baseValidIn(Lookup.dimensionId(Level.OVERWORLD)), c.theme() + " must be the_nether-only");
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
            if (list.get(i).jigsaw().pool().path().equals(poolPath)) {
                return i;
            }
        }
        return -1;
    }

    static void blazeRoomRollsOnLargeNetherSeeds(GameTestHelper helper) {
        // The surprise blaze spawner room (SKYNETHERPLAN): a 5% rare_structures roll on each of the 5 Large Nether
        // seeds. (Its on-demand debug seed is auto-generated from these hosts now, not a hand-made theme.)
        final ServerLevel overworld = helper.getLevel();
        for (String t : new String[] { "nether_rocky_large", "nether_lava_large", "nether_forest_large",
                "nether_soul_large", "nether_basalt_large" }) {
            final IslandTheme nt = theme(overworld, t);
            helper.assertTrue(nt.rareStructures().stream().anyMatch(
                            rs -> rs.jigsaw().pool().path().equals("nether_fortress/blaze_room")),
                    t + " should carry the 5% blaze spawner room rare structure");
        }
        // forcedRare germinates it on demand on a host theme — the same path the auto debug seed drives.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> wastes = biome(nether, Biomes.NETHER_WASTES);
        final IslandTheme host = theme(nether, "nether_rocky_large");
        final int idx = rareIndex(host, "nether_fortress/blaze_room");
        helper.assertTrue(idx >= 0, "nether_rocky_large should host the blaze room rare structure");
        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), host, wastes,
                RandomSource.create(140L), DebugForce.rare(idx));
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().path().equals("nether_fortress/blaze_room")),
                "forcing the blaze-room rare structure should assemble its jigsaw");
        helper.succeed();
    }

    static void bastionRemnantRollsOnBastionBiomeLargeSeeds(GameTestHelper helper) {
        // A ruined bastion remnant (crying obsidian + cracked polished blackstone) is a 5% rare_structures roll on the
        // three bastion-biome Large Nether seeds — the Nether-wastes Rocky, the crimson/warped Forest and the soul-sand
        // Soul — but NOT the basalt deltas or the lava sea (the vanilla rule). Its on-demand debug seed is auto-generated.
        final ServerLevel overworld = helper.getLevel();
        for (String t : new String[] { "nether_rocky_large", "nether_forest_large", "nether_soul_large" }) {
            helper.assertTrue(theme(overworld, t).rareStructures().stream().anyMatch(
                            rs -> rs.jigsaw().pool().path().equals("bastion/remnant")),
                    t + " should carry the 5% bastion remnant rare structure");
        }
        for (String t : new String[] { "nether_basalt_large", "nether_lava_large" }) {
            helper.assertTrue(theme(overworld, t).rareStructures().stream().noneMatch(
                            rs -> rs.jigsaw().pool().path().equals("bastion/remnant")),
                    t + " must not carry the bastion remnant (no bastions in the basalt deltas or the lava sea)");
        }
        // forcedRare germinates it on demand on a host theme — the same path the auto debug seed drives.
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> wastes = biome(nether, Biomes.NETHER_WASTES);
        final IslandTheme host = theme(nether, "nether_rocky_large");
        final int idx = rareIndex(host, "bastion/remnant");
        helper.assertTrue(idx >= 0, "nether_rocky_large should host the bastion remnant rare structure");
        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), host, wastes,
                RandomSource.create(77L), DebugForce.rare(idx));
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().path().equals("bastion/remnant")),
                "forcing the bastion-remnant rare structure should assemble its jigsaw");
        helper.succeed();
    }

    static void debugStreetsSeedIsADeepJigsawSpike(GameTestHelper helper) {
        // SKYJIGSAWPLAN Phase 0 spike: a throwaway creative seed whose jigsaw recurses (depth 6) through a
        // self-connecting street pool, so the network branches, twists and — on a real island — runs out over the
        // void. The over-void sprawl and its reach are an in-world smoke test (throw the seed); here we just guard
        // the wiring — a deep jigsaw into the start pool, carried as a jigsaw site on the plan.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme streets = theme(overworld, "debug_streets");
        helper.assertTrue(streets.jigsaw().isPresent(), "debug_streets must have a jigsaw config");
        helper.assertTrue(streets.jigsaw().get().pool().path().equals("debug_streets/start"),
                "debug_streets must start from its start pool");
        helper.assertTrue(streets.jigsaw().get().depth() >= 5,
                "debug_streets must recurse deep enough to sprawl (got depth " + streets.jigsaw().get().depth() + ")");
        helper.assertTrue(streets.jigsaw().get().reach() > 0,
                "debug_streets must set reach > 0 so the path/bridge surfacing pass runs");
        final BlockPos c = new BlockPos(40, 80, 40);
        final IslandPlan p = IslandGenerator.planIsland(overworld, c, streets, overworld.getBiome(c),
                RandomSource.create(42L));
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().path().equals("debug_streets/start")),
                "the debug streets seed should carry its start jigsaw site");
        helper.succeed();
    }

    static void pathSurfacerResolvesMarkersIntoPathsAndBridges(GameTestHelper helper) {
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

    static void pathSurfacerSupportsFloatingFloors(GameTestHelper helper) {
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

    static void snowCoverCapsHighestBlock(GameTestHelper helper) {
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

    static void tradePostBlacksmithPlaces(GameTestHelper helper) {
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

    static void tradePostOverVoidUsesPiers(GameTestHelper helper) {
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
            Jigsaw.placeCapped(level, pool, Id.of("minecraft:bottom"), 5, origin, false, "shop_", 4, fillers, iter);
            voidChains += countChains(helper);
            // (2) on solid ground — surplus lots should be fields, never piers
            fillRegion(helper, false);
            Jigsaw.placeCapped(level, pool, Id.of("minecraft:bottom"), 5, origin, false, "shop_", 4, fillers, iter);
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
                    // 26.1.2: the chain block was renamed minecraft:chain -> minecraft:iron_chain (copper update).
                    if (helper.getBlockState(new BlockPos(x, y, z)).is(Blocks.IRON_CHAIN)) {
                        chains++;
                    }
                }
            }
        }
        return chains;
    }

    static void tradePostIsAStreetVillage(GameTestHelper helper) {
        // SKYJIGSAWPLAN Phase 1: the Trade Post is now a street village — a square radiating a depth-4 street
        // network with shops + fields hung off lot connectors, surfaced by PathSurfacer (dirt paths on the island,
        // self-railing bridges over the void). Guard the wiring; the village's look is an in-world smoke test. The
        // streets/lots pools are validated by the datapack load (a bad element reference fails the run).
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme tp = theme(overworld, "trade_post");
        helper.assertTrue(tp.jigsaw().isPresent() && tp.jigsaw().get().pool().path().equals("trade_post/start"),
                "trade_post must start from its start pool");
        helper.assertTrue(tp.jigsaw().get().depth() >= 4, "trade_post must recurse into a street network");
        helper.assertTrue(tp.jigsaw().get().reach() > 0, "trade_post must set reach for surfacing + the bed scan");
        final BlockPos c = new BlockPos(40, 80, 40);
        final IslandPlan p = IslandGenerator.planIsland(overworld, c, tp, overworld.getBiome(c), RandomSource.create(8L));
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().path().equals("trade_post/start")),
                "trade_post should carry its start jigsaw site");
        helper.succeed();
    }

    static void villageCenterIsABigVillage(GameTestHelper helper) {
        // The village_center seed is "a bigger Trade Post" laid out as a CLUSTER: the SAME building pieces via a
        // denser street skeleton (trade_post/start_dense, whose lanes weight the large/big-building section higher),
        // a deeper street network and a guaranteed 4+ shops, spread over 3 small islands ringed around a void centre
        // (cluster_offsets). Like the trade post it's biome-styled -- a desert one pulls the desert pieces.
        final ServerLevel level = helper.getLevel();
        final IslandTheme vc = theme(level, "village_center");
        helper.assertTrue(vc.jigsaw().isPresent() && vc.jigsaw().get().pool().path().equals("trade_post/start_dense"),
                "village_center must reuse the trade post village pieces (via the denser start_dense skeleton)");
        helper.assertTrue(vc.jigsaw().get().depth() > 4, "village_center must run a deeper street network than the trade post (depth 4)");
        helper.assertTrue(vc.jigsaw().get().capMin() >= 4, "village_center must guarantee at least 4 shops");
        helper.assertTrue(!vc.shape().clusterOffsets().isEmpty(),
                "village_center must be a cluster of small islands (cluster_offsets), not one huge island");
        helper.assertTrue(vc.jigsaw().get().centerpiece().map(cp -> cp.path().equals("anvil")).orElse(false),
                "village_center must set an anvil centerpiece (the capstone at the cluster's centre)");
        final Holder<Biome> desert = biome(level, "minecraft:desert");
        final IslandPlan p = IslandGenerator.planIsland(level, new BlockPos(40, 80, 40), vc, desert, RandomSource.create(8L));
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().path().equals("trade_post_desert/start_dense")),
                "a desert village_center must use the desert village pieces (biome-styled like the trade post)");
        helper.succeed();
    }

    static void villageCenterFavoursBigBuildings(GameTestHelper helper) {
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

    static void villageCenterpieceLandsOnSquareCentre(GameTestHelper helper) {
        // The anvil capstone (GenerationJob) is stamped at the jigsaw origin, which must be the start square's centre
        // tile — its lantern. Assemble just the start square and confirm the lantern sits exactly at origin: the spot
        // the capstone replaces. Guards against the jigsaw seating shifting and the capstone landing off-centre/floating.
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 3, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("trade_post/start"));
        Jigsaw.placeCapped(level, pool, Id.of("minecraft:bottom"), 1, origin, false, "", 0, null, 1L);
        helper.assertTrue(helper.getBlockState(new BlockPos(24, 3, 24)).is(Blocks.LANTERN),
                "the start square's centre tile must land at origin (where the centerpiece capstone is stamped)");
        helper.succeed();
    }

    static void jigsawConfigWithPoolSwapsOnlyThePool(GameTestHelper helper) {
        // Backs JigsawConfig.withPool (the wither dimensionVariant uses to swap in a _nether pool): it must change ONLY
        // the pool. Round-trip — withPool(other).withPool(original) equalling the original — proves every other field
        // (depth, cap, golems, centerpiece, …) survived the copy.
        final var jc = theme(helper.getLevel(), "village_center").jigsaw().orElseThrow();
        final Id other = Id.of("skyseed:trade_post_desert/start");
        final var swapped = jc.withPool(other);
        helper.assertTrue(swapped.pool().equals(other), "withPool must set the new pool");
        helper.assertTrue(swapped.withPool(jc.pool()).equals(jc),
                "withPool must change only the pool (round-trip to the original pool must equal the original)");
        helper.succeed();
    }

    static void tradePostDesertBiomeSelectsDesertPool(GameTestHelper helper) {
        // Biome-override wiring — what a forced-biome debug seed exercises. Planning the trade post in a desert biome
        // must select the desert jigsaw pool (and a sand surface), not the default plains/oak start.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme tp = theme(overworld, "trade_post");
        final Holder<Biome> desert = biome(overworld, "minecraft:desert");
        final BlockPos c = new BlockPos(40, 80, 40);
        final IslandPlan p = IslandGenerator.planIsland(overworld, c, tp, desert, RandomSource.create(8L));
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().path().equals("trade_post_desert/start")),
                "a trade post in a desert biome should use the desert jigsaw pool");
        helper.succeed();
    }

    static void tradePostBiomeStylesSelectTheirPools(GameTestHelper helper) {
        // The remaining village styles: savanna gets its acacia pool; taiga and snowy share the spruce pool.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme tp = theme(overworld, "trade_post");
        final BlockPos c = new BlockPos(40, 80, 40);
        final String[][] cases = {
                {"minecraft:savanna", "trade_post_savanna/start"},
                {"minecraft:taiga", "trade_post_spruce/start"},
                {"minecraft:snowy_plains", "trade_post_spruce/start"}};
        for (String[] cs : cases) {
            final Holder<Biome> b = biome(overworld, cs[0]);
            final IslandPlan p = IslandGenerator.planIsland(overworld, c, tp, b, RandomSource.create(8L));
            helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().path().equals(cs[1])),
                    cs[0] + " trade post should use pool " + cs[1]);
        }
        helper.succeed();
    }

    static void hamletBiomeStylesSelectTheirPools(GameTestHelper helper) {
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
            final Holder<Biome> b = biome(overworld, cs[0]);
            final IslandPlan p = IslandGenerator.planIsland(overworld, c, h, b, RandomSource.create(8L));
            helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().path().equals(cs[1])),
                    cs[0] + " hamlet should use pool " + cs[1]);
        }
        helper.succeed();
    }

    static void hamletReusesTradePostShops(GameTestHelper helper) {
        // The hamlet starts from a small green whose lot connectors pull the trade post's lots pool, so it places the
        // same diverse profession shops, capped to 1–2. The placement is position-seeded and the gametest origin
        // varies per run, so pass each iteration as an explicit seed → five DIFFERENT hamlets; at least one lands a shop.
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
            Jigsaw.placeCapped(level, pool, Id.of("minecraft:bottom"), 2, origin, false, "shop_", 2, fillers, iter);
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

    static void tradePostBiomePiecesUseTheirWood(GameTestHelper helper) {
        // The savanna set is acacia and the shared spruce set is spruce — confirm the palette produced the right wood
        // (not oak) and the villages still place shops via the same cap/filler machinery.
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 3, 24));
        checkWood(helper, level, origin, "trade_post_savanna", Blocks.ACACIA_PLANKS);
        checkWood(helper, level, origin, "trade_post_spruce", Blocks.SPRUCE_PLANKS);
        helper.succeed();
    }

    private static void checkWood(GameTestHelper helper, ServerLevel level, BlockPos origin, String pool, Block wood) {
        final var startPool = Lookup.templatePool(level.registryAccess(), Ids.mod(pool + "/start"));
        final var fillers = Lookup.templatePool(level.registryAccess(), Ids.mod(pool + "/fillers"));
        int beds = 0;
        int rightWood = 0;
        int oak = 0;
        // The village is position-seeded AND the gametest origin varies per run, so a single placement sometimes rolls
        // zero shops; sample several explicit seeds and assert across them (a shop lands in at least one, oak never).
        for (long seed = 1; seed <= 4; seed++) {
            for (int x = 4; x <= 44; x++) {
                for (int z = 4; z <= 44; z++) {
                    for (int y = 1; y <= 14; y++) {
                        helper.setBlock(new BlockPos(x, y, z), y <= 2 ? Blocks.DIRT : Blocks.AIR);
                    }
                }
            }
            Jigsaw.placeCapped(level, startPool, Id.of("minecraft:bottom"), 5, origin, false, "shop_", 3, fillers, seed);
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
        }
        helper.assertTrue(beds > 0, pool + " placed no shops across 4 seeds (red_bed=" + beds + ")");
        helper.assertTrue(rightWood > 0, pool + " has no " + wood + " walls (count=" + rightWood + ")");
        helper.assertTrue(oak == 0, pool + " still has oak plank walls (oak=" + oak + ")");
    }

    static void tradePostVillagePlacesShops(GameTestHelper helper) {
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
                Jigsaw.placeCapped(level, pool, Id.of("minecraft:bottom"), 6, origin, false, "shop_", cap, fillers, seed);
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

    static void tradePostDesertVillageIsSandstone(GameTestHelper helper) {
        // Biome diversity: a desert biome override swaps in the sand/sandstone piece set (its own jigsaw pool), built
        // by the same generator from a desert palette. Assemble it and confirm the buildings are sandstone — not oak —
        // while the shop cap + filler machinery still place shops and fields. (Loads dev-generated .nbt.)
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 3, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("trade_post_desert/start"));
        final var fillers = Lookup.templatePool(level.registryAccess(), Ids.mod("trade_post_desert/fillers"));
        int beds = 0;
        int sandstone = 0;
        int oakWall = 0;
        // Position-seeded AND the gametest origin varies per run, so a single placement sometimes rolls zero shops;
        // sample several explicit seeds and assert across them (a shop in at least one, oak in none).
        for (long seed = 1; seed <= 4; seed++) {
            for (int x = 4; x <= 44; x++) {
                for (int z = 4; z <= 44; z++) {
                    for (int y = 1; y <= 14; y++) {
                        helper.setBlock(new BlockPos(x, y, z), y <= 2 ? Blocks.DIRT : Blocks.AIR);
                    }
                }
            }
            Jigsaw.placeCapped(level, pool, Id.of("minecraft:bottom"), 5, origin, false, "shop_", 3, fillers, seed);
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
        }
        helper.assertTrue(beds > 0, "desert village placed no shops across 4 seeds (red_bed=" + beds + ")");
        helper.assertTrue(sandstone > 0, "desert village has no sandstone walls (smooth_sandstone=" + sandstone + ")");
        helper.assertTrue(oakWall == 0, "desert village still has oak plank walls (oak_planks=" + oakWall + ")");
        helper.succeed();
    }

    static void netherFortressIsNetherNativeWithFortressJigsaw(GameTestHelper helper) {
        // Nether-native fortress island (SKYNETHERPLAN): a netherrack island that assembles the fortress jigsaw — a keep
        // with a caged blaze spawner, self-connecting arcaded bridge spans out over the void, wart-garden ends. The
        // structure itself is placed later by the generation job, so the plan carries it as a jigsaw site.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme nf = theme(overworld, "nether_fortress");
        helper.assertTrue(nf.baseValidIn(Lookup.dimensionId(Level.NETHER)), "nether_fortress must implement the_nether");
        helper.assertTrue(!nf.baseValidIn(Lookup.dimensionId(Level.OVERWORLD)), "nether_fortress must be the_nether-only");

        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> wastes = biome(nether, Biomes.NETHER_WASTES);
        helper.assertTrue(IslandGenerator.formValidFor(nf, wastes, 64, Lookup.dimensionId(Level.NETHER)),
                "nether_fortress should grow in the Nether");

        final IslandPlan p = IslandGenerator.planIsland(nether, new BlockPos(40, 64, 40), nf, wastes,
                RandomSource.create(131L));
        boolean netherrack = false;
        for (IslandPlan.BlockPlacement bp : p.blocks()) {
            if (bp.state().is(Blocks.NETHERRACK)) netherrack = true;
        }
        helper.assertTrue(netherrack, "the nether fortress island should be a netherrack island");
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().path().equals("nether_fortress/start")),
                "the nether fortress island should assemble the fortress jigsaw (start pool = the keep)");
        helper.succeed();
    }

    static void netherFortressAssemblesABoundedBridge(GameTestHelper helper) {
        // Assemble the fortress jigsaw with the production span_ cap and confirm the pieces actually chain AND stay
        // bounded: the keep places exactly one (blaze) spawner, the self-connecting arcaded spans (incl. branching
        // crossings) lay a run of nether brick out from it, and the cap (≤ 8 span_ pieces, surplus re-stamped as
        // wart-garden ends) keeps it a compact fortress — not a runaway sprawl that fills the 48² template. (Loads
        // dev-generated .nbt; syncDevStructures keeps the node copy current.)
        final ServerLevel level = helper.getLevel();
        final BlockPos origin = helper.absolutePos(new BlockPos(24, 3, 24));
        final var pool = Lookup.templatePool(level.registryAccess(), Ids.mod("nether_fortress/start"));
        final var ends = Lookup.templatePool(level.registryAccess(), Ids.mod("nether_fortress/ends"));
        Jigsaw.placeCapped(level, pool, Id.of("minecraft:bottom"), 5, origin, false, "span_", 8, ends, 1L);
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

    static void bastionIsNetherNativeWithBastionJigsaw(GameTestHelper helper) {
        // Nether-native bastion remnant island: a blackstone island that assembles the hand-built bastion (a
        // lodestone treasure plinth, a magma-cube spawner, bastion loot). The structure is placed later by the
        // generation job, so the plan carries it as a jigsaw site.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme bastion = theme(overworld, "bastion");
        helper.assertTrue(bastion.baseValidIn(Lookup.dimensionId(Level.NETHER)), "bastion must implement the_nether");
        helper.assertTrue(!bastion.baseValidIn(Lookup.dimensionId(Level.OVERWORLD)), "bastion must be the_nether-only");

        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> wastes = biome(nether, Biomes.NETHER_WASTES);
        helper.assertTrue(IslandGenerator.formValidFor(bastion, wastes, 64, Lookup.dimensionId(Level.NETHER)),
                "bastion should grow in the Nether");
        final Holder<Biome> deltas = biome(nether, Biomes.BASALT_DELTAS);
        helper.assertTrue(!IslandGenerator.formValidFor(bastion, deltas, 64, Lookup.dimensionId(Level.NETHER)),
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
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().path().equals("bastion/bastion")),
                "the bastion should assemble the bastion jigsaw");
        helper.succeed();
    }

    static void piglinTradingPostIsNetherNativeWithItsJigsaw(GameTestHelper helper) {
        // Nether-native Piglin Trading Post: a blackstone island that assembles the hand-built trading-post hall and
        // grows anywhere in the Nether — including the basalt deltas, since (unlike the bastion) it has no fizzle rule.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme post = theme(overworld, "piglin_trading_post");
        helper.assertTrue(post.baseValidIn(Lookup.dimensionId(Level.NETHER)), "trading post must implement the_nether");
        helper.assertTrue(!post.baseValidIn(Lookup.dimensionId(Level.OVERWORLD)), "trading post must be the_nether-only");

        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> wastes = biome(nether, Biomes.NETHER_WASTES);
        helper.assertTrue(IslandGenerator.formValidFor(post, wastes, 64, Lookup.dimensionId(Level.NETHER)),
                "trading post should grow in the Nether");
        final Holder<Biome> deltas = biome(nether, Biomes.BASALT_DELTAS);
        helper.assertTrue(IslandGenerator.formValidFor(post, deltas, 64, Lookup.dimensionId(Level.NETHER)),
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
                        .anyMatch(j -> j.pool().path().equals("piglin_trading_post/trading_post")),
                "the trading post should assemble its jigsaw");
        helper.succeed();
    }

    static void piglinTradingPostOverworldEasterEggGrowsTheCottage(GameTestHelper helper) {
        // Easter egg: thrown topside the Nether-native trading post doesn't fizzle — an overworld biome_override grows
        // a grass island and an overworld-dimensioned rare structure (chance 1.0) swaps the hall for the abandoned
        // cottage (the Hamlet's 10% rare structure).
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme post = theme(overworld, "piglin_trading_post");
        final Holder<Biome> plains = biome(overworld, Biomes.PLAINS);
        helper.assertTrue(IslandGenerator.formValidFor(post, plains, 80, Lookup.dimensionId(Level.OVERWORLD)),
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
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().path().equals("abandoned/cottage")),
                "the overworld easter egg should assemble the abandoned cottage");
        helper.assertTrue(p.jigsaws().stream()
                        .noneMatch(j -> j.pool().path().equals("piglin_trading_post/trading_post")),
                "the trading-post hall must not appear in the overworld");
        helper.succeed();
    }

    static void witherArenaIsNetherNativeWithItsJigsaw(GameTestHelper helper) {
        // Nether-native Wither Arena: the capstone venue. A blackstone island that assembles the hand-built obsidian
        // arena jigsaw, and (like the other nether structures) grows only in the Nether.
        final ServerLevel overworld = helper.getLevel();
        final IslandTheme arena = theme(overworld, "wither_arena");
        helper.assertTrue(arena.baseValidIn(Lookup.dimensionId(Level.NETHER)), "wither arena must implement the_nether");
        helper.assertTrue(!arena.baseValidIn(Lookup.dimensionId(Level.OVERWORLD)), "wither arena must be the_nether-only");

        final ServerLevel nether = overworld.getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> wastes = biome(nether, Biomes.NETHER_WASTES);
        helper.assertTrue(IslandGenerator.formValidFor(arena, wastes, 64, Lookup.dimensionId(Level.NETHER)),
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
        helper.assertTrue(p.jigsaws().stream().anyMatch(j -> j.pool().path().equals("wither_arena/wither_arena")),
                "the wither arena should assemble its jigsaw");
        helper.succeed();
    }

    static void forestInSnowDefersGroundCoverPastTrees(GameTestHelper helper) {
        // Regression: a Forest seed on snowy_plains came up bare — the 90% snow ground cover was placed before the
        // (deferred) spruce sites, and a snow layer fails the vanilla tree feature's valid-position check. Ground cover
        // is now recorded as scatter and placed AFTER the trees (GenerationJob), so it can never block one.
        final ServerLevel level = helper.getLevel();
        final IslandTheme forest = theme(level, "forest");
        final Holder<Biome> snowy = biome(level, Biomes.SNOWY_PLAINS);
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

    static void structureConnectionsLinkAfterPlacement(GameTestHelper helper) {
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

    static void dimensionGateGrowsOrFizzlesByImplementation(GameTestHelper helper) {
        // The adapt-or-fizzle matrix (SKYNETHERPLAN): a seed grows only in dimensions it implements — its base
        // `dimensions` or a dimension-keyed override — and fizzles elsewhere rather than growing the foreign base.
        final ServerLevel level = helper.getLevel();
        final Holder<Biome> biome = biome(level, Biomes.PLAINS);
        final String ow = Lookup.dimensionId(Level.OVERWORLD);
        final String nether = Lookup.dimensionId(Level.NETHER);
        final String end = Lookup.dimensionId(Level.END);

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

    static void dimensionOverrideNeverInheritsOverworld(GameTestHelper helper) {
        // A Nether/End override is a complete spec — an unset field must NOT fall back to the overworld base. This
        // theme's base has coal ore + grass; its bare Nether override sets only the surface, so the Nether island
        // must carry none of that overworld content (neutral netherrack body, no coal, no grass).
        final ServerLevel nether = helper.getLevel().getServer().getLevel(Level.NETHER);
        helper.assertTrue(nether != null, "no the_nether level on the server");
        final Holder<Biome> wastes = biome(nether, Biomes.NETHER_WASTES);
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
}
