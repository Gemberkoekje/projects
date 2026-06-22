package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.FrontAndTop;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
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
 * The grand <b>Woodland Mansion</b>, assembled by the jigsaw from a two-storey dark-oak <b>core</b> (the start
 * piece — the entrance hall + illager garrison, loot rooms, library, gabled roof) plus up to three single-storey
 * <b>wing</b> pieces drawn from a pool (storeroom / library / checkerboard secret room), attached to the core's
 * west, east and back walls so the manor sprawls a little differently each time. Connectors sit at floor level
 * with pre-carved doorways (the Trade Post / Trial Chamber pattern). The illager garrison — a guaranteed evoker
 * (→ Totem of Undying) + vindicators — spawns in the open ground-floor hall via the theme's {@code animals}
 * pack. (v2 — split from the original single template into a modular core + wings pool for layout variety.
 * Vertical floor-stacking via jigsaw was spiked and works, but the internal staircase makes horizontal wings
 * the cleaner split here.)
 */
public final class WoodlandMansionTemplates {
    private WoodlandMansionTemplates() {}

    private static final BlockState AIR = Blocks.AIR.defaultBlockState();
    private static final BlockState PLANKS = Blocks.DARK_OAK_PLANKS.defaultBlockState();
    private static final BlockState LOG = Blocks.DARK_OAK_LOG.defaultBlockState();
    private static final BlockState COBBLE = Blocks.COBBLESTONE.defaultBlockState();
    private static final BlockState GLASS = Blocks.GLASS_PANE.defaultBlockState();
    private static final BlockState BOOKSHELF = Blocks.BOOKSHELF.defaultBlockState();
    private static final BlockState RED_CARPET = Blocks.RED_CARPET.defaultBlockState();
    private static final BlockState BLUE_CARPET = Blocks.BLUE_CARPET.defaultBlockState();
    private static final BlockState LANTERN = Blocks.LANTERN.defaultBlockState().setValue(BlockStateProperties.HANGING, true);
    private static final String PLANKS_ID = "minecraft:dark_oak_planks";

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("core.nbt"), core());
        writeIfAbsent(dir.resolve("wing_storeroom.nbt"), wing("storeroom"));
        writeIfAbsent(dir.resolve("wing_library.nbt"), wing("library"));
        writeIfAbsent(dir.resolve("wing_checker.nbt"), wing("checker"));
    }

    /** The start piece: the two-storey 13×13 manor, with three ground-floor wall connectors drawing wings. */
    private static Built core() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int x0 = 1, x1 = 11, z0 = 1, z1 = 11; // walls; roof overhangs to 0..12

        for (int x = x0; x <= x1; x++) {
            for (int z = z0; z <= z1; z++) {
                final boolean perim = x == x0 || x == x1 || z == z0 || z == z1;
                m.put(new BlockPos(x, 0, z), perim ? COBBLE : PLANKS);
            }
        }
        walls(m, x0, x1, z0, z1, 1, 4, PLANKS);
        walls(m, x0, x1, z0, z1, 6, 9, PLANKS);
        fillFloor(m, x0, x1, z0, z1, 5, PLANKS);
        fillFloor(m, x0, x1, z0, z1, 10, PLANKS);

        // Frame: corner pillars only now (the three mid-pillars give way to wing doorways).
        for (final int[] p : new int[][]{{1, 1}, {11, 1}, {1, 11}, {11, 11}}) {
            for (int y = 1; y <= 9; y++) {
                m.put(new BlockPos(p[0], y, p[1]), LOG);
            }
        }
        for (final int[] c : new int[][]{{3, 3}, {9, 3}, {3, 9}, {9, 9}}) {
            for (int y = 1; y <= 4; y++) {
                m.put(new BlockPos(c[0], y, c[1]), LOG);
            }
            for (int y = 6; y <= 9; y++) {
                m.put(new BlockPos(c[0], y, c[1]), LOG);
            }
        }

        final int[][] windows = {{3, 1}, {9, 1}, {1, 3}, {1, 9}, {11, 3}, {11, 9}, {3, 11}, {9, 11}};
        for (final int[] w : windows) {
            for (final int baseY : new int[]{2, 7}) {
                m.put(new BlockPos(w[0], baseY, w[1]), GLASS);
                m.put(new BlockPos(w[0], baseY + 1, w[1]), GLASS);
            }
        }
        door(m, 6, 1, 1, Direction.NORTH);

        // Three ground-floor wings. The connector must sit at the template's bounding-box edge (x0/x12/z12,
        // out in the roof-overhang plane) — not flush on the wall — or the wing lands inside the core's box and
        // the jigsaw rejects it for overlap. The doorway is carved through the wall just inside each connector.
        wingConnector(m, bes, 0, 6, FrontAndTop.WEST_UP, 1, 6);
        wingConnector(m, bes, 12, 6, FrontAndTop.EAST_UP, 11, 6);
        wingConnector(m, bes, 6, 12, FrontAndTop.SOUTH_UP, 6, 11);

        for (int t = 2; t <= 10; t++) {
            m.put(new BlockPos(6, 1, t), RED_CARPET);
            m.put(new BlockPos(t, 1, 6), RED_CARPET);
        }
        chest(m, bes, 2, 1, 2, Direction.EAST);

        // Staircase to the upper floor in the SE quarter, at x7-8 (clear of the (9,9) corner pillar, whose upper
        // log used to block the climb's exit). It rises south from z6/y1 to z9/y4.
        for (int s = 0; s < 4; s++) {
            final int sy = 1 + s, sz = 6 + s;
            m.put(new BlockPos(7, sy, sz), stair(Direction.SOUTH));
            m.put(new BlockPos(8, sy, sz), stair(Direction.SOUTH));
        }
        // Open the upper floor over the climb — z7-9 only (leave z6 floored so the upper-hall carpet has support).
        for (int x = 7; x <= 8; x++) {
            for (int z = 7; z <= 9; z++) {
                m.put(new BlockPos(x, 5, z), AIR);
            }
        }

        for (int t = 2; t <= 10; t++) {
            m.put(new BlockPos(t, 6, 6), BLUE_CARPET);
        }
        chest(m, bes, 2, 6, 2, Direction.EAST);
        chest(m, bes, 10, 6, 2, Direction.WEST);
        for (int z = 8; z <= 10; z++) {
            m.put(new BlockPos(10, 6, z), BOOKSHELF);
            m.put(new BlockPos(10, 7, z), BOOKSHELF);
        }

        // Inner-corner lanterns. Skip the GROUND lantern at the stairwell corner (8,8) — it would hang in the
        // open stairwell; the upper one there lights the stairwell from above instead.
        for (final int[] l : new int[][]{{4, 4}, {8, 4}, {4, 8}, {8, 8}}) {
            m.put(new BlockPos(l[0], 9, l[1]), LANTERN);
            if (!(l[0] == 8 && l[1] == 8)) {
                m.put(new BlockPos(l[0], 4, l[1]), LANTERN);
            }
        }

        StructureParts.gableRoof(m, x0, x1, z0, z1, 10, PLANKS, Blocks.DARK_OAK_STAIRS, Blocks.DARK_OAK_SLAB, 1);
        StructureParts.anchor(m, bes, new BlockPos(6, 0, 6), PLANKS_ID);
        return new Built(m, bes);
    }

    /**
     * A floor-level connector at the box edge ({@code connX,connZ}) drawing the wings pool, with a 1×2 doorway
     * carved through the wall behind it ({@code wallX,wallZ}); the connector becomes a plank threshold.
     *
     * <p>The connector must sit a block proud of the wall (out in the roof-overhang plane) or the wing lands
     * inside the core box and the jigsaw rejects it for overlap. That left a one-block open slot beside the
     * doorway between the core wall and the attached wing. To close it, this also walls the box-edge plane across
     * the wing's 5-wide footprint (a jamb), so the wing butts flush against solid instead of a gap — the wing
     * sits one block further out (x-1 / z+13), adjacent to the jamb, so no overlap is introduced.
     */
    private static void wingConnector(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes,
                                      int connX, int connZ, FrontAndTop facing, int wallX, int wallZ) {
        m.put(new BlockPos(wallX, 1, wallZ), AIR);
        m.put(new BlockPos(wallX, 2, wallZ), AIR);
        // Jamb: fill the box-edge plane across the wing's 5-wide footprint (perpendicular to the facing).
        final boolean spanZ = facing == FrontAndTop.WEST_UP || facing == FrontAndTop.EAST_UP;
        for (int o = -2; o <= 2; o++) {
            final int jx = spanZ ? connX : connX + o;
            final int jz = spanZ ? connZ + o : connZ;
            for (int y = 0; y <= 4; y++) {
                m.put(new BlockPos(jx, y, jz), PLANKS);
            }
        }
        // Doorway + connector through the jamb at the connector column.
        m.put(new BlockPos(connX, 1, connZ), AIR);
        m.put(new BlockPos(connX, 2, connZ), AIR);
        m.put(new BlockPos(connX, 0, connZ), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, facing));
        bes.put(new BlockPos(connX, 0, connZ), jig("skyseed:mansion_wall", "skyseed:wing_door", "skyseed:woodland_mansion/wings", PLANKS_ID));
    }

    /** A 5×5 single-storey dark-oak wing: a connector + doorway on the −Z wall (faces the core), a gable roof,
     * a {@code chests/woodland_mansion} chest and decoration by {@code kind}. */
    private static Built wing(String kind) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int max = 4, mid = 2;
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), PLANKS);
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                final boolean corner = (x == 0 || x == max) && (z == 0 || z == max);
                if (perim) {
                    for (int y = 1; y <= 3; y++) {
                        m.put(new BlockPos(x, y, z), corner ? LOG : PLANKS);
                    }
                }
            }
        }
        // Connector + doorway on the −Z wall.
        m.put(new BlockPos(mid, 1, 0), AIR);
        m.put(new BlockPos(mid, 2, 0), AIR);
        m.put(new BlockPos(mid, 0, 0), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.NORTH_UP));
        bes.put(new BlockPos(mid, 0, 0), jig("skyseed:wing_door", "skyseed:mansion_wall", "minecraft:empty", PLANKS_ID));
        // Side windows.
        m.put(new BlockPos(0, 2, mid), GLASS);
        m.put(new BlockPos(max, 2, mid), GLASS);

        switch (kind) {
            case "library" -> {
                for (int x = 1; x <= 3; x++) {
                    m.put(new BlockPos(x, 1, max), BOOKSHELF);
                    m.put(new BlockPos(x, 2, max), BOOKSHELF);
                }
            }
            case "checker" -> {
                for (int x = 1; x <= 3; x++) {
                    for (int z = 1; z <= 3; z++) {
                        m.put(new BlockPos(x, 0, z), (x + z) % 2 == 0 ? Blocks.RED_WOOL.defaultBlockState() : Blocks.BLUE_WOOL.defaultBlockState());
                    }
                }
            }
            default -> { // storeroom
                m.put(new BlockPos(1, 1, 3), Blocks.BARREL.defaultBlockState());
                m.put(new BlockPos(3, 1, 3), Blocks.BARREL.defaultBlockState());
            }
        }
        chest(m, bes, mid, 1, 3, Direction.NORTH);
        m.put(new BlockPos(mid, 3, mid), LANTERN);
        StructureParts.gableRoof(m, 0, max, 0, max, 4, PLANKS, Blocks.DARK_OAK_STAIRS, Blocks.DARK_OAK_SLAB, 0);
        return new Built(m, bes);
    }

    private static void walls(Map<BlockPos, BlockState> m, int x0, int x1, int z0, int z1, int y0, int y1, BlockState s) {
        for (int y = y0; y <= y1; y++) {
            for (int x = x0; x <= x1; x++) {
                m.put(new BlockPos(x, y, z0), s);
                m.put(new BlockPos(x, y, z1), s);
            }
            for (int z = z0; z <= z1; z++) {
                m.put(new BlockPos(x0, y, z), s);
                m.put(new BlockPos(x1, y, z), s);
            }
        }
    }

    private static void fillFloor(Map<BlockPos, BlockState> m, int x0, int x1, int z0, int z1, int y, BlockState s) {
        for (int x = x0; x <= x1; x++) {
            for (int z = z0; z <= z1; z++) {
                m.put(new BlockPos(x, y, z), s);
            }
        }
    }

    private static BlockState stair(Direction facing) {
        return Blocks.DARK_OAK_STAIRS.defaultBlockState().setValue(BlockStateProperties.HORIZONTAL_FACING, facing);
    }

    private static void door(Map<BlockPos, BlockState> m, int x, int y, int z, Direction facing) {
        final BlockState base = Blocks.DARK_OAK_DOOR.defaultBlockState()
                .setValue(BlockStateProperties.HORIZONTAL_FACING, facing)
                .setValue(BlockStateProperties.DOOR_HINGE, DoorHingeSide.LEFT);
        m.put(new BlockPos(x, y, z), base.setValue(BlockStateProperties.DOUBLE_BLOCK_HALF, DoubleBlockHalf.LOWER));
        m.put(new BlockPos(x, y + 1, z), base.setValue(BlockStateProperties.DOUBLE_BLOCK_HALF, DoubleBlockHalf.UPPER));
    }

    private static void chest(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int y, int z, Direction facing) {
        m.put(new BlockPos(x, y, z), Blocks.CHEST.defaultBlockState().setValue(BlockStateProperties.HORIZONTAL_FACING, facing));
        bes.put(new BlockPos(x, y, z), StructureParts.lootChest("minecraft:chests/woodland_mansion"));
    }
}
