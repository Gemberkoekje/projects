package dev.gemberkoekje.skyseed.worldgen;

import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;

/**
 * Places island blocks into the world. Deliberately a near-pure function of
 * {@code (ServerLevel, BlockPos center, RandomSource)} — independent of items/entities so it stays
 * reusable (the curated start island can share it) and testable (plan §6).
 *
 * <p>Milestone 3 only places a flat stone disk as a placeholder, to prove the throw → timer →
 * generation loop end to end. Milestone 4 replaces this with the layered teardrop blob, and a theme
 * parameter is threaded through from milestone 6.
 */
public final class IslandGenerator {
    private IslandGenerator() {}

    /** Flat one-block-thick stone disk centred on {@code center}. */
    public static void generatePlaceholder(ServerLevel level, BlockPos center, RandomSource random) {
        final int radius = 3;
        final BlockState stone = Blocks.STONE.defaultBlockState();
        for (int dx = -radius; dx <= radius; dx++) {
            for (int dz = -radius; dz <= radius; dz++) {
                if (dx * dx + dz * dz <= radius * radius + 1) { // +1 rounds the corners
                    // UPDATE_CLIENTS only: show the block without triggering neighbour/physics
                    // cascades. Tick-budgeted bulk placement is milestone 9.
                    level.setBlock(center.offset(dx, 0, dz), stone, Block.UPDATE_CLIENTS);
                }
            }
        }
    }
}
