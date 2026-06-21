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
 * @param jigsaws jigsaw structures to assemble on the island once the terrain has landed; a villager is
 *                spawned at every bed found in each one
 * @param random RNG carried over from planning, used for the trees' own shape randomness
 */
public record IslandPlan(List<BlockPlacement> blocks, List<TreeSite> trees, List<MobSpawn> mobs,
                         List<BlockPos> hives, List<JigsawSite> jigsaws, RandomSource random) {
    public record BlockPlacement(BlockPos pos, BlockState state) {}

    public record TreeSite(ConfiguredFeature<?, ?> feature, BlockPos pos) {}

    /**
     * A mob to spawn once generation finishes. For a land mob {@code pos} is the surface block (it
     * spawns on top); for a water mob ({@code inWater}) {@code pos} is a pond water block (it spawns there).
     */
    public record MobSpawn(EntityType<?> type, BlockPos pos, boolean inWater) {}

    /**
     * A jigsaw structure to assemble at {@code origin} (the anchor tile) once the terrain is down.
     * {@code pad} is the half-width searched afterwards for beds (one villager spawns per bed).
     */
    public record JigsawSite(net.minecraft.resources.ResourceLocation pool, net.minecraft.resources.ResourceLocation target,
                             int depth, int pad, BlockPos origin) {}
}
