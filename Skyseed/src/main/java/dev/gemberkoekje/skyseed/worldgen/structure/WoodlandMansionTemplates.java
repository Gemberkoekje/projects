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
import net.minecraft.world.level.block.state.properties.Half;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The grand <b>Woodland Mansion</b>, assembled by the jigsaw from a two-storey dark-oak <b>core</b> (the start
 * piece — the carpeted entrance hall + the illager garrison) plus up to three single-storey <b>wing</b> rooms drawn
 * from a pool (library / prison / storeroom), one off each of the core's west, east and back walls.
 *
 * <p>Styled from the real vanilla mansion's block data (its {@code data/minecraft/structure/woodland_mansion}
 * templates): <b>birch-plank floors</b> with red/white carpet runners, <b>dark-oak-plank walls</b> on a
 * <b>cobblestone base course</b> with a <b>cobblestone cornice</b> (a wall band + an inward stair eave) under the
 * roofline, <b>dark-oak-log</b> corner posts and window frames, and <b>glass-pane windows with dark-oak-fence bars</b>.
 *
 * <p><b>Flush connections (the overlap fix).</b> The walls sit AT the template's bounding-box edges and the roof does
 * not overhang, so each box edge IS the wall. A wing connector is a floor-level jigsaw block ON that wall edge, with a
 * doorway carved through the wall just above it and <em>nothing behind it</em>; the wing's matching side is left OPEN
 * (no wall) so it butts straight onto the core's wall — one shared wall, no jamb, no stray block on the floor. The
 * illager garrison (a guaranteed evoker → Totem of Undying + vindicators) spawns in the open hall via the theme's
 * {@code animals} pack.
 */
public final class WoodlandMansionTemplates {
    private WoodlandMansionTemplates() {}

    private static final BlockState AIR = Blocks.AIR.defaultBlockState();
    private static final BlockState PLANKS = Blocks.DARK_OAK_PLANKS.defaultBlockState();
    private static final BlockState BIRCH = Blocks.BIRCH_PLANKS.defaultBlockState();
    private static final BlockState LOG = Blocks.DARK_OAK_LOG.defaultBlockState();
    private static final BlockState COBBLE = Blocks.COBBLESTONE.defaultBlockState();
    private static final BlockState COBBLE_WALL = Blocks.COBBLESTONE_WALL.defaultBlockState();
    private static final BlockState GLASS = Blocks.GLASS_PANE.defaultBlockState();
    private static final BlockState BOOKSHELF = Blocks.BOOKSHELF.defaultBlockState();
    private static final BlockState RED_CARPET = Blocks.RED_CARPET.defaultBlockState();
    private static final BlockState WHITE_CARPET = Blocks.WHITE_CARPET.defaultBlockState();
    private static final BlockState LANTERN = Blocks.LANTERN.defaultBlockState().setValue(BlockStateProperties.HANGING, true);
    private static final String BIRCH_ID = "minecraft:birch_planks";

    private static final int W = 10;      // core footprint: walls at x,z = 0..W (the box edges — no overhang)
    private static final int STOREY = 4;  // wall height per floor (floors at y = 0 and y = STOREY+1)

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("core.nbt"), core());
        writeIfAbsent(dir.resolve("wing_library.nbt"), wing("library"));
        writeIfAbsent(dir.resolve("wing_prison.nbt"), wing("prison"));
        writeIfAbsent(dir.resolve("wing_storeroom.nbt"), wing("storeroom"));
    }

    /** The start piece: the two-storey 11×11 manor, with a floor-level wing connector on each of three walls. */
    private static Built core() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int top = STOREY * 2 + 2;   // roof slab height (two storeys + the mid floor)

        birchFloor(m, 0, W, 0, W, 0);
        birchFloor(m, 0, W, 0, W, STOREY + 1);
        carpetRunner(m, 0);
        carpetRunner(m, STOREY + 1);

        vanillaWalls(m, 1, true);              // ground floor: windows + the cornice band
        vanillaWalls(m, STOREY + 2, true);     // upper floor

        // The entrance: a dark-oak door on the front (−Z) wall with a torch either side.
        door(m, 5, 1, 0, Direction.NORTH);
        m.put(new BlockPos(4, 3, 0), Blocks.WALL_TORCH.defaultBlockState().setValue(BlockStateProperties.HORIZONTAL_FACING, Direction.NORTH));
        m.put(new BlockPos(6, 3, 0), Blocks.WALL_TORCH.defaultBlockState().setValue(BlockStateProperties.HORIZONTAL_FACING, Direction.NORTH));

        // Three flush wing doorways — one per wall, on the box edge (see class doc).
        wingConnector(m, bes, 0, 5, FrontAndTop.WEST_UP);
        wingConnector(m, bes, W, 5, FrontAndTop.EAST_UP);
        wingConnector(m, bes, 5, W, FrontAndTop.SOUTH_UP);

        // Ground-floor garrison hall: open, lantern-lit. A loot chest in a corner.
        chest(m, bes, 1, 1, 1, Direction.EAST);
        // Staircase up the SE corner (rises south, x = 7..8, z = 6..9 → y = 1..4) and an opening above it.
        for (int s = 0; s < STOREY; s++) {
            m.put(new BlockPos(7, 1 + s, 6 + s), darkStair(Direction.SOUTH, Half.BOTTOM));
            m.put(new BlockPos(8, 1 + s, 6 + s), darkStair(Direction.SOUTH, Half.BOTTOM));
        }
        for (int x = 7; x <= 8; x++) {
            for (int z = 7; z <= 9; z++) {
                m.put(new BlockPos(x, STOREY + 1, z), AIR);
            }
        }

        // Upper floor: a small library + two loot chests.
        chest(m, bes, 1, STOREY + 2, 1, Direction.EAST);
        chest(m, bes, W - 1, STOREY + 2, 1, Direction.WEST);
        for (int z = 8; z <= 9; z++) {
            m.put(new BlockPos(W - 1, STOREY + 2, z), BOOKSHELF);
            m.put(new BlockPos(W - 1, STOREY + 3, z), BOOKSHELF);
        }

        for (final int[] l : new int[][]{{2, 2}, {8, 2}, {2, 8}}) {  // corner lanterns (skip the stairwell corner)
            m.put(new BlockPos(l[0], STOREY, l[1]), LANTERN);
            m.put(new BlockPos(l[0], top - 1, l[1]), LANTERN);
        }

        StructureParts.gableRoof(m, 0, W, 0, W, top, PLANKS, Blocks.DARK_OAK_STAIRS, Blocks.DARK_OAK_SLAB, 0);
        StructureParts.anchor(m, bes, new BlockPos(5, 0, 5), BIRCH_ID);
        return new Built(m, bes);
    }

    /**
     * A floor-level connector ON the box-edge wall at {@code (connX,connZ)}, facing out, with a doorway carved through
     * the wall above it and nothing behind it. The wing (open on its matching side) butts flush onto this wall.
     */
    private static void wingConnector(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes,
                                      int connX, int connZ, FrontAndTop facing) {
        m.put(new BlockPos(connX, 1, connZ), AIR);   // carve the 1×2 doorway through the wall
        m.put(new BlockPos(connX, 2, connZ), AIR);
        m.put(new BlockPos(connX, 0, connZ), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, facing));
        bes.put(new BlockPos(connX, 0, connZ), jig("skyseed:mansion_wall", "skyseed:wing_door", "skyseed:woodland_mansion/wings", BIRCH_ID));
    }

    /** A 7×7 single-storey wing room, OPEN on its −Z side (it butts flush onto the core's wall), styled + themed. */
    private static Built wing(String kind) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int mx = 6, mid = 3;       // 7×7 footprint (0..6); −Z (z=0) is the open connecting side

        birchFloor(m, 0, mx, 0, mx, 0);
        for (int y = 1; y <= STOREY; y++) {
            for (int x = 0; x <= mx; x++) {
                for (int z = 1; z <= mx; z++) {                 // z starts at 1 — z=0 is OPEN (abuts the core wall)
                    if (x != 0 && x != mx && z != mx) {
                        continue;                                // interior + the open −Z side stay clear
                    }
                    final boolean corner = (x == 0 || x == mx) && z == mx;
                    final BlockState s = corner ? LOG : (y == 1 ? COBBLE : PLANKS);  // cobblestone base course
                    m.put(new BlockPos(x, y, z), s);
                }
            }
        }
        wallCornice(m, 0, mx, 1, mx, STOREY);                   // cobblestone cornice on the three closed walls
        // A connector + doorway opening on the −Z (open) side so the wing reads as a doorway into the core.
        m.put(new BlockPos(mid, 0, 0), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.NORTH_UP));
        bes.put(new BlockPos(mid, 0, 0), jig("skyseed:wing_door", "skyseed:mansion_wall", "minecraft:empty", BIRCH_ID));
        // Glass-pane windows with fence bars on the two side walls.
        window(m, 0, 2, mid, true);
        window(m, mx, 2, mid, true);

        switch (kind) {
            case "library" -> {
                for (int x = 1; x <= mx - 1; x++) {
                    m.put(new BlockPos(x, 1, mx - 1), BOOKSHELF);
                    m.put(new BlockPos(x, 2, mx - 1), BOOKSHELF);
                }
                m.put(new BlockPos(mid, 1, mid), Blocks.LECTERN.defaultBlockState());
            }
            case "prison" -> {                                  // vanilla's 2×2 cells: iron bars + cauldrons
                for (int z = 2; z <= 4; z++) {
                    m.put(new BlockPos(mid, 1, z), Blocks.IRON_BARS.defaultBlockState());
                    m.put(new BlockPos(mid, 2, z), Blocks.IRON_BARS.defaultBlockState());
                }
                m.put(new BlockPos(1, 1, mx - 1), Blocks.CAULDRON.defaultBlockState());
                m.put(new BlockPos(mx - 1, 1, mx - 1), Blocks.CAULDRON.defaultBlockState());
            }
            default -> {                                        // storeroom
                m.put(new BlockPos(1, 1, mx - 1), Blocks.BARREL.defaultBlockState());
                m.put(new BlockPos(mx - 1, 1, mx - 1), Blocks.BARREL.defaultBlockState());
                m.put(new BlockPos(1, 2, mx - 1), Blocks.BARREL.defaultBlockState());
            }
        }
        chest(m, bes, mid, 1, mx - 1, Direction.NORTH);
        m.put(new BlockPos(mid, STOREY, mid), LANTERN);
        StructureParts.gableRoof(m, 0, mx, 0, mx, STOREY + 1, PLANKS, Blocks.DARK_OAK_STAIRS, Blocks.DARK_OAK_SLAB, 0);
        return new Built(m, bes);
    }

    /** A storey of the core's 11×11 wall ring at base {@code y0}: cobblestone base course, dark-oak field + log posts,
     *  glass-pane windows mid-wall, and a cobblestone cornice band on top. */
    private static void vanillaWalls(Map<BlockPos, BlockState> m, int y0, boolean windows) {
        for (int y = y0; y < y0 + STOREY; y++) {
            for (int x = 0; x <= W; x++) {
                final boolean edgeX = x == 0 || x == W;
                place(m, x, y, 0, wallBlock(x, y - y0, true));
                place(m, x, y, W, wallBlock(x, y - y0, true));
                if (edgeX) {
                    for (int z = 1; z < W; z++) {
                        place(m, x, y, z, wallBlock(z, y - y0, false));
                    }
                }
            }
        }
        // Log corner posts.
        for (final int[] c : new int[][]{{0, 0}, {W, 0}, {0, W}, {W, W}}) {
            for (int y = y0; y < y0 + STOREY; y++) {
                m.put(new BlockPos(c[0], y, c[1]), LOG);
            }
        }
        if (windows) {
            for (final int[] w : new int[][]{{3, 0}, {7, 0}, {3, W}, {7, W}, {0, 3}, {0, 7}, {W, 3}, {W, 7}}) {
                window(m, w[0], y0 + 1, w[1], w[0] == 0 || w[0] == W);
            }
        }
        wallCornice(m, 0, W, 0, W, y0 + STOREY - 1);
    }

    /** Field block for a wall column: cobblestone on the base course (relative y 0), dark-oak planks above. */
    private static BlockState wallBlock(int along, int relY, boolean perimeter) {
        return relY == 0 ? COBBLE : PLANKS;
    }

    /** A cobblestone-wall + inward dark-oak-stair cornice band around a wall ring at height {@code y}. */
    private static void wallCornice(Map<BlockPos, BlockState> m, int x0, int x1, int z0, int z1, int y) {
        for (int x = x0; x <= x1; x++) {
            m.put(new BlockPos(x, y, z0), COBBLE_WALL);
            m.put(new BlockPos(x, y, z1), COBBLE_WALL);
        }
        for (int z = z0; z <= z1; z++) {
            m.put(new BlockPos(x0, y, z), COBBLE_WALL);
            m.put(new BlockPos(x1, y, z), COBBLE_WALL);
        }
    }

    /** A 1×2 glass-pane window with a dark-oak-fence bar below it, in the wall at {@code (x,baseY,z)}. */
    private static void window(Map<BlockPos, BlockState> m, int x, int baseY, int z, boolean onXWall) {
        m.put(new BlockPos(x, baseY, z), Blocks.DARK_OAK_FENCE.defaultBlockState());
        m.put(new BlockPos(x, baseY + 1, z), GLASS);
    }

    private static void birchFloor(Map<BlockPos, BlockState> m, int x0, int x1, int z0, int z1, int y) {
        for (int x = x0; x <= x1; x++) {
            for (int z = z0; z <= z1; z++) {
                m.put(new BlockPos(x, y, z), BIRCH);
            }
        }
    }

    /** The vanilla hall runner: a red-carpet cross down the middle with a white-carpet border. */
    private static void carpetRunner(Map<BlockPos, BlockState> m, int y) {
        for (int t = 1; t < W; t++) {
            m.put(new BlockPos(5, y + 1, t), RED_CARPET);
            m.put(new BlockPos(t, y + 1, 5), RED_CARPET);
            m.put(new BlockPos(4, y + 1, t), WHITE_CARPET);
            m.put(new BlockPos(6, y + 1, t), WHITE_CARPET);
            m.put(new BlockPos(t, y + 1, 4), WHITE_CARPET);
            m.put(new BlockPos(t, y + 1, 6), WHITE_CARPET);
        }
    }

    private static void place(Map<BlockPos, BlockState> m, int x, int y, int z, BlockState s) {
        m.put(new BlockPos(x, y, z), s);
    }

    private static BlockState darkStair(Direction facing, Half half) {
        return Blocks.DARK_OAK_STAIRS.defaultBlockState()
                .setValue(BlockStateProperties.HORIZONTAL_FACING, facing)
                .setValue(BlockStateProperties.HALF, half);
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
