package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;
import net.minecraft.world.level.block.state.properties.DoorHingeSide;
import net.minecraft.world.level.block.state.properties.DoubleBlockHalf;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The grand <b>Woodland Mansion</b> — the second grand structure from SKYGRANDSTRUCTURESPLAN. A two-storey
 * dark-oak manor (13×13 footprint, a tall gable roof) sitting on a larger grassy "grand island". The illager
 * garrison — a <b>guaranteed evoker</b> (→ Totem of Undying) and a pack of vindicators — spawns in the
 * ground-floor hall via the theme's {@code animals} pack (the proven Evoker-Cell pattern, jittered around the
 * island centre). Loot is on the vanilla {@code chests/woodland_mansion} table. v1 is a single rotated
 * template; splitting it into a modular jigsaw pool with per-room marker spawning is a flagged follow-up.
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

    private record Built(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> blockEntities) {}

    public static void generateInto(Path dir) throws IOException {
        final Path file = dir.resolve("mansion.nbt");
        if (!Files.exists(file)) {
            final Built b = mansion();
            StructureWriter.write(b.blocks(), b.blockEntities(), file);
            Skyseed.LOGGER.info("[skyseed] generated structure template {}", file.getFileName());
        }
    }

    private static Built mansion() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        // Walls span x/z 1..11 (interior 2..10); the roof overhangs to 0..12 (the 13×13 .nbt).
        final int x0 = 1, x1 = 11, z0 = 1, z1 = 11;

        // Ground floor: cobblestone foundation ring + dark-oak planks inside.
        for (int x = x0; x <= x1; x++) {
            for (int z = z0; z <= z1; z++) {
                final boolean perim = x == x0 || x == x1 || z == z0 || z == z1;
                m.put(new BlockPos(x, 0, z), perim ? COBBLE : PLANKS);
            }
        }
        // Exterior walls (both storeys), the mid-floor slab, and the top ceiling.
        walls(m, x0, x1, z0, z1, 1, 4, PLANKS);   // ground-floor walls
        walls(m, x0, x1, z0, z1, 6, 9, PLANKS);   // upper-floor walls
        fillFloor(m, x0, x1, z0, z1, 5, PLANKS);  // upper-floor slab
        fillFloor(m, x0, x1, z0, z1, 10, PLANKS); // ceiling under the roof

        // Dark-oak-log frame: corner + mid-wall pillars, full height (no front-mid: the door sits there).
        for (final int[] p : new int[][]{{1, 1}, {11, 1}, {1, 11}, {11, 11}, {1, 6}, {11, 6}, {6, 11}}) {
            for (int y = 1; y <= 9; y++) {
                m.put(new BlockPos(p[0], y, p[1]), LOG);
            }
        }
        // Four free-standing interior columns per storey for grandeur (clear of the central spawn hall).
        for (final int[] c : new int[][]{{3, 3}, {9, 3}, {3, 9}, {9, 9}}) {
            for (int y = 1; y <= 4; y++) {
                m.put(new BlockPos(c[0], y, c[1]), LOG);
            }
            for (int y = 6; y <= 9; y++) {
                m.put(new BlockPos(c[0], y, c[1]), LOG);
            }
        }

        // Windows — glass panes punched into both storeys (front wall keeps the door at x6).
        final int[][] front = {{3, 1}, {9, 1}, {1, 3}, {1, 9}, {11, 3}, {11, 9}, {3, 11}, {9, 11}};
        for (final int[] w : front) {
            for (final int baseY : new int[]{2, 7}) {
                m.put(new BlockPos(w[0], baseY, w[1]), GLASS);
                m.put(new BlockPos(w[0], baseY + 1, w[1]), GLASS);
            }
        }
        // Grand front door, centred in the front (−Z) wall.
        door(m, 6, 1, 1, Direction.NORTH);

        // Ground floor: a red-carpet runner (the illager pack lands on the open centre here).
        for (int t = 2; t <= 10; t++) {
            m.put(new BlockPos(6, 1, t), RED_CARPET);
            m.put(new BlockPos(t, 1, 6), RED_CARPET);
        }
        chest(m, bes, 2, 1, 2, Direction.EAST);

        // Staircase to the upper floor: a two-wide run rising in +Z in the back-right, with the slab cut away
        // above it for headroom and a landing at the top.
        for (int s = 0; s < 4; s++) {
            final int sy = 1 + s, sz = 6 + s;
            m.put(new BlockPos(8, sy, sz), stair(Direction.SOUTH));
            m.put(new BlockPos(9, sy, sz), stair(Direction.SOUTH));
        }
        for (int x = 8; x <= 9; x++) {
            for (int z = 6; z <= 9; z++) {
                m.put(new BlockPos(x, 5, z), AIR); // open the stairwell through the upper slab
            }
        }

        // Upper floor: a blue-carpet runner, two loot chests, and a little library.
        for (int t = 2; t <= 10; t++) {
            m.put(new BlockPos(t, 6, 6), BLUE_CARPET);
        }
        chest(m, bes, 2, 6, 2, Direction.EAST);
        chest(m, bes, 10, 6, 2, Direction.WEST);
        for (int z = 8; z <= 10; z++) {
            m.put(new BlockPos(10, 6, z), BOOKSHELF);
            m.put(new BlockPos(10, 7, z), BOOKSHELF);
        }

        // Hanging lanterns keep both halls lit (clear of the corner columns).
        for (final int[] l : new int[][]{{4, 4}, {8, 4}, {4, 8}, {8, 8}}) {
            m.put(new BlockPos(l[0], 4, l[1]), LANTERN);
            m.put(new BlockPos(l[0], 9, l[1]), LANTERN);
        }

        // Tall dark-oak gable roof with a one-block overhang.
        StructureParts.gableRoof(m, x0, x1, z0, z1, 10, PLANKS, Blocks.DARK_OAK_STAIRS, Blocks.DARK_OAK_SLAB, 1);

        // Anchor at the ground-floor centre (kept as open hall) — placed last so nothing overwrites the jigsaw.
        StructureParts.anchor(m, bes, new BlockPos(6, 0, 6), "minecraft:dark_oak_planks");
        return new Built(m, bes);
    }

    /** Perimeter walls (the four sides) of {@code [x0..x1]×[z0..z1]} from {@code y0} to {@code y1}. */
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
