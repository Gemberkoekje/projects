package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.FrontAndTop;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.JigsawBlock;
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
 * The Pillager Outpost — a wide cobblestone-and-dark-oak watchtower (the vanilla outpost translated to a floating
 * island) ringed by a small camp. A semi-open arched base holds the iron-golem cage at its centre (the golem
 * spawns at the jigsaw origin via the theme's {@code iron_golems}, so it lands inside) with room to walk past it
 * to the corner ladder; the middle floor is an <b>enclosed</b> room with the pillager spawner + a
 * {@code chests/pillager_outpost} chest, so spawned pillagers can't fall off the island; the top is an open watch
 * platform under a pitched roof. The camp apron carries tents, a target, a campfire and a banner. See
 * {@code SKYSTRUCTURESPLAN.md}.
 */
public final class OutpostTemplates {
    private OutpostTemplates() {}

    private static final BlockState LOG = Blocks.DARK_OAK_LOG.defaultBlockState();
    private static final BlockState PLANK = Blocks.DARK_OAK_PLANKS.defaultBlockState();
    private static final BlockState FENCE = Blocks.DARK_OAK_FENCE.defaultBlockState();
    private static final BlockState COBBLE = Blocks.COBBLESTONE.defaultBlockState();
    private static final BlockState MOSSY = Blocks.MOSSY_COBBLESTONE.defaultBlockState();
    private static final BlockState GLASS = Blocks.GLASS_PANE.defaultBlockState();
    private static final BlockState AIR = Blocks.AIR.defaultBlockState();

    private record Built(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> blockEntities) {}

    public static void generateInto(Path dir) throws IOException {
        final Path file = dir.resolve("tower.nbt");
        if (!Files.exists(file)) {
            final Built b = tower();
            StructureWriter.write(b.blocks(), b.blockEntities(), file);
            Skyseed.LOGGER.info("[skyseed] generated structure template {}", file.getFileName());
        }
    }

    /** A deterministic cobble/mossy mix for the weathered base. */
    private static BlockState stone(int a, int b) {
        return Math.floorMod(a * 5 + b * 3, 4) == 0 ? MOSSY : COBBLE;
    }

    private static Built tower() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int lo = 3, hi = 9, mid = 6; // 7×7 tower walls at x/z 3..9, centre (6,6); .nbt apron is 0..12

        // Cobble floor under the tower.
        for (int x = lo; x <= hi; x++) {
            for (int z = lo; z <= hi; z++) {
                m.put(new BlockPos(x, 0, z), stone(x, z));
            }
        }

        // --- Base (y1-3): weathered cobble walls + dark-oak corner posts, big arch openings, a front door. ---
        for (int y = 1; y <= 4; y++) {
            ring(m, lo, hi, y, (x, z) -> ((x == lo || x == hi) && (z == lo || z == hi)) ? LOG : stone(x, z));
        }
        for (final int d : new int[]{5, 6, 7}) { // 3-wide arches (y2-3) on the back and side walls
            m.put(new BlockPos(d, 2, lo), AIR); m.put(new BlockPos(d, 3, lo), AIR);
            m.put(new BlockPos(lo, 2, d), AIR); m.put(new BlockPos(lo, 3, d), AIR);
            m.put(new BlockPos(hi, 2, d), AIR); m.put(new BlockPos(hi, 3, d), AIR);
        }
        door(m, mid, 1, hi, Direction.SOUTH); // front door, south wall

        // Iron-golem cage: a 3×3 dark-oak cage at the centre; the golem spawns at (6,1,6) and is penned in.
        for (final int[] c : new int[][]{{5, 5}, {7, 5}, {5, 7}, {7, 7}}) {
            for (int y = 1; y <= 3; y++) {
                m.put(new BlockPos(c[0], y, c[1]), LOG);
            }
        }
        for (final int[] e : new int[][]{{mid, 5}, {5, mid}, {7, mid}, {mid, 7}}) {
            for (int y = 1; y <= 3; y++) {
                m.put(new BlockPos(e[0], y, e[1]), FENCE);
            }
        }
        m.put(new BlockPos(mid, 0, mid), MOSSY); // cage floor (overwritten by the anchor below)

        // Mid floor slab (y4) with the ladder hole at corner (4,4).
        for (int x = 4; x <= 8; x++) {
            for (int z = 4; z <= 8; z++) {
                m.put(new BlockPos(x, 4, z), (x == 4 && z == 4) ? AIR : PLANK);
            }
        }

        // --- Enclosed spawner room (y5-7): dark-oak walls on a cobble course, log corners, arrow slits. ---
        ring(m, lo, hi, 5, (x, z) -> ((x == lo || x == hi) && (z == lo || z == hi)) ? LOG : stone(x, z));
        for (int y = 6; y <= 7; y++) {
            ring(m, lo, hi, y, (x, z) -> ((x == lo || x == hi) && (z == lo || z == hi)) ? LOG : PLANK);
        }
        m.put(new BlockPos(mid, 6, lo), GLASS); m.put(new BlockPos(mid, 6, hi), GLASS);
        m.put(new BlockPos(lo, 6, mid), GLASS); m.put(new BlockPos(hi, 6, mid), GLASS);
        m.put(new BlockPos(mid, 5, mid), Blocks.SPAWNER.defaultBlockState());
        bes.put(new BlockPos(mid, 5, mid), spawner("minecraft:pillager"));
        m.put(new BlockPos(7, 5, 7), Blocks.CHEST.defaultBlockState().setValue(BlockStateProperties.HORIZONTAL_FACING, Direction.WEST));
        bes.put(new BlockPos(7, 5, 7), lootChest("minecraft:chests/pillager_outpost"));

        // Platform floor (y8), full 7×7 so the ladder backs onto solid, ladder hole at (4,4).
        for (int x = lo; x <= hi; x++) {
            for (int z = lo; z <= hi; z++) {
                m.put(new BlockPos(x, 8, z), (x == 4 && z == 4) ? AIR : PLANK);
            }
        }

        // --- Open watch platform (y9-11): a fence railing, dark-oak corner posts, a lantern. ---
        ring(m, lo, hi, 9, (x, z) -> ((x == lo || x == hi) && (z == lo || z == hi)) ? LOG : FENCE);
        for (final int[] c : new int[][]{{lo, lo}, {hi, lo}, {lo, hi}, {hi, hi}}) {
            for (int y = 9; y <= 11; y++) {
                m.put(new BlockPos(c[0], y, c[1]), LOG);
            }
        }
        m.put(new BlockPos(mid, 11, mid), Blocks.LANTERN.defaultBlockState().setValue(BlockStateProperties.HANGING, true));

        // Pitched dark-oak roof over the platform.
        StructureParts.gableRoof(m, lo, hi, lo, hi, 12, PLANK, Blocks.DARK_OAK_STAIRS, Blocks.DARK_OAK_SLAB, 1);

        // Ladder up the NW inside corner (backs onto the solid x=lo wall the whole way).
        final BlockState ladder = Blocks.LADDER.defaultBlockState().setValue(BlockStateProperties.HORIZONTAL_FACING, Direction.EAST);
        for (int y = 1; y <= 8; y++) {
            m.put(new BlockPos(4, y, 4), ladder);
        }

        // --- Camp apron: two tents, a target, a campfire, a banner, a couple of hay bales. ---
        tent(m, 0, 4, Blocks.WHITE_WOOL.defaultBlockState(), Blocks.BARREL.defaultBlockState());
        tent(m, 10, 6, Blocks.LIGHT_GRAY_WOOL.defaultBlockState(), Blocks.FLETCHING_TABLE.defaultBlockState());
        // Target on a post.
        m.put(new BlockPos(11, 1, 2), FENCE); m.put(new BlockPos(11, 2, 2), FENCE);
        m.put(new BlockPos(11, 3, 2), Blocks.TARGET.defaultBlockState());
        // Campfire + log seats.
        m.put(new BlockPos(2, 1, 11), Blocks.CAMPFIRE.defaultBlockState());
        m.put(new BlockPos(1, 1, 11), LOG); m.put(new BlockPos(3, 1, 11), LOG);
        // Banner on a tall pole.
        for (int y = 1; y <= 3; y++) {
            m.put(new BlockPos(10, y, 11), FENCE);
        }
        m.put(new BlockPos(10, 4, 11), Blocks.GRAY_BANNER.defaultBlockState());
        // Supplies.
        m.put(new BlockPos(1, 1, 1), Blocks.HAY_BLOCK.defaultBlockState());
        m.put(new BlockPos(2, 1, 1), Blocks.HAY_BLOCK.defaultBlockState());
        m.put(new BlockPos(1, 2, 1), Blocks.HAY_BLOCK.defaultBlockState());

        anchor(m, bes, new BlockPos(mid, 0, mid));
        return new Built(m, bes);
    }

    /** Place the perimeter ring of {@code [lo..hi]²} at height {@code y} from a per-column supplier. */
    private interface Col { BlockState at(int x, int z); }
    private static void ring(Map<BlockPos, BlockState> m, int lo, int hi, int y, Col col) {
        for (int x = lo; x <= hi; x++) {
            m.put(new BlockPos(x, y, lo), col.at(x, lo));
            m.put(new BlockPos(x, y, hi), col.at(x, hi));
        }
        for (int z = lo; z <= hi; z++) {
            m.put(new BlockPos(lo, y, z), col.at(lo, z));
            m.put(new BlockPos(hi, y, z), col.at(hi, z));
        }
    }

    /** A small 3×3 canvas tent on dark-oak posts with an item inside, its low corner at {@code (ox,oz)}. */
    private static void tent(Map<BlockPos, BlockState> m, int ox, int oz, BlockState wool, BlockState inside) {
        for (final int[] c : new int[][]{{0, 0}, {2, 0}, {0, 2}, {2, 2}}) {
            m.put(new BlockPos(ox + c[0], 1, oz + c[1]), LOG);
            m.put(new BlockPos(ox + c[0], 2, oz + c[1]), LOG);
        }
        for (int dx = 0; dx <= 2; dx++) {
            for (int dz = 0; dz <= 2; dz++) {
                m.put(new BlockPos(ox + dx, 3, oz + dz), wool);
            }
        }
        m.put(new BlockPos(ox + 1, 4, oz + 1), wool); // peak
        m.put(new BlockPos(ox + 1, 1, oz + 1), inside);
    }

    private static void door(Map<BlockPos, BlockState> m, int x, int y, int z, Direction facing) {
        final BlockState base = Blocks.DARK_OAK_DOOR.defaultBlockState()
                .setValue(BlockStateProperties.HORIZONTAL_FACING, facing)
                .setValue(BlockStateProperties.DOOR_HINGE, DoorHingeSide.LEFT);
        m.put(new BlockPos(x, y, z), base.setValue(BlockStateProperties.DOUBLE_BLOCK_HALF, DoubleBlockHalf.LOWER));
        m.put(new BlockPos(x, y + 1, z), base.setValue(BlockStateProperties.DOUBLE_BLOCK_HALF, DoubleBlockHalf.UPPER));
    }

    private static void anchor(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockPos p) {
        m.put(p, Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.DOWN_SOUTH));
        final CompoundTag t = new CompoundTag();
        t.putString("id", "minecraft:jigsaw");
        t.putString("name", "minecraft:bottom");
        t.putString("target", "minecraft:empty");
        t.putString("pool", "minecraft:empty");
        t.putString("final_state", "minecraft:mossy_cobblestone");
        t.putString("joint", "rollable");
        bes.put(p, t);
    }

    private static CompoundTag spawner(String mobId) {
        final CompoundTag entity = new CompoundTag();
        entity.putString("id", mobId);
        final CompoundTag spawnData = new CompoundTag();
        spawnData.put("entity", entity.copy());
        final CompoundTag potData = new CompoundTag();
        potData.put("entity", entity.copy());
        final CompoundTag potential = new CompoundTag();
        potential.putInt("weight", 1);
        potential.put("data", potData);
        final ListTag potentials = new ListTag();
        potentials.add(potential);
        final CompoundTag be = new CompoundTag();
        be.putString("id", "minecraft:mob_spawner");
        be.put("SpawnData", spawnData);
        be.put("SpawnPotentials", potentials);
        return be;
    }

    private static CompoundTag lootChest(String table) {
        final CompoundTag be = new CompoundTag();
        be.putString("id", "minecraft:chest");
        be.putString("LootTable", table);
        return be;
    }
}
