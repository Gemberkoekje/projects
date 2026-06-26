package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;
import net.minecraft.world.level.block.state.properties.Half;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The <b>End Portal temple</b> (SKYENDPLAN Phase 1) — what the End Portal Seed grows. Deliberately <i>not</i> a box: a
 * stepped blackstone plinth carries an open polished-diorite peristyle (quartz columns, four arched dark-oak-lintelled
 * entrances) around the vanilla <b>12-frame End portal ring</b> on a gilded dais; four blackstone corner spires with
 * end-rod tips frame a glowing quartz <b>cupola</b> that funnels light straight down onto the portal. Gilded-blackstone
 * bands and dark-oak beams are the accents. Fill the twelve frames with Eyes of Ender to light it.
 */
public final class PortalChamberTemplates {
    private PortalChamberTemplates() {}

    private static final BlockState AIR = Blocks.AIR.defaultBlockState();
    private static final BlockState DIORITE = Blocks.POLISHED_DIORITE.defaultBlockState();
    private static final BlockState SMOOTH = Blocks.SMOOTH_QUARTZ.defaultBlockState();
    private static final BlockState CHISELED = Blocks.CHISELED_QUARTZ_BLOCK.defaultBlockState();
    private static final BlockState PILLAR = Blocks.QUARTZ_PILLAR.defaultBlockState();
    private static final BlockState BLACK = Blocks.BLACKSTONE.defaultBlockState();
    private static final BlockState BLACK_BRICK = Blocks.POLISHED_BLACKSTONE_BRICKS.defaultBlockState();
    private static final BlockState POL_BLACK = Blocks.POLISHED_BLACKSTONE.defaultBlockState();
    private static final BlockState GILDED = Blocks.GILDED_BLACKSTONE.defaultBlockState();
    private static final BlockState GLASS = Blocks.GLASS_PANE.defaultBlockState();
    private static final BlockState ROD = Blocks.END_ROD.defaultBlockState();
    private static final BlockState LOG_Y = Blocks.DARK_OAK_LOG.defaultBlockState();
    private static final BlockState LOG_X = Blocks.DARK_OAK_LOG.defaultBlockState().setValue(BlockStateProperties.AXIS, Direction.Axis.X);
    private static final BlockState LOG_Z = Blocks.DARK_OAK_LOG.defaultBlockState().setValue(BlockStateProperties.AXIS, Direction.Axis.Z);

    private static final int MAX = 12;   // 13×13 footprint (0..12)
    private static final int MID = 6;

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("chamber.nbt"), chamber());
    }

    private static Built chamber() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();

        foundationAndFloor(m);
        portalRing(m);
        for (final int[] c : new int[][]{{0, 0}, {MAX - 2, 0}, {0, MAX - 2}, {MAX - 2, MAX - 2}}) {
            spire(m, c[0], c[1]);
        }
        colonnade(m, true, 1);         // north (on the floor edge, inset 1 from the plinth)
        colonnade(m, true, MAX - 1);   // south
        colonnade(m, false, 1);        // west
        colonnade(m, false, MAX - 1);  // east
        entablatureAndRoof(m);
        cupola(m);
        lighting(m);

        StructureParts.anchor(m, bes, new BlockPos(MID, 0, MID), "minecraft:polished_blackstone");
        return new Built(m, bes);
    }

    /** A blackstone plinth (the floor inset one block reads as a step), a polished-diorite floor, a gilded portal dais. */
    private static void foundationAndFloor(Map<BlockPos, BlockState> m) {
        box(m, 0, MAX, 0, 0, 0, MAX, POL_BLACK);                 // plinth top course
        for (final int[] c : new int[][]{{0, 0}, {MAX, 0}, {0, MAX}, {MAX, MAX}}) {
            m.put(new BlockPos(c[0], 0, c[1]), GILDED);          // gilded plinth corners
        }
        box(m, 1, MAX - 1, 1, 1, 1, MAX - 1, DIORITE);          // floor (inset 1 → the plinth reads as a step)
        rectPerim(m, 1, MAX - 1, 1, MAX - 1, 1, BLACK_BRICK);   // floor border
        rectPerim(m, 3, 9, 3, 9, 1, POL_BLACK);                 // raised-look dais ring around the portal
        for (final int[] c : new int[][]{{3, 3}, {9, 3}, {3, 9}, {9, 9}}) {
            m.put(new BlockPos(c[0], 1, c[1]), GILDED);          // gilded dais corners
        }
    }

    /** The vanilla 12-frame End portal ring (5×5 at 4..8): frames face inward, the centre 3×3 cleared to a void. */
    private static void portalRing(Map<BlockPos, BlockState> m) {
        for (int x = 5; x <= 7; x++) {
            frame(m, x, 4, Direction.SOUTH);
            frame(m, x, 8, Direction.NORTH);
        }
        for (int z = 5; z <= 7; z++) {
            frame(m, 4, z, Direction.EAST);
            frame(m, 8, z, Direction.WEST);
        }
        box(m, 5, 7, 1, 1, 5, 7, AIR);
    }

    /** A 3×3 blackstone corner spire: quartz-edged shaft, gilded bands, outward windows, a tapered end-rod tip. */
    private static void spire(Map<BlockPos, BlockState> m, int x0, int z0) {
        final int x1 = x0 + 2, z1 = z0 + 2, cx = x0 + 1, cz = z0 + 1;
        final int outX = x0 == 0 ? x0 : x1, outZ = z0 == 0 ? z0 : z1; // the two faces pointing away from the building
        for (int y = 1; y <= 9; y++) {
            for (int x = x0; x <= x1; x++) {
                for (int z = z0; z <= z1; z++) {
                    final boolean corner = (x == x0 || x == x1) && (z == z0 || z == z1);
                    final boolean core = x == cx && z == cz;
                    BlockState s = core ? BLACK : corner ? PILLAR : BLACK_BRICK;
                    if ((y == 4 || y == 7) && !corner && !core) {
                        s = GILDED;                                 // glowing gilded bands
                    }
                    if ((y == 5 || y == 6) && ((x == cx && z == outZ) || (z == cz && x == outX))) {
                        s = GLASS;                                  // a window slit on each outward face
                    }
                    m.put(new BlockPos(x, y, z), s);
                }
            }
        }
        box(m, x0, x1, 10, 10, z0, z1, SMOOTH);                      // cap base
        for (final Direction d : Direction.Plane.HORIZONTAL) {       // stairs slope up toward the centre peak (face inward)
            m.put(new BlockPos(cx + d.getStepX(), 11, cz + d.getStepZ()), qStair(d.getOpposite(), false));
        }
        m.put(new BlockPos(cx, 11, cz), CHISELED);
        m.put(new BlockPos(cx, 12, cz), GILDED);
        m.put(new BlockPos(cx, 13, cz), ROD);                       // spire tip + light
    }

    /**
     * One open colonnade side between the corner spires: a pier (with a tall window) and a quartz column on each side
     * of a 3-wide arched entrance with a dark-oak-and-gilded lintel. {@code horiz} varies X along the {@code edge} Z
     * wall; otherwise varies Z along the {@code edge} X wall.
     */
    private static void colonnade(Map<BlockPos, BlockState> m, boolean horiz, int edge) {
        final BlockState lintelLog = horiz ? LOG_X : LOG_Z;
        for (int a = 3; a <= 9; a++) {
            final boolean entrance = a >= 5 && a <= 7;
            final boolean column = a == 4 || a == 8;
            for (int y = 2; y <= 5; y++) {
                BlockState s;
                if (entrance) {
                    s = y < 5 ? AIR : (a == MID ? GILDED : lintelLog); // open doorway, lintel beam + gilded keystone
                } else if (column) {
                    s = PILLAR;
                } else {
                    s = (y == 3 || y == 4) ? GLASS : DIORITE;          // pier with a lancet window
                }
                put(m, horiz, edge, a, y, s);
            }
        }
    }

    /** Dark-oak frieze + a stepped quartz hip roof from the eave up to the cupola, leaving the corners to the spires. */
    private static void entablatureAndRoof(Map<BlockPos, BlockState> m) {
        for (int a = 3; a <= 9; a++) {                               // dark-oak architrave over each colonnade
            put(m, true, 1, a, 6, LOG_X);
            put(m, true, MAX - 1, a, 6, LOG_X);
            put(m, false, 1, a, 6, LOG_Z);
            put(m, false, MAX - 1, a, 6, LOG_Z);
        }
        hipRing(m, 1, MAX - 1, 1, MAX - 1, 7);                       // eave (skips spire corners)
        hipRing(m, 2, MAX - 2, 2, MAX - 2, 8);                       // second tier, up to the cupola drum
    }

    /** A glowing quartz cupola over the portal: a windowed dark-oak-posted drum, a stepped open cap, a gilded finial. */
    private static void cupola(Map<BlockPos, BlockState> m) {
        for (final int[] c : new int[][]{{3, 3}, {9, 3}, {3, 9}, {9, 9}}) {
            box(m, c[0], c[0], 2, 5, c[1], c[1], LOG_Y);             // dark-oak canopy posts: dais → drum, framing the portal
        }
        for (int y = 6; y <= 9; y++) {                               // 7×7 drum at 3..9, interior open over the portal
            for (int x = 3; x <= 9; x++) {
                for (int z = 3; z <= 9; z++) {
                    if (x != 3 && x != 9 && z != 3 && z != 9) {
                        continue;
                    }
                    final boolean corner = (x == 3 || x == 9) && (z == 3 || z == 9);
                    final boolean midFace = x == MID || z == MID;
                    m.put(new BlockPos(x, y, z), corner ? LOG_Y : midFace ? GLASS : CHISELED);
                }
            }
        }
        hipRing(m, 3, 9, 3, 9, 10);                                  // drum cornice
        hipRing(m, 4, 8, 4, 8, 11);                                  // stepped open cap (light still falls through)
        hipRing(m, 5, 7, 5, 7, 12);
        m.put(new BlockPos(MID, 13, MID), GILDED);                   // apex finial
        m.put(new BlockPos(MID, 14, MID), ROD);
    }

    /** End rods at the four corners of the portal ring, flanking it so it reads at night. (Spire tips are already lit.) */
    private static void lighting(Map<BlockPos, BlockState> m) {
        for (final int[] c : new int[][]{{4, 4}, {8, 4}, {4, 8}, {8, 8}}) {
            m.put(new BlockPos(c[0], 2, c[1]), ROD);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────

    private static void frame(Map<BlockPos, BlockState> m, int x, int z, Direction facing) {
        m.put(new BlockPos(x, 1, z), Blocks.END_PORTAL_FRAME.defaultBlockState()
                .setValue(BlockStateProperties.HORIZONTAL_FACING, facing)
                .setValue(BlockStateProperties.EYE, false));
    }

    private static void box(Map<BlockPos, BlockState> m, int x0, int x1, int y0, int y1, int z0, int z1, BlockState s) {
        for (int x = x0; x <= x1; x++) {
            for (int y = y0; y <= y1; y++) {
                for (int z = z0; z <= z1; z++) {
                    m.put(new BlockPos(x, y, z), s);
                }
            }
        }
    }

    private static void rectPerim(Map<BlockPos, BlockState> m, int x0, int x1, int z0, int z1, int y, BlockState s) {
        for (int x = x0; x <= x1; x++) {
            m.put(new BlockPos(x, y, z0), s);
            m.put(new BlockPos(x, y, z1), s);
        }
        for (int z = z0; z <= z1; z++) {
            m.put(new BlockPos(x0, y, z), s);
            m.put(new BlockPos(x1, y, z), s);
        }
    }

    private static void put(Map<BlockPos, BlockState> m, boolean horiz, int edge, int along, int y, BlockState s) {
        m.put(horiz ? new BlockPos(along, y, edge) : new BlockPos(edge, y, along), s);
    }

    private static BlockState qStair(Direction facing, boolean top) {
        return Blocks.QUARTZ_STAIRS.defaultBlockState()
                .setValue(BlockStateProperties.HORIZONTAL_FACING, facing)
                .setValue(BlockStateProperties.HALF, top ? Half.TOP : Half.BOTTOM);
    }

    /** A roof tier: outward-sloping quartz stairs on the four straight runs, solid quartz at the hip corners; any
     *  position inside a corner spire's 3×3 is left alone so the spires rise cleanly through the roofline. */
    private static void hipRing(Map<BlockPos, BlockState> m, int x0, int x1, int z0, int z1, int y) {
        for (int x = x0; x <= x1; x++) {   // stairs face INWARD toward the ridge, so each eave tapers thin to the outside
            tile(m, x, y, z0, qStair(Direction.SOUTH, false));
            tile(m, x, y, z1, qStair(Direction.NORTH, false));
        }
        for (int z = z0; z <= z1; z++) {
            tile(m, x0, y, z, qStair(Direction.EAST, false));
            tile(m, x1, y, z, qStair(Direction.WEST, false));
        }
        for (final int[] c : new int[][]{{x0, z0}, {x1, z0}, {x0, z1}, {x1, z1}}) {
            tile(m, c[0], y, c[1], SMOOTH);
        }
    }

    private static void tile(Map<BlockPos, BlockState> m, int x, int y, int z, BlockState s) {
        final boolean inSpire = (x <= 2 || x >= MAX - 2) && (z <= 2 || z >= MAX - 2);
        if (!inSpire) {
            m.put(new BlockPos(x, y, z), s);
        }
    }
}
