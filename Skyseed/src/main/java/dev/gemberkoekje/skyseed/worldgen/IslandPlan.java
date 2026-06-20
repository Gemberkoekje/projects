package dev.gemberkoekje.skyseed.worldgen;

import net.minecraft.core.BlockPos;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.levelgen.feature.ConfiguredFeature;

import java.util.List;

/**
 * A fully computed island, ready to be placed into the world over several ticks (plan §5 tick-budget
 * guard). Produced by {@link IslandGenerator#planIsland} without touching the world, so it can be
 * overlap-checked and then drained by a {@link GenerationJob}.
 *
 * @param blocks ordered block placements (bottom-up, for a tidy "grows in" animation)
 * @param trees  configured features to place after the solid blocks land
 * @param random RNG carried over from planning, used for the trees' own shape randomness
 */
public record IslandPlan(List<BlockPlacement> blocks, List<TreeSite> trees, RandomSource random) {
    public record BlockPlacement(BlockPos pos, BlockState state) {}

    public record TreeSite(ConfiguredFeature<?, ?> feature, BlockPos pos) {}
}
