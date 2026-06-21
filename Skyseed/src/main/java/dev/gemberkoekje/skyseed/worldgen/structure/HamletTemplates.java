package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.world.level.block.BedBlock;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.DoorBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BedPart;
import net.minecraft.world.level.block.state.properties.DoubleBlockHalf;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * Code-authored Hamlet cottages, generated to {@code .nbt} structure templates at dev time (see
 * {@link DevStructureGenerator}). Three variants give the Hamlet island some variety; each has a bed so
 * the generator can find a spot for the resident villager. You can replace any of these with a
 * structure-block-authored {@code .nbt} of the same name and the game will load that instead.
 */
public final class HamletTemplates {
    private HamletTemplates() {}

    /** Write the cottage templates into the data dir if they are not already present. */
    public static void generateInto(Path structureDir) throws IOException {
        writeIfAbsent(structureDir.resolve("cottage_oak.nbt"),
                cottage(7, Blocks.OAK_PLANKS.defaultBlockState(), Blocks.OAK_PLANKS.defaultBlockState(),
                        Blocks.OAK_LOG.defaultBlockState(), Blocks.OAK_PLANKS.defaultBlockState(), true));
        writeIfAbsent(structureDir.resolve("cottage_spruce.nbt"),
                cottage(7, Blocks.COBBLESTONE.defaultBlockState(), Blocks.SPRUCE_PLANKS.defaultBlockState(),
                        Blocks.SPRUCE_LOG.defaultBlockState(), Blocks.SPRUCE_PLANKS.defaultBlockState(), false));
        writeIfAbsent(structureDir.resolve("cottage_small.nbt"),
                cottage(5, Blocks.OAK_PLANKS.defaultBlockState(), Blocks.OAK_PLANKS.defaultBlockState(),
                        Blocks.STRIPPED_OAK_LOG.defaultBlockState(), Blocks.OAK_PLANKS.defaultBlockState(), true));
    }

    private static void writeIfAbsent(Path file, Map<BlockPos, BlockState> blocks) throws IOException {
        if (!java.nio.file.Files.exists(file)) {
            StructureWriter.write(blocks, file);
            Skyseed.LOGGER.info("[skyseed] generated structure template {}", file.getFileName());
        }
    }

    /** An n×n cottage (n odd): plank floor, walled with log corner posts, flat roof, a door, windows, a bed. */
    private static Map<BlockPos, BlockState> cottage(int n, BlockState floor, BlockState wall, BlockState post,
                                                     BlockState roof, boolean doorOnNorth) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final BlockState glass = Blocks.GLASS.defaultBlockState();
        final int max = n - 1;
        final int mid = n / 2;

        for (int x = 0; x < n; x++) {
            for (int z = 0; z < n; z++) {
                m.put(new BlockPos(x, 0, z), floor);
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                final boolean corner = (x == 0 || x == max) && (z == 0 || z == max);
                if (perim) {
                    for (int h = 1; h <= 3; h++) {
                        m.put(new BlockPos(x, h, z), corner ? post : wall);
                    }
                }
                m.put(new BlockPos(x, 4, z), roof);
            }
        }

        // Windows on the two side walls, plus the wall opposite the door.
        m.put(new BlockPos(0, 2, mid), glass);
        m.put(new BlockPos(max, 2, mid), glass);
        m.put(new BlockPos(mid, 2, doorOnNorth ? max : 0), glass);

        // Door, opening into the cottage.
        final int doorZ = doorOnNorth ? 0 : max;
        final Direction face = doorOnNorth ? Direction.SOUTH : Direction.NORTH;
        m.put(new BlockPos(mid, 1, doorZ), Blocks.OAK_DOOR.defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.LOWER).setValue(DoorBlock.FACING, face));
        m.put(new BlockPos(mid, 2, doorZ), Blocks.OAK_DOOR.defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.UPPER).setValue(DoorBlock.FACING, face));

        // Furniture.
        m.put(new BlockPos(1, 1, 1), Blocks.RED_BED.defaultBlockState()
                .setValue(BedBlock.PART, BedPart.FOOT).setValue(BedBlock.FACING, Direction.SOUTH));
        m.put(new BlockPos(1, 1, 2), Blocks.RED_BED.defaultBlockState()
                .setValue(BedBlock.PART, BedPart.HEAD).setValue(BedBlock.FACING, Direction.SOUTH));
        m.put(new BlockPos(max - 1, 1, max - 1), Blocks.CRAFTING_TABLE.defaultBlockState());
        m.put(new BlockPos(max - 1, 1, 1), Blocks.TORCH.defaultBlockState());

        return m;
    }
}
