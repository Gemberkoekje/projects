package dev.gemberkoekje.skyseed.worldgen;

import net.minecraft.core.BlockPos;
import net.minecraft.util.RandomSource;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.levelgen.feature.ConfiguredFeature;

import java.util.List;

/**
 * A fully computed island, ready to be placed into the world over several ticks (README → Generation algorithm tick-budget
 * guard). Produced by {@link IslandGenerator#planIsland} without touching the world, so it can be
 * overlap-checked and then drained by a {@link GenerationJob}.
 *
 * @param blocks ordered block placements (bottom-up, for a tidy "grows in" animation)
 * @param trees  configured features to place after the solid blocks land
 * @param mobs   animals to spawn on the surface once the island is fully placed
 * @param hives  bee-nest positions to populate with bees once placed
 * @param random RNG carried over from planning, used for the trees' own shape randomness
 */
public record IslandPlan(List<BlockPlacement> blocks, List<TreeSite> trees, List<MobSpawn> mobs,
                         List<BlockPos> hives, RandomSource random) {
    public record BlockPlacement(BlockPos pos, BlockState state) {}

    public record TreeSite(ConfiguredFeature<?, ?> feature, BlockPos pos) {}

    /** A mob to spawn at {@code pos} (the surface block) once generation finishes. */
    public record MobSpawn(EntityType<?> type, BlockPos pos) {}
}
