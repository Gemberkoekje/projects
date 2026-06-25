package dev.gemberkoekje.skyseed.worldgen;

import net.minecraft.core.BlockPos;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.util.RandomSource;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.levelgen.feature.ConfiguredFeature;

import java.util.List;
import java.util.Optional;
import java.util.Set;

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
 * @param animals dedicated Animal Island spawns — a rolled pack of farm animals placed in the enclosure
 * @param random RNG carried over from planning, used for the trees' own shape randomness
 * @param twinTheme if present, grow this theme at the dimension-linked coordinate in the other dimension (the
 *                  Ruined Portal twin — set by the theme's or a rolled rare structure's {@code twin} field)
 * @param fluidTicks water sources (placed physics-free with the rest of the blocks) to nudge into flowing once the
 *                   island has landed — e.g. a Ladder Island waterfall. {@link GenerationJob} schedules each a tick.
 * @param scatterPositions ground-cover positions (a subset of {@code blocks}) that {@link GenerationJob} places AFTER
 *                   the trees, skipping any a tree has taken — so a snow layer can't block a tree from forming.
 * @param snow per-column probability (0–1; 0 = off) that {@link GenerationJob} drapes a snow layer over the highest
 *                   block of a column of the finished island as the final step — ground, building roofs and tree tops
 *                   alike (a cold-biome island); below 1 it leaves icy patches showing
 */
public record IslandPlan(List<BlockPlacement> blocks, List<TreeSite> trees, List<MobSpawn> mobs,
                         List<BlockPos> hives, List<JigsawSite> jigsaws, List<AnimalSpawn> animals,
                         RandomSource random, Optional<ResourceLocation> twinTheme, List<BlockPos> fluidTicks,
                         Set<BlockPos> scatterPositions, float snow) {
    public record BlockPlacement(BlockPos pos, BlockState state) {}

    public record TreeSite(ConfiguredFeature<?, ?> feature, BlockPos pos) {}

    /**
     * A mob to spawn once generation finishes. For a land mob {@code pos} is the surface block (it
     * spawns on top); for a water mob ({@code inWater}) {@code pos} is a pond water block (it spawns there).
     */
    public record MobSpawn(EntityType<?> type, BlockPos pos, boolean inWater) {}

    /**
     * A guaranteed Animal Island spawn placed in the enclosure. {@code baby} ages it down; {@code inWater}
     * spawns it submerged (Aquarium). Sheep are given a random wool colour at spawn.
     */
    public record AnimalSpawn(EntityType<?> type, BlockPos pos, boolean baby, boolean inWater) {}

    /**
     * A jigsaw structure to assemble at {@code origin} (the anchor tile) once the terrain is down.
     * {@code pad} is the half-width searched afterwards for beds (one villager spawns per bed);
     * {@code ironGolems} golems are spawned at the centre once assembled; {@code reach} is the half-extent the
     * post-assembly connection-link and (when {@code > 0}) path/bridge surfacing passes scan (SKYJIGSAWPLAN §3a).
     */
    public record JigsawSite(ResourceLocation pool, ResourceLocation target,
                             int depth, int pad, int ironGolems, BlockPos origin, int reach,
                             String capPrefix, int capCount, String capFiller) {}
}
