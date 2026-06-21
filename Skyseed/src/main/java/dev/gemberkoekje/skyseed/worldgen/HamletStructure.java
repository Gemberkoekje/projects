package dev.gemberkoekje.skyseed.worldgen;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.world.level.block.BedBlock;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.DoorBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BedPart;
import net.minecraft.world.level.block.state.properties.DoubleBlockHalf;

import java.util.List;
import java.util.Map;

/**
 * The Hamlet Island's cottage (SKYVILLAGESPLAN → Hamlet) — a hand-authored 7×7 oak-and-cobblestone
 * cabin with one bed, a crafting table, a torch and a door, stamped into the island's block plan on a
 * levelled pad. Hand-built like the start island and the custom trees; the bed gives the spawned
 * villager a home POI to claim. Returns the interior tile where the villager should spawn.
 */
public final class HamletStructure {
    private HamletStructure() {}

    public static BlockPos plan(Map<BlockPos, BlockState> blockMap, List<BlockPos> surfaceList, BlockPos center, int topDome) {
        final BlockState grass = Blocks.GRASS_BLOCK.defaultBlockState();
        final BlockState dirt = Blocks.DIRT.defaultBlockState();
        final BlockState floor = Blocks.OAK_PLANKS.defaultBlockState();
        final BlockState plank = Blocks.OAK_PLANKS.defaultBlockState();
        final BlockState post = Blocks.OAK_LOG.defaultBlockState();
        final BlockState glass = Blocks.GLASS.defaultBlockState();

        final int cx = center.getX();
        final int cz = center.getZ();
        final int gy = center.getY() + topDome; // floor level = the island's high, flat-ish centre

        // Level a 9×9 yard to the floor height: clear anything above, ensure solid ground below.
        for (int dx = -4; dx <= 4; dx++) {
            for (int dz = -4; dz <= 4; dz++) {
                final int wx = cx + dx, wz = cz + dz;
                for (int y = gy + 1; y <= center.getY() + topDome + 6; y++) {
                    blockMap.remove(new BlockPos(wx, y, wz));
                }
                blockMap.put(new BlockPos(wx, gy, wz), grass);
                blockMap.put(new BlockPos(wx, gy - 1, wz), dirt);
                blockMap.put(new BlockPos(wx, gy - 2, wz), dirt);
            }
        }
        // Keep ground decoration / trees off the whole yard.
        surfaceList.removeIf(p -> Math.abs(p.getX() - cx) <= 4 && Math.abs(p.getZ() - cz) <= 4);

        // 7×7 cabin: plank floor, walls with log corner posts, a flat plank roof, hollow interior.
        for (int dx = -3; dx <= 3; dx++) {
            for (int dz = -3; dz <= 3; dz++) {
                final int wx = cx + dx, wz = cz + dz;
                blockMap.put(new BlockPos(wx, gy, wz), floor);
                final boolean perimeter = Math.abs(dx) == 3 || Math.abs(dz) == 3;
                final boolean cornerPost = Math.abs(dx) == 3 && Math.abs(dz) == 3;
                for (int h = 1; h <= 3; h++) {
                    final BlockPos p = new BlockPos(wx, gy + h, wz);
                    if (perimeter) {
                        blockMap.put(p, cornerPost ? post : plank);
                    } else {
                        blockMap.remove(p); // hollow interior
                    }
                }
                blockMap.put(new BlockPos(wx, gy + 4, wz), plank); // flat roof
            }
        }

        // Windows (glass) at the three non-door wall midpoints, eye height.
        blockMap.put(new BlockPos(cx + 3, gy + 2, cz), glass);
        blockMap.put(new BlockPos(cx - 3, gy + 2, cz), glass);
        blockMap.put(new BlockPos(cx, gy + 2, cz + 3), glass);

        // Door on the -Z wall, opening into the cabin (faces +Z / south).
        final BlockState doorLower = Blocks.OAK_DOOR.defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.LOWER).setValue(DoorBlock.FACING, Direction.SOUTH);
        final BlockState doorUpper = Blocks.OAK_DOOR.defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.UPPER).setValue(DoorBlock.FACING, Direction.SOUTH);
        blockMap.put(new BlockPos(cx, gy + 1, cz - 3), doorLower);
        blockMap.put(new BlockPos(cx, gy + 2, cz - 3), doorUpper);

        // Furnishings on the floor.
        final BlockState bedFoot = Blocks.RED_BED.defaultBlockState()
                .setValue(BedBlock.PART, BedPart.FOOT).setValue(BedBlock.FACING, Direction.NORTH);
        final BlockState bedHead = Blocks.RED_BED.defaultBlockState()
                .setValue(BedBlock.PART, BedPart.HEAD).setValue(BedBlock.FACING, Direction.NORTH);
        blockMap.put(new BlockPos(cx - 2, gy + 1, cz + 2), bedFoot);
        blockMap.put(new BlockPos(cx - 2, gy + 1, cz + 1), bedHead);
        blockMap.put(new BlockPos(cx + 2, gy + 1, cz + 2), Blocks.CRAFTING_TABLE.defaultBlockState());
        blockMap.put(new BlockPos(cx + 2, gy + 1, cz - 2), Blocks.TORCH.defaultBlockState());

        // Spawn the villager on the floor, in the middle.
        return new BlockPos(cx, gy + 1, cz);
    }
}
