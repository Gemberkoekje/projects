package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.compat.Id;
import dev.gemberkoekje.skyseed.compat.Lookup;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan.TreeSite;
import dev.gemberkoekje.skyseed.worldgen.theme.Decoration;
import dev.gemberkoekje.skyseed.worldgen.theme.GroundEntry;
import dev.gemberkoekje.skyseed.worldgen.theme.TreeEntry;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.DoublePlantBlock;
import net.minecraft.world.level.block.LiquidBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;
import net.minecraft.world.level.block.state.properties.DoubleBlockHalf;
import net.minecraft.world.level.block.state.properties.DripstoneThickness;
import net.minecraft.world.level.levelgen.feature.ConfiguredFeature;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;

/**
 * Plants a variant's surface decoration onto an island: trees (vanilla configured features deferred to
 * {@link IslandPlan.TreeSite}, or {@link CustomTrees} hand-built ones stamped straight in), ground cover, and
 * underside hangs. Also places short rim waterfalls. Carved pond columns are already out of {@code surfaceList}.
 */
final class DecorationPlanner {
    private DecorationPlanner() {}

    static void planDecoration(ServerLevel level, Map<BlockPos, BlockState> blockMap, List<TreeSite> trees,
                               List<BlockPos> surfaceList, List<BlockPos> bottomList, Decoration deco,
                               Set<BlockPos> scatter, RandomSource random) {
        if (surfaceList.isEmpty()) {
            return;
        }
        final List<BlockPos> treeBases = new ArrayList<>();
        for (TreeEntry tree : deco.trees()) {
            // skyseed:* "features" are built-in hand-built trees (vanilla features that won't place
            // dry, like mangroves); anything else is a vanilla configured feature placed afterwards.
            final boolean custom = tree.feature().getNamespace().equals(Skyseed.MODID);
            ConfiguredFeature<?, ?> feature = null;
            if (custom) {
                final String path = tree.feature().getPath();
                if (!path.equals("mangrove") && !path.equals("azalea") && !path.equals("ice_spike")) {
                    Skyseed.LOGGER.warn("[skyseed] unknown built-in tree '{}' — skipping", tree.feature());
                    continue;
                }
            } else {
                Optional<ConfiguredFeature<?, ?>> resolved =
                        Lookup.configuredFeature(level.registryAccess(), tree.feature());
                if (resolved.isEmpty()) {
                    Skyseed.LOGGER.warn("[skyseed] theme references unknown feature '{}' — skipping", tree.feature());
                    continue;
                }
                feature = resolved.get();
            }
            final int spacingSq = tree.spacing() * tree.spacing();
            for (int i = 0; i < tree.tries(); i++) {
                final BlockPos base = surfaceList.get(random.nextInt(surfaceList.size()));
                boolean tooClose = false;
                for (BlockPos t : treeBases) {
                    if (t.distSqr(base) < spacingSq) {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) {
                    continue;
                }
                treeBases.add(base);
                if (custom) {
                    // hand-built into the streamed block list (vanilla features that won't place here)
                    final String path = tree.feature().getPath();
                    if (path.equals("azalea")) {
                        CustomTrees.buildAzalea(blockMap, base, random);
                    } else if (path.equals("ice_spike")) {
                        CustomTrees.buildIceSpike(blockMap, base, random);
                    } else {
                        CustomTrees.buildMangrove(blockMap, base, random);
                    }
                } else {
                    trees.add(new TreeSite(feature, base.above()));
                }
            }
        }

        // Ground cover is recorded as "scatter": GenerationJob places these positions AFTER the (deferred) trees, so a
        // snow layer can't claim a tree's spot and make the vanilla tree feature fail (which left a snowy Forest bare).
        // Hand-built trees are already in blockMap and skipped.
        if (!deco.ground().isEmpty()) {
            for (BlockPos grass : surfaceList) {
                final BlockPos above = grass.above();
                if (blockMap.containsKey(above)) {
                    continue; // already a trunk/leaf/etc. from a hand-built tree
                }
                float roll = random.nextFloat();
                for (GroundEntry g : deco.ground()) {
                    roll -= g.chance();
                    if (roll < 0) {
                        if (Lookup.hasBlock(g.block())) {
                            placeGround(blockMap, above, Lookup.block(g.block()), scatter);
                        }
                        break;
                    }
                }
            }
        }

        planUnderside(blockMap, bottomList, deco.underside(), random);
    }

    /** Place a ground plant, expanding two-tall plants (dripleaves, pitcher plant, tall flowers) into both halves. */
    private static void placeGround(Map<BlockPos, BlockState> blockMap, BlockPos above, Block block, Set<BlockPos> scatter) {
        if (block instanceof DoublePlantBlock) {
            blockMap.put(above, block.defaultBlockState().setValue(DoublePlantBlock.HALF, DoubleBlockHalf.LOWER));
            blockMap.put(above.above(), block.defaultBlockState().setValue(DoublePlantBlock.HALF, DoubleBlockHalf.UPPER));
            scatter.add(above);
            scatter.add(above.above());
        } else {
            blockMap.put(above, block.defaultBlockState());
            scatter.add(above);
        }
    }

    /** Hang per-column features from the island's underside: dripstone, cave vines, spore blossoms, roots. */
    private static void planUnderside(Map<BlockPos, BlockState> blockMap, List<BlockPos> bottomList,
                                      List<GroundEntry> cfg, RandomSource random) {
        if (cfg.isEmpty() || bottomList.isEmpty()) {
            return;
        }
        for (BlockPos bottom : bottomList) {
            float roll = random.nextFloat();
            for (GroundEntry g : cfg) {
                roll -= g.chance();
                if (roll < 0) {
                    if (Lookup.hasBlock(g.block())) {
                        hangUnder(blockMap, bottom, g.block(), random);
                    }
                    break;
                }
            }
        }
    }

    /** Build a single hanging feature under {@code bottom} (a column's lowest block, or a cave ceiling for the CaveCarver). */
    static void hangUnder(Map<BlockPos, BlockState> blockMap, BlockPos bottom, Id id, RandomSource random) {
        final BlockPos first = bottom.below();
        if (blockMap.containsKey(first)) {
            return;
        }
        switch (id.path()) {
            case "pointed_dripstone" -> {
                final int len = 1 + random.nextInt(3); // 1-3 tall stalactite
                for (int i = 0; i < len; i++) {
                    final DripstoneThickness th = (len == 1 || i == len - 1) ? DripstoneThickness.TIP
                            : (i == 0) ? (len >= 3 ? DripstoneThickness.BASE : DripstoneThickness.FRUSTUM)
                            : DripstoneThickness.MIDDLE;
                    blockMap.put(bottom.below(i + 1), Blocks.POINTED_DRIPSTONE.defaultBlockState()
                            .setValue(BlockStateProperties.VERTICAL_DIRECTION, Direction.DOWN)
                            .setValue(BlockStateProperties.DRIPSTONE_THICKNESS, th));
                }
            }
            case "cave_vines", "cave_vines_plant" -> {
                final int len = 1 + random.nextInt(4); // 1-4 trailing vine, berries lit
                for (int i = 0; i < len; i++) {
                    final boolean tip = i == len - 1;
                    blockMap.put(bottom.below(i + 1), (tip ? Blocks.CAVE_VINES : Blocks.CAVE_VINES_PLANT)
                            .defaultBlockState().setValue(BlockStateProperties.BERRIES, Boolean.TRUE));
                }
            }
            // Glow lichen is a multiface block: set its UP face so it clings to the island's underside and glows.
            case "glow_lichen" -> blockMap.put(first, Blocks.GLOW_LICHEN.defaultBlockState().setValue(BlockStateProperties.UP, true));
            default -> blockMap.put(first, Lookup.blockState(id));
        }
    }

    /** Short static cascades off the rim — a spring at the lip + a falling-water column down the side. */
    static void placeWaterfalls(Map<BlockPos, BlockState> blockMap, List<BlockPos> surfaceList,
                                BlockPos center, int baseRadius, int count, RandomSource random) {
        final BlockState source = Blocks.WATER.defaultBlockState();
        final BlockState falling = Blocks.WATER.defaultBlockState().setValue(LiquidBlock.LEVEL, 8);
        final int minEdgeSq = (int) ((baseRadius * 0.6) * (baseRadius * 0.6));
        final List<BlockPos> edges = new ArrayList<>();
        for (BlockPos p : surfaceList) {
            int dx = p.getX() - center.getX();
            int dz = p.getZ() - center.getZ();
            if (dx * dx + dz * dz >= minEdgeSq) {
                edges.add(p);
            }
        }
        final List<BlockPos> pick = edges.isEmpty() ? surfaceList : edges;
        for (int n = 0; n < count; n++) {
            final BlockPos c = pick.get(random.nextInt(pick.size()));
            int dx = c.getX() - center.getX();
            int dz = c.getZ() - center.getZ();
            int ox = 0;
            int oz = 0;
            if (Math.abs(dx) >= Math.abs(dz)) {
                ox = Integer.signum(dx);
            } else {
                oz = Integer.signum(dz);
            }
            if (ox == 0 && oz == 0) {
                ox = 1;
            }
            final int topY = c.getY();
            blockMap.put(new BlockPos(c.getX(), topY, c.getZ()), source); // spring at the lip
            for (int k = 0; k <= 6; k++) {
                blockMap.put(new BlockPos(c.getX() + ox, topY - k, c.getZ() + oz), falling); // cascade down the face
            }
        }
    }
}
