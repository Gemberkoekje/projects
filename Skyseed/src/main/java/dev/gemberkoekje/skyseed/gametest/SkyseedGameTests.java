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
