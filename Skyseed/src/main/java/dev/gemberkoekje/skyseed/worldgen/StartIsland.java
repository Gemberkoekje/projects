package dev.gemberkoekje.skyseed.worldgen;

import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.LeavesBlock;
import net.minecraft.world.level.block.state.BlockState;

/**
 * The curated starting island (plan §6/§8): hand-authored block-by-block so it is soft-lock-proof by
 * construction — a small grass/dirt teardrop with one guaranteed oak tree. From this a player can
 * always craft the first (2×2, no table) Forest Skyseed: chop the tree → planks, dig the island →
 * dirt. Deliberately NOT procedural, so it can never roll without wood.
 */
public final class StartIsland {
    private static final int FLAGS = Block.UPDATE_CLIENTS;

    private StartIsland() {}

    /** Builds the island centred on {@code center} (its grass surface) and returns the spawn pos on top. */
    public static BlockPos build(ServerLevel level, BlockPos center) {
        final BlockState grass = Blocks.GRASS_BLOCK.defaultBlockState();
        final BlockState dirt = Blocks.DIRT.defaultBlockState();
        final BlockState stone = Blocks.STONE.defaultBlockState();
        final BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();

        // Layered teardrop: grass top, dirt body, a small stone tip — radius shrinks with depth.
        layer(level, center, 0, 3, grass, pos);
        layer(level, center, -1, 3, dirt, pos);
        layer(level, center, -2, 3, dirt, pos);
        layer(level, center, -3, 2, dirt, pos);
        layer(level, center, -4, 1, stone, pos);

        // A guaranteed oak, offset from the spawn point so the player doesn't stand in the trunk.
        buildOak(level, center.offset(2, 0, 2), pos);

        return center.above(); // stand on the grass at the centre
    }

    private static void layer(ServerLevel level, BlockPos center, int dy, int radius, BlockState state,
                              BlockPos.MutableBlockPos pos) {
        final int r2 = radius * radius + 1; // +1 rounds the corners
        for (int dx = -radius; dx <= radius; dx++) {
            for (int dz = -radius; dz <= radius; dz++) {
                if (dx * dx + dz * dz <= r2) {
                    level.setBlock(pos.set(center.getX() + dx, center.getY() + dy, center.getZ() + dz), state, FLAGS);
                }
            }
        }
    }

    private static void buildOak(ServerLevel level, BlockPos ground, BlockPos.MutableBlockPos pos) {
        final BlockState log = Blocks.OAK_LOG.defaultBlockState();
        // Persistent leaves so the curated tree never decays.
        final BlockState leaves = Blocks.OAK_LEAVES.defaultBlockState().setValue(LeavesBlock.PERSISTENT, Boolean.TRUE);
        final int gx = ground.getX(), gy = ground.getY(), gz = ground.getZ();

        // Trunk: 5 logs above the ground (gy is the grass block).
        for (int i = 1; i <= 5; i++) {
            level.setBlock(pos.set(gx, gy + i, gz), log, FLAGS);
        }
        // Canopy: two 5x5 rings (corners trimmed) around the top logs, a 3x3, then a plus cap.
        leafSquare(level, gx, gy + 4, gz, 2, leaves, pos);
        leafSquare(level, gx, gy + 5, gz, 2, leaves, pos);
        leafSquare(level, gx, gy + 6, gz, 1, leaves, pos);
        level.setBlock(pos.set(gx, gy + 7, gz), leaves, FLAGS);
        level.setBlock(pos.set(gx + 1, gy + 7, gz), leaves, FLAGS);
        level.setBlock(pos.set(gx - 1, gy + 7, gz), leaves, FLAGS);
        level.setBlock(pos.set(gx, gy + 7, gz + 1), leaves, FLAGS);
        level.setBlock(pos.set(gx, gy + 7, gz - 1), leaves, FLAGS);
    }

    /** A filled leaf square of the given radius, skipping the trunk column and the far corners. */
    private static void leafSquare(ServerLevel level, int gx, int y, int gz, int radius, BlockState leaves,
                                   BlockPos.MutableBlockPos pos) {
        for (int dx = -radius; dx <= radius; dx++) {
            for (int dz = -radius; dz <= radius; dz++) {
                if (dx == 0 && dz == 0) {
                    continue; // trunk
                }
                if (Math.abs(dx) == radius && Math.abs(dz) == radius) {
                    continue; // trim corners
                }
                level.setBlock(pos.set(gx + dx, y, gz + dz), leaves, FLAGS);
            }
        }
    }
}
