package dev.gemberkoekje.skyseed.worldgen;

import net.minecraft.core.BlockPos;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.LeavesBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.util.Map;

/**
 * Hand-built trees written straight into the streamed block plan — vanilla features (mangrove, azalea, ice spike)
 * that won't place dry on a freshly-grown island, so {@link DecorationPlanner} stamps them in block by block.
 */
final class CustomTrees {
    private CustomTrees() {}

    /** A hand-built mangrove (logs + roots + persistent leaves) added straight into the block plan. */
    static void buildMangrove(Map<BlockPos, BlockState> blockMap, BlockPos ground, RandomSource random) {
        final BlockState log = Blocks.MANGROVE_LOG.defaultBlockState();
        final BlockState leaves = Blocks.MANGROVE_LEAVES.defaultBlockState().setValue(LeavesBlock.PERSISTENT, Boolean.TRUE);
        final BlockState roots = Blocks.MANGROVE_ROOTS.defaultBlockState();
        final BlockState muddy = Blocks.MUDDY_MANGROVE_ROOTS.defaultBlockState();
        final int gx = ground.getX(), gy = ground.getY(), gz = ground.getZ();
        final int trunk = 4 + random.nextInt(2); // 4-5 logs

        blockMap.put(new BlockPos(gx, gy, gz), roots);
        blockMap.put(new BlockPos(gx, gy - 1, gz), muddy);
        for (int[] d : new int[][]{ {1, 0}, {-1, 0}, {0, 1}, {0, -1} }) {
            if (random.nextInt(3) != 0) {
                blockMap.put(new BlockPos(gx + d[0], gy, gz + d[1]), roots);
            }
        }
        for (int i = 1; i <= trunk; i++) {
            blockMap.put(new BlockPos(gx, gy + i, gz), log);
        }
        leafBlob(blockMap, gx, gy + trunk - 1, gz, 2, leaves);
        leafBlob(blockMap, gx, gy + trunk, gz, 1, leaves);
        blockMap.put(new BlockPos(gx, gy + trunk + 1, gz), leaves);
        for (int[] d : new int[][]{ {1, 0}, {-1, 0}, {0, 1}, {0, -1} }) {
            blockMap.put(new BlockPos(gx + d[0], gy + trunk + 1, gz + d[1]), leaves);
        }
    }

    private static void leafBlob(Map<BlockPos, BlockState> blockMap, int gx, int y, int gz, int radius, BlockState leaves) {
        for (int dx = -radius; dx <= radius; dx++) {
            for (int dz = -radius; dz <= radius; dz++) {
                if (dx == 0 && dz == 0) {
                    continue; // trunk
                }
                if (Math.abs(dx) == radius && Math.abs(dz) == radius) {
                    continue; // trim corners
                }
                blockMap.putIfAbsent(new BlockPos(gx + dx, y, gz + dz), leaves);
            }
        }
    }

    /** A hand-built azalea tree (oak trunk + persistent azalea / flowering-azalea canopy). */
    static void buildAzalea(Map<BlockPos, BlockState> blockMap, BlockPos ground, RandomSource random) {
        final BlockState log = Blocks.OAK_LOG.defaultBlockState();
        final BlockState leaves = Blocks.AZALEA_LEAVES.defaultBlockState().setValue(LeavesBlock.PERSISTENT, Boolean.TRUE);
        final BlockState flowering = Blocks.FLOWERING_AZALEA_LEAVES.defaultBlockState().setValue(LeavesBlock.PERSISTENT, Boolean.TRUE);
        final int gx = ground.getX(), gy = ground.getY(), gz = ground.getZ();
        final int trunk = 2 + random.nextInt(2); // 2-3 logs
        for (int i = 1; i <= trunk; i++) {
            blockMap.put(new BlockPos(gx, gy + i, gz), log);
        }
        final int topY = gy + trunk;
        azaleaLayer(blockMap, gx, topY, gz, 2, true, leaves, flowering, random);
        azaleaLayer(blockMap, gx, topY + 1, gz, 1, false, leaves, flowering, random);
        blockMap.putIfAbsent(new BlockPos(gx, topY + 2, gz), random.nextInt(3) == 0 ? flowering : leaves);
    }

    private static void azaleaLayer(Map<BlockPos, BlockState> blockMap, int gx, int y, int gz, int radius,
                                    boolean skipCenter, BlockState leaves, BlockState flowering, RandomSource random) {
        for (int dx = -radius; dx <= radius; dx++) {
            for (int dz = -radius; dz <= radius; dz++) {
                if (skipCenter && dx == 0 && dz == 0) {
                    continue; // trunk
                }
                if (Math.abs(dx) == radius && Math.abs(dz) == radius) {
                    continue; // trim corners
                }
                blockMap.putIfAbsent(new BlockPos(gx + dx, y, gz + dz), random.nextInt(4) == 0 ? flowering : leaves);
            }
        }
    }

    /** A hand-built ice spike — a packed-ice spire with a flared base (vanilla's feature won't place here). */
    static void buildIceSpike(Map<BlockPos, BlockState> blockMap, BlockPos ground, RandomSource random) {
        final BlockState ice = Blocks.PACKED_ICE.defaultBlockState();
        final int gx = ground.getX(), gy = ground.getY(), gz = ground.getZ();
        final int h = 5 + random.nextInt(5); // 5-9 tall
        for (int i = 0; i < h; i++) {
            blockMap.put(new BlockPos(gx, gy + 1 + i, gz), ice);
            if (i < 2) { // flared base
                for (int[] d : new int[][]{ {1, 0}, {-1, 0}, {0, 1}, {0, -1} }) {
                    if (random.nextInt(3) != 0) {
                        blockMap.putIfAbsent(new BlockPos(gx + d[0], gy + 1 + i, gz + d[1]), ice);
                    }
                }
            }
        }
    }
}
