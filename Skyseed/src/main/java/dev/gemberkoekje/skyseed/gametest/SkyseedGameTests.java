package dev.gemberkoekje.skyseed.gametest;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.registry.SkyseedRegistries;
import dev.gemberkoekje.skyseed.worldgen.IslandGenerator;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan;
import dev.gemberkoekje.skyseed.worldgen.theme.IslandTheme;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.gametest.framework.GameTest;
import net.minecraft.gametest.framework.GameTestHelper;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
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

    // --- structure templates (guard the template de-duplication) ---

    @GameTest(template = REGION)
    public static void outpostHasSpawnerAndCage(GameTestHelper helper) {
        final BlockPos o = place(helper, "skyseed:outpost/tower");
        helper.assertTrue(contains(helper, o, 14, 18, 14, Blocks.SPAWNER), "outpost lost its pillager spawner");
        helper.assertTrue(contains(helper, o, 14, 18, 14, Blocks.DARK_OAK_FENCE), "outpost lost its golem cage");
        helper.assertTrue(contains(helper, o, 14, 18, 14, Blocks.CHEST), "outpost lost its loot chest");
        helper.succeed();
    }

    @GameTest(template = REGION)
    public static void trialHubHasBossAndOminousVault(GameTestHelper helper) {
        final BlockPos o = place(helper, "skyseed:trial_chamber/hub");
        helper.assertTrue(contains(helper, o, 8, 8, 8, Blocks.TRIAL_SPAWNER), "trial hub lost its breeze boss spawner");
        helper.assertTrue(contains(helper, o, 8, 8, 8, Blocks.VAULT), "trial hub lost its ominous vault");
        helper.succeed();
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
