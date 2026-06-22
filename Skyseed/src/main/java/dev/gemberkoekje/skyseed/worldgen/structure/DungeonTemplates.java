package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.FrontAndTop;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.JigsawBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;
import net.minecraft.world.level.block.state.properties.DoorHingeSide;
import net.minecraft.world.level.block.state.properties.DoubleBlockHalf;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * Code-authored Dungeon rooms, generated to {@code .nbt} at dev time (see {@link DevStructureGenerator}).
 * A roomier 7×7×5 cobblestone box (5×5×3 inside) — a vanilla mob spawner in the centre and two loot chests
 * ({@code minecraft:chests/simple_dungeon}); three weighted variants per pool differ only in the spawner's
 * mob, like vanilla. The {@code hamlet_weathering} processor mosses the cobble. Two pools:
 * <ul>
 *   <li>{@code dungeon/buried} — the sealed box, meant to be sunk (theme {@code sink}) into a big island so
 *       you only find it by digging down into it.</li>
 *   <li>{@code dungeon/lair} — the same box with an open stepped stairwell down to a door, plus a few broken
 *       ruin stubs around the mouth, for the dedicated Dungeon island.</li>
 * </ul>
 * See {@code SKYSTRUCTURESPLAN.md}.
 */
public final class DungeonTemplates {
    private DungeonTemplates() {}

    private static final BlockState COBBLE = Blocks.COBBLESTONE.defaultBlockState();
    private static final BlockState AIR = Blocks.AIR.defaultBlockState();

    public static void generateInto(Path dir) throws IOException {
        for (final String mob : new String[]{"zombie", "skeleton", "spider"}) {
            writeIfAbsent(dir.resolve("buried_" + mob + ".nbt"), room("minecraft:" + mob, false));
            writeIfAbsent(dir.resolve("lair_" + mob + ".nbt"), room("minecraft:" + mob, true));
        }
    }

    /**
     * The dungeon box (7×7 outer, 5×5×3 interior). With {@code entrance} it also gets a door in the +Z wall, an
     * open stepped stairwell climbing back to the surface, and a few ruin stubs around the mouth. The interior
     * is explicit air so it carves a hollow when the box is sunk into solid island fill.
     */
    private static Built room(String mobId, boolean entrance) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int max = 6, mid = 3; // 7×7 footprint, 5×5 interior

        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), COBBLE); // floor
                m.put(new BlockPos(x, 4, z), COBBLE); // ceiling
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                for (int y = 1; y <= 3; y++) {
                    m.put(new BlockPos(x, y, z), perim ? COBBLE : AIR); // walls / hollow interior
                }
            }
        }
        // Spawner centred on the floor; a loot chest against the west and east walls, facing in.
        m.put(new BlockPos(mid, 1, mid), Blocks.SPAWNER.defaultBlockState());
        bes.put(new BlockPos(mid, 1, mid), mobSpawner(mobId));
        m.put(new BlockPos(1, 1, mid), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.EAST));
        bes.put(new BlockPos(1, 1, mid), lootChest("minecraft:chests/simple_dungeon"));
        m.put(new BlockPos(max - 1, 1, mid), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.WEST));
        bes.put(new BlockPos(max - 1, 1, mid), lootChest("minecraft:chests/simple_dungeon"));

        if (entrance) {
            addEntrance(m, max, mid);
            anchor(m, bes, new BlockPos(mid, 0, max), "minecraft:cobblestone"); // seat at the door wall so the stair stays centred-ish
        } else {
            anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:cobblestone"); // sealed box: seat at its own centre
        }
        return new Built(m, bes);
    }

    /**
     * A door in the +Z wall and an open stepped stairwell climbing from the chamber floor (nbt y0) back up to
     * the island surface (nbt y5, where {@code sink:5} seats it), plus a handful of broken ruin stubs flanking
     * the mouth. The trench is left open to the sky — the side walls are the island's own cobble.
     */
    private static void addEntrance(Map<BlockPos, BlockState> m, int max, int mid) {
        // Doorway: clear the wall at the +Z face and drop in a closed door, facing out toward the stair.
        m.remove(new BlockPos(mid, 1, max));
        m.remove(new BlockPos(mid, 2, max));
        final BlockState lower = Blocks.DARK_OAK_DOOR.defaultBlockState()
                .setValue(BlockStateProperties.HORIZONTAL_FACING, Direction.SOUTH)
                .setValue(BlockStateProperties.DOOR_HINGE, DoorHingeSide.LEFT)
                .setValue(BlockStateProperties.OPEN, false);
        m.put(new BlockPos(mid, 1, max), lower.setValue(BlockStateProperties.DOUBLE_BLOCK_HALF, DoubleBlockHalf.LOWER));
        m.put(new BlockPos(mid, 2, max), lower.setValue(BlockStateProperties.DOUBLE_BLOCK_HALF, DoubleBlockHalf.UPPER));

        // Stairwell at x=mid, climbing in +Z: a landing level with the floor (z=max+1), then five steps up to
        // the surface (z=max+6 sits at nbt y5). Carve the air above each tread up to the surface so it reads
        // as an open stepped cut you descend into.
        for (int i = 0; i <= 5; i++) {
            final int z = max + 1 + i;
            final int treadY = Math.max(0, i); // landing tread at y0, then y1..y5
            m.put(new BlockPos(mid, treadY, z), COBBLE);
            for (int y = treadY + 1; y <= 5; y++) {
                m.put(new BlockPos(mid, y, z), AIR);
            }
        }

        // Broken ruin stubs around the mouth (on the surface, nbt y6+), flanking the open stair.
        ruinStub(m, 1, max + 2, 2);
        ruinStub(m, 1, max + 4, 1);
        ruinStub(m, max - 1, max + 3, 2);
        ruinStub(m, max - 1, max + 5, 1);
        ruinStub(m, mid - 1, max + 6, 1);
        ruinStub(m, mid + 1, max + 6, 2);
    }

    /** A short broken cobble post sitting on the surface (nbt y6 up), {@code h} blocks tall. */
    private static void ruinStub(Map<BlockPos, BlockState> m, int x, int z, int h) {
        for (int i = 0; i < h; i++) {
            m.put(new BlockPos(x, 6 + i, z), COBBLE);
        }
    }
}
