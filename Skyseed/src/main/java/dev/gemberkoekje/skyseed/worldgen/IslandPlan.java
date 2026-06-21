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
 * @param villagers villagers to spawn once placed (for the village islands' curated structures)
 * @param structures NBT building templates to stamp on the island once the terrain has landed
 * @param random RNG carried over from planning, used for the trees' own shape randomness
 */
public record IslandPlan(List<BlockPlacement> blocks, List<TreeSite> trees, List<MobSpawn> mobs,
                         List<BlockPos> hives, List<VillagerSpawn> villagers,
                         List<StructurePlacement> structures, RandomSource random) {
    public record BlockPlacement(BlockPos pos, BlockState state) {}

    public record TreeSite(ConfiguredFeature<?, ?> feature, BlockPos pos) {}

    /**
     * A mob to spawn once generation finishes. For a land mob {@code pos} is the surface block (it
     * spawns on top); for a water mob ({@code inWater}) {@code pos} is a pond water block (it spawns there).
     */
    public record MobSpawn(EntityType<?> type, BlockPos pos, boolean inWater) {}

    /** A villager to spawn inside a curated structure. {@code profession} empty = unemployed (player assigns it). */
    public record VillagerSpawn(BlockPos pos, String profession) {}

    /** An NBT structure template to place at {@code origin} (its 0,0,0 corner) once the terrain is down. */
    public record StructurePlacement(net.minecraft.resources.ResourceLocation template, BlockPos origin) {}
}
