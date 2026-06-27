package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.FrontAndTop;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.JigsawBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The Sprawling Dungeon (SKYDUNGEONPLAN Part A) — a recursing jigsaw of small cobblestone rooms + corridors, dressed
 * like the vanilla monster dungeon (cobble, mossed by the {@code hamlet_weathering} pool processor, mob spawners,
 * {@code chests/simple_dungeon}) and connected like a vanilla stronghold (a hub, straight/corner/cross corridors, and
 * varied terminal rooms). Every piece self-links through the {@code dungeon_complex/parts} pool via doorway connectors
 * ({@link StructureParts#jig} jigsaw blocks whose final state is air, so a connected pair opens into one passage and an
 * unreached one is a shallow alcove in the island stone). The {@code start} hub carries an open stepped stairwell up to
 * the surface — the only tell of the buried complex. Reused two ways: the Huge Rocky 2.5% rare, and the dedicated
 * {@code dungeon_large} island seed. See {@link DevStructureGenerator}.
 */
public final class DungeonComplexTemplates {
    private DungeonComplexTemplates() {}

    /** Every piece self-links through this pool — connector name = target = {@code skyseed:dungeon}. */
    private static final String POOL = "skyseed:dungeon_complex/parts";

    private static final BlockState COBBLE = Blocks.COBBLESTONE.defaultBlockState();
    private static final BlockState MOSSY = Blocks.MOSSY_COBBLESTONE.defaultBlockState();
    private static final BlockState AIR = Blocks.AIR.defaultBlockState();
    private static final BlockState BARS = Blocks.IRON_BARS.defaultBlockState();

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("start.nbt"), start());
        writeIfAbsent(dir.resolve("corridor.nbt"), corridor());
        writeIfAbsent(dir.resolve("corner.nbt"), corner());
        writeIfAbsent(dir.resolve("cross.nbt"), cross());
        writeIfAbsent(dir.resolve("cell_block.nbt"), cellBlock());
        writeIfAbsent(dir.resolve("flooded_room.nbt"), floodedRoom());
        writeIfAbsent(dir.resolve("lava_room.nbt"), lavaRoom());
        writeIfAbsent(dir.resolve("treasure_vault.nbt"), treasureVault());
        writeIfAbsent(dir.resolve("dead_end.nbt"), deadEnd());
        writeIfAbsent(dir.resolve("stairs_down.nbt"), stairsDown());
        writeIfAbsent(dir.resolve("shaft.nbt"), shaft());
        for (final String mob : new String[]{"zombie", "skeleton", "spider"}) {
            writeIfAbsent(dir.resolve("spawner_" + mob + ".nbt"), spawnerRoom("minecraft:" + mob));
        }
    }

    // ---- shared geometry helpers -------------------------------------------------------------------------------

    /** A cobblestone box: solid floor + ceiling, perimeter walls, hollow (air) interior. */
    private static void box(Map<BlockPos, BlockState> m, int x0, int x1, int z0, int z1, int floorY, int ceilY) {
        for (int x = x0; x <= x1; x++) {
            for (int z = z0; z <= z1; z++) {
                m.put(new BlockPos(x, floorY, z), (x + z) % 5 == 0 ? MOSSY : COBBLE);
                m.put(new BlockPos(x, ceilY, z), COBBLE);
                final boolean perim = x == x0 || x == x1 || z == z0 || z == z1;
                for (int y = floorY + 1; y < ceilY; y++) {
                    m.put(new BlockPos(x, y, z), perim ? ((x + y + z) % 7 == 0 ? MOSSY : COBBLE) : AIR);
                }
            }
        }
    }

    /** A 1-wide, 2-tall doorway connector at floor level {@code (x,1,z)} facing {@code dir}, self-linking into the parts pool. */
    private static void door(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int z, FrontAndTop dir) {
        doorAt(m, bes, x, 1, z, dir);
    }

    /** As {@link #door}, but at an arbitrary height {@code y} — so a piece's two doors can sit at different levels (the
     *  descending staircase / shaft), which makes the next piece attach lower and the complex go down, not just out. */
    private static void doorAt(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int y, int z, FrontAndTop dir) {
        m.put(new BlockPos(x, y, z), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, dir));
        bes.put(new BlockPos(x, y, z), jig("skyseed:dungeon", "skyseed:dungeon", POOL, "minecraft:air"));
        m.put(new BlockPos(x, y + 1, z), AIR);
    }

    /**
     * A one-way DESCENDING exit for the staircase/shaft, at the piece's low end: its name is {@code skyseed:dungeon_down},
     * which no ordinary piece <em>targets</em> — so a parent can never enter the stair from this low connector (which
     * would make it climb). It only ever expands downward (its target is {@code skyseed:dungeon}, so it still spawns the
     * ordinary pieces below). The entry stays the normal {@link #doorAt} at the high end, so stairs only ever go down.
     */
    private static void doorAtDown(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int y, int z, FrontAndTop dir) {
        m.put(new BlockPos(x, y, z), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, dir));
        bes.put(new BlockPos(x, y, z), jig("skyseed:dungeon_down", "skyseed:dungeon", POOL, "minecraft:air"));
        m.put(new BlockPos(x, y + 1, z), AIR);
    }

    /** A solid cobble fill (with the same mossy speckle as {@link #box}) — carved afterwards for descending pieces. */
    private static void fill(Map<BlockPos, BlockState> m, int x0, int x1, int z0, int z1, int y0, int y1) {
        for (int x = x0; x <= x1; x++) {
            for (int z = z0; z <= z1; z++) {
                for (int y = y0; y <= y1; y++) {
                    m.put(new BlockPos(x, y, z), (x + y + z) % 7 == 0 ? MOSSY : COBBLE);
                }
            }
        }
    }

    private static void chest(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockPos p, Direction facing) {
        m.put(p, Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, facing));
        bes.put(p, lootChest("minecraft:chests/simple_dungeon"));
    }

    // ---- pieces ------------------------------------------------------------------------------------------------

    /** The entrance hub: a 9×9 chamber with corridor doors N/W/E and an open stepped stairwell climbing +Z to the
     *  surface (seated by {@code sink:6}). A lantern lights the hub. */
    private static Built start() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        box(m, 0, 8, 0, 8, 0, 5);
        door(m, bes, 4, 0, FrontAndTop.NORTH_UP);
        door(m, bes, 0, 4, FrontAndTop.WEST_UP);
        door(m, bes, 8, 4, FrontAndTop.EAST_UP);
        m.put(new BlockPos(4, 4, 4), Blocks.LANTERN.defaultBlockState().setValue(BlockStateProperties.HANGING, true));

        // Entrance: a doorway in the +Z wall and a stepped stair climbing out and up to the surface (nbt y6 = sink 6),
        // open to the sky — the side walls are the island's own rock. Mirrors the old dungeon lair entrance.
        m.put(new BlockPos(4, 1, 8), AIR);
        m.put(new BlockPos(4, 2, 8), AIR);
        for (int i = 0; i <= 6; i++) {
            final int z = 9 + i;
            final int treadY = Math.min(6, i);
            m.put(new BlockPos(4, treadY, z), COBBLE);
            for (int y = treadY + 1; y <= 6; y++) {
                m.put(new BlockPos(4, y, z), AIR);
            }
        }
        anchor(m, bes, new BlockPos(4, 0, 4), "minecraft:cobblestone");
        return new Built(m, bes);
    }

    /** A straight 3-wide corridor, doors N + S (the jigsaw rotates it for E–W runs). */
    private static Built corridor() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        box(m, 0, 2, 0, 4, 0, 3);
        door(m, bes, 1, 0, FrontAndTop.NORTH_UP);
        door(m, bes, 1, 4, FrontAndTop.SOUTH_UP);
        m.put(new BlockPos(1, 2, 2), Blocks.COBWEB.defaultBlockState()); // a stray cobweb
        return new Built(m, bes);
    }

    /** An L-corner, doors N + E (rotated for the other three turns). */
    private static Built corner() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        box(m, 0, 4, 0, 4, 0, 3);
        door(m, bes, 2, 0, FrontAndTop.NORTH_UP);
        door(m, bes, 4, 2, FrontAndTop.EAST_UP);
        return new Built(m, bes);
    }

    /** A 4-way crossing — the branch points, doors on all sides + a torch-lit centre. */
    private static Built cross() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        box(m, 0, 4, 0, 4, 0, 4);
        door(m, bes, 2, 0, FrontAndTop.NORTH_UP);
        door(m, bes, 2, 4, FrontAndTop.SOUTH_UP);
        door(m, bes, 0, 2, FrontAndTop.WEST_UP);
        door(m, bes, 4, 2, FrontAndTop.EAST_UP);
        m.put(new BlockPos(2, 4, 2), Blocks.LANTERN.defaultBlockState().setValue(BlockStateProperties.HANGING, true));
        return new Built(m, bes);
    }

    /** A spawner room (vanilla-dungeon style): a mob spawner centred on the floor + two loot chests; one door (a
     *  destination). Three baked variants differ only in the spawner mob, like vanilla dungeons. */
    private static Built spawnerRoom(String mobId) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        box(m, 0, 6, 0, 6, 0, 4);
        door(m, bes, 3, 0, FrontAndTop.NORTH_UP);
        m.put(new BlockPos(3, 1, 3), Blocks.SPAWNER.defaultBlockState());
        bes.put(new BlockPos(3, 1, 3), mobSpawner(mobId));
        chest(m, bes, new BlockPos(1, 1, 3), Direction.EAST);
        chest(m, bes, new BlockPos(5, 1, 3), Direction.WEST);
        m.put(new BlockPos(1, 3, 1), Blocks.COBWEB.defaultBlockState());
        m.put(new BlockPos(5, 3, 5), Blocks.COBWEB.defaultBlockState());
        return new Built(m, bes);
    }

    /** A prison cell block (stronghold-style): iron-bar cells down a 7-long room, doors N + S (a pass-through). */
    private static Built cellBlock() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        box(m, 0, 4, 0, 6, 0, 4);
        door(m, bes, 2, 0, FrontAndTop.NORTH_UP);
        door(m, bes, 2, 6, FrontAndTop.SOUTH_UP);
        // Two barred cells against the walls (a barred face + a dividing bar), flanking the central walkway.
        for (final int wallX : new int[]{0, 4}) {
            final int barX = wallX == 0 ? 1 : 3;
            for (int z = 2; z <= 4; z++) {
                m.put(new BlockPos(barX, 1, z), BARS);
                m.put(new BlockPos(barX, 2, z), BARS);
            }
            m.put(new BlockPos(barX, 1, 3), AIR); // a gap to step into the cell
            m.put(new BlockPos(barX, 2, 3), AIR);
        }
        chest(m, bes, new BlockPos(1, 1, 3), Direction.EAST);
        return new Built(m, bes);
    }

    /** A flooded room — a sunken water pool with a drowned spawner on a central island, door N. */
    private static Built floodedRoom() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        box(m, 0, 6, 0, 6, 0, 4);
        door(m, bes, 3, 0, FrontAndTop.NORTH_UP);
        // Flood the interior floor with a thin water layer (contained by the cobble walls).
        for (int x = 1; x <= 5; x++) {
            for (int z = 1; z <= 5; z++) {
                m.put(new BlockPos(x, 1, z), Blocks.WATER.defaultBlockState());
            }
        }
        m.put(new BlockPos(3, 1, 3), COBBLE); // a dry plinth
        m.put(new BlockPos(3, 2, 3), Blocks.SPAWNER.defaultBlockState());
        bes.put(new BlockPos(3, 2, 3), mobSpawner("minecraft:drowned"));
        return new Built(m, bes);
    }

    /** A lava-hazard room — a magma/lava strip you must cross to the chest, door N. */
    private static Built lavaRoom() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        box(m, 0, 6, 0, 6, 0, 4);
        door(m, bes, 3, 0, FrontAndTop.NORTH_UP);
        for (int x = 1; x <= 5; x++) {
            m.put(new BlockPos(x, 0, 3), Blocks.LAVA.defaultBlockState()); // a lava trench across the room
        }
        m.put(new BlockPos(3, 0, 3), Blocks.MAGMA_BLOCK.defaultBlockState()); // a single stepping stone
        chest(m, bes, new BlockPos(3, 1, 5), Direction.NORTH);
        return new Built(m, bes);
    }

    /** The treasure vault — the capstone {@code centerpiece}: a barred strongroom with three chests + a gold accent,
     *  one door (terminal). */
    private static Built treasureVault() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        box(m, 0, 6, 0, 6, 0, 4);
        door(m, bes, 3, 0, FrontAndTop.NORTH_UP);
        // An inner barred wall just past the door — the strongroom gate, with a 1-wide opening.
        for (int x = 1; x <= 5; x++) {
            m.put(new BlockPos(x, 1, 2), BARS);
            m.put(new BlockPos(x, 2, 2), BARS);
        }
        m.put(new BlockPos(3, 1, 2), AIR);
        m.put(new BlockPos(3, 2, 2), AIR);
        m.put(new BlockPos(3, 1, 4), Blocks.GOLD_BLOCK.defaultBlockState());
        chest(m, bes, new BlockPos(2, 1, 4), Direction.EAST);
        chest(m, bes, new BlockPos(4, 1, 4), Direction.WEST);
        chest(m, bes, new BlockPos(3, 1, 5), Direction.NORTH);
        m.put(new BlockPos(1, 3, 5), Blocks.LANTERN.defaultBlockState().setValue(BlockStateProperties.HANGING, true));
        return new Built(m, bes);
    }

    /** A capped dead-end stub — one door, the other three sides solid, so a branch terminates as a small alcove. */
    private static Built deadEnd() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        box(m, 0, 2, 0, 2, 0, 3);
        door(m, bes, 1, 0, FrontAndTop.NORTH_UP);
        m.put(new BlockPos(1, 1, 1), Blocks.COBWEB.defaultBlockState());
        return new Built(m, bes);
    }

    /** A descending stepped staircase: a 1-wide cobble stair cut into a solid block, its top door (nbt y5) four blocks
     *  above its bottom door (nbt y1) — so whatever attaches below lands four levels down. The complex goes down, not
     *  just out. */
    private static Built stairsDown() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        fill(m, 0, 2, 0, 4, 0, 7);
        for (int z = 0; z <= 4; z++) {
            final int tread = 4 - z;               // descends one per step
            m.put(new BlockPos(1, tread + 1, z), AIR); // headroom over each tread
            m.put(new BlockPos(1, tread + 2, z), AIR);
        }
        doorAt(m, bes, 1, 5, 0, FrontAndTop.NORTH_UP);     // top — the entry (a parent connects here)
        doorAtDown(m, bes, 1, 1, 4, FrontAndTop.SOUTH_UP); // bottom — one-way descending exit (4 lower)
        return new Built(m, bes);
    }

    /** A ladder shaft (a "ladder room"): a solid 3×3 tower with a 1-wide ladder well down its core, its top door (nbt
     *  y7) six blocks above its bottom door (nbt y1) — a steeper plunge than the staircase. */
    private static Built shaft() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        fill(m, 0, 2, 0, 2, 0, 8);
        for (int y = 1; y <= 7; y++) {
            m.put(new BlockPos(1, y, 1), Blocks.LADDER.defaultBlockState()
                    .setValue(BlockStateProperties.HORIZONTAL_FACING, Direction.WEST)); // backed by the east wall
        }
        doorAt(m, bes, 1, 7, 0, FrontAndTop.NORTH_UP);     // top — the entry
        doorAtDown(m, bes, 1, 1, 2, FrontAndTop.SOUTH_UP); // bottom — one-way descending exit (6 lower)
        return new Built(m, bes);
    }
}
