package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The <b>End Portal Chamber</b> (SKYENDPLAN Phase 1) — what the End Portal Seed grows: a roofed, stronghold-styled
 * 11×11 mottled-stone-brick room holding the vanilla <b>12-frame End portal ring</b> (frames empty, facing inward) over
 * a 3×3 portal void. Fill the twelve frames with Eyes of Ender to light the portal to the End. A doorway, corner
 * torches, a couple of bookshelves and a lurking silverfish brick give it the stronghold flavour.
 */
public final class PortalChamberTemplates {
    private PortalChamberTemplates() {}

    private static final BlockState AIR = Blocks.AIR.defaultBlockState();
    private static final BlockState BRICK = Blocks.STONE_BRICKS.defaultBlockState();
    private static final BlockState CRACKED = Blocks.CRACKED_STONE_BRICKS.defaultBlockState();
    private static final BlockState MOSSY = Blocks.MOSSY_STONE_BRICKS.defaultBlockState();

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("chamber.nbt"), chamber());
    }

    private static Built chamber() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int max = 10, mid = 5;          // 11×11 footprint (0..10)

        // Foundation (y0) + floor (y1) of mottled stone brick.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), mottle(x, z));
                m.put(new BlockPos(x, 1, z), mottle(x + 3, z));
            }
        }
        // Perimeter walls (y2..4) with a doorway on the +Z wall, and a ceiling (y5).
        for (int y = 2; y <= 4; y++) {
            for (int x = 0; x <= max; x++) {
                for (int z = 0; z <= max; z++) {
                    if (x != 0 && x != max && z != 0 && z != max) {
                        continue;
                    }
                    if (z == max && x == mid && (y == 2 || y == 3)) {
                        continue; // doorway
                    }
                    m.put(new BlockPos(x, y, z), mottle(x + y, z));
                }
            }
        }
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 5, z), mottle(x, z + 5));
            }
        }

        // The 12-frame End portal ring (a 5×5 at x,z = 3..7), frames empty + facing inward toward the 3×3 void.
        for (int x = 4; x <= 6; x++) {
            frame(m, x, 3, Direction.SOUTH);   // north edge faces +Z (inward)
            frame(m, x, 7, Direction.NORTH);   // south edge faces −Z
        }
        for (int z = 4; z <= 6; z++) {
            frame(m, 3, z, Direction.EAST);    // west edge faces +X
            frame(m, 7, z, Direction.WEST);    // east edge faces −X
        }
        for (int x = 4; x <= 6; x++) {         // clear the centre 3×3 at the frame level — the portal void
            for (int z = 4; z <= 6; z++) {
                m.put(new BlockPos(x, 1, z), AIR);
            }
        }

        // Lighting + stronghold flavour.
        for (final int[] c : new int[][]{{1, 1}, {max - 1, 1}, {1, max - 1}, {max - 1, max - 1}}) {
            m.put(new BlockPos(c[0], 2, c[1]), Blocks.TORCH.defaultBlockState());   // corner torches
        }
        m.put(new BlockPos(1, 2, 4), Blocks.BOOKSHELF.defaultBlockState());          // a small library nook
        m.put(new BlockPos(1, 2, 5), Blocks.BOOKSHELF.defaultBlockState());
        m.put(new BlockPos(1, 3, 4), Blocks.BOOKSHELF.defaultBlockState());
        m.put(new BlockPos(2, 1, 2), Blocks.INFESTED_STONE_BRICKS.defaultBlockState());   // a lurking silverfish
        m.put(new BlockPos(max - 2, 1, max - 2), Blocks.INFESTED_STONE_BRICKS.defaultBlockState());

        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:stone_bricks");
        return new Built(m, bes);
    }

    private static void frame(Map<BlockPos, BlockState> m, int x, int z, Direction facing) {
        m.put(new BlockPos(x, 1, z), Blocks.END_PORTAL_FRAME.defaultBlockState()
                .setValue(BlockStateProperties.HORIZONTAL_FACING, facing)
                .setValue(BlockStateProperties.EYE, false));
    }

    /** Mostly plain stone brick, with ~20% cracked and ~10% mossy scattered deterministically for an aged look. */
    private static BlockState mottle(int a, int b) {
        final int h = Math.floorMod(a * 7 + b * 13, 10);
        return h < 2 ? CRACKED : h < 3 ? MOSSY : BRICK;
    }
}
