package dev.gemberkoekje.skyseed.worldgen;

import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.entity.MobSpawnType;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.chunk.ChunkGenerator;

/**
 * Drains an {@link IslandPlan} into the world a bounded number of blocks per tick, so an island never
 * places thousands of blocks in a single tick (README → Generation algorithm). The gradual fill doubles as a "grows in"
 * animation. Trees (configured features) are heavier than a setBlock, so they go a couple per tick
 * once the solid blocks have landed; mobs are spawned last, once the whole island exists.
 */
public final class GenerationJob {
    private static final int BLOCKS_PER_TICK = 512;
    private static final int TREES_PER_TICK = 2;

    private final ServerLevel level;
    private final IslandPlan plan;
    private int blockIndex = 0;
    private int treeIndex = 0;
    private boolean mobsSpawned = false;

    public GenerationJob(ServerLevel level, IslandPlan plan) {
        this.level = level;
        this.plan = plan;
    }

    /** Advance one tick. @return true once the whole island (blocks + trees + mobs) has been placed. */
    public boolean tick() {
        final int blockCount = plan.blocks().size();
        int budget = BLOCKS_PER_TICK;
        while (budget-- > 0 && blockIndex < blockCount) {
            IslandPlan.BlockPlacement bp = plan.blocks().get(blockIndex++);
            // UPDATE_CLIENTS only: show the block without neighbour/physics cascades.
            level.setBlock(bp.pos(), bp.state(), Block.UPDATE_CLIENTS);
        }
        if (blockIndex < blockCount) {
            return false;
        }

        final int treeCount = plan.trees().size();
        if (treeIndex < treeCount) {
            final ChunkGenerator generator = level.getChunkSource().getGenerator();
            int treeBudget = TREES_PER_TICK;
            while (treeBudget-- > 0 && treeIndex < treeCount) {
                IslandPlan.TreeSite ts = plan.trees().get(treeIndex++);
                ts.feature().place(level, generator, plan.random(), ts.pos());
            }
            if (treeIndex < treeCount) {
                return false;
            }
        }

        if (!mobsSpawned) {
            spawnMobs();
            mobsSpawned = true;
        }
        return true;
    }

    /** Spawn the planned mobs on top of their surface block, skipping spots that aren't clear. */
    private void spawnMobs() {
        for (IslandPlan.MobSpawn ms : plan.mobs()) {
            final BlockPos surface = ms.pos();
            final BlockPos spawnPos = surface.above();
            // Need solid ground and two blocks of standing room — but plants (flowers, grass, mushrooms)
            // don't block a mob, so allow those; only skip on no ground or a real obstruction (trunk, leaves).
            if (level.getBlockState(surface).isAir()
                    || level.getBlockState(spawnPos).blocksMotion()
                    || level.getBlockState(spawnPos.above()).blocksMotion()) {
                continue;
            }
            ms.type().spawn(level, spawnPos, MobSpawnType.SPAWNER);
        }
    }
}
