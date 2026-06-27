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

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The Abandoned Mineshaft (SKYDUNGEONPLAN Part B) — a recursing jigsaw modeled piece-for-piece on the vanilla
 * mineshaft: 3-wide carved tunnels with an oak-plank floor, a centre rail line, fence-post-and-plank support arches
 * every few blocks, scattered cobwebs and the occasional cave-spider nest or chest minecart. Pieces <em>carve</em>
 * (they bake the floor + arches + rails + air interior but NOT the side walls or ceiling), so where the run is sunk in
 * the island body it reads as a tunnel through the rock, and where it sprawls out over the void it reads as an open
 * wooden trestle-walkway — which {@code PathSurfacer.supportTrestles} (Phase 3) then stilts with fence legs. Every
 * piece self-links through {@code mineshaft/parts} on {@code skyseed:mineshaft}; the {@code start} hub carries the
 * surface stair. See {@link DevStructureGenerator}.
 */
public final class MineshaftTemplates {
    private MineshaftTemplates() {}

    private static final String POOL = "skyseed:mineshaft/parts";

    private static final BlockState PLANKS = Blocks.OAK_PLANKS.defaultBlockState();
    private static final BlockState FENCE = Blocks.OAK_FENCE.defaultBlockState();
    private static final BlockState RAIL = Blocks.RAIL.defaultBlockState();
    private static final BlockState WEB = Blocks.COBWEB.defaultBlockState();
    private static final BlockState AIR = Blocks.AIR.defaultBlockState();
    private static final String LOOT = "minecraft:chests/abandoned_mineshaft";

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("start.nbt"), start());
        writeIfAbsent(dir.resolve("corridor.nbt"), corridor(false, false));
        writeIfAbsent(dir.resolve("corridor_loot.nbt"), corridor(true, false));
        writeIfAbsent(dir.resolve("corridor_spawner.nbt"), corridor(false, true));
        writeIfAbsent(dir.resolve("cross.nbt"), cross());
        writeIfAbsent(dir.resolve("room.nbt"), room());
        writeIfAbsent(dir.resolve("dead_end.nbt"), deadEnd());
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    /** A mineshaft connector at {@code (x,1,z)} facing {@code dir}; its final state is a rail, so the centre track
     *  carries straight through a join. Self-links into the parts pool. */
    private static void link(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int z, FrontAndTop dir) {
        m.put(new BlockPos(x, 1, z), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, dir));
        bes.put(new BlockPos(x, 1, z), jig("skyseed:mineshaft", "skyseed:mineshaft", POOL, "minecraft:rail"));
    }

    /** An oak-plank floor + a carved 2-tall air tunnel over it, across the given footprint. */
    private static void tunnel(Map<BlockPos, BlockState> m, int x0, int x1, int z0, int z1) {
        for (int x = x0; x <= x1; x++) {
            for (int z = z0; z <= z1; z++) {
                m.put(new BlockPos(x, 0, z), PLANKS);
                m.put(new BlockPos(x, 1, z), AIR);
                m.put(new BlockPos(x, 2, z), AIR);
            }
        }
    }

    /** A support arch across a 3-wide tunnel at {@code z}: fence posts at x0/x2, a plank beam over the top. */
    private static void arch(Map<BlockPos, BlockState> m, int x0, int x2, int z) {
        m.put(new BlockPos(x0, 1, z), FENCE);
        m.put(new BlockPos(x0, 2, z), FENCE);
        m.put(new BlockPos(x2, 1, z), FENCE);
        m.put(new BlockPos(x2, 2, z), FENCE);
        for (int x = x0; x <= x2; x++) {
            m.put(new BlockPos(x, 3, z), PLANKS);
        }
    }

    // ---- pieces ------------------------------------------------------------------------------------------------

    /** A straight 3-wide tunnel (rotated for E–W), doors N + S. {@code minecart} adds a chest-minecart on the rail;
     *  {@code spawner} buries a cave-spider spawner in a cobweb nest at the centre. */
    private static Built corridor(boolean minecart, boolean spawner) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final Map<BlockPos, CompoundTag> entities = new HashMap<>();
        tunnel(m, 0, 2, 0, 4);
        for (int z = 0; z <= 4; z++) {
            m.put(new BlockPos(1, 1, z), RAIL); // the centre track
        }
        arch(m, 0, 2, 1);
        arch(m, 0, 2, 3);
        m.put(new BlockPos(0, 2, 2), WEB);
        m.put(new BlockPos(2, 1, 3), WEB);
        if (minecart) {
            entities.put(new BlockPos(1, 1, 2), chestMinecart(LOOT));
        }
        if (spawner) {
            m.put(new BlockPos(1, 1, 2), Blocks.SPAWNER.defaultBlockState());
            bes.put(new BlockPos(1, 1, 2), mobSpawner("minecraft:cave_spider"));
            for (final int[] d : new int[][]{{0, 2}, {2, 2}, {1, 2}, {0, 1}, {2, 1}, {0, 3}, {2, 3}}) {
                m.putIfAbsent(new BlockPos(d[0], 2, d[1]), WEB); // a cobweb nest around the spawner
            }
        }
        link(m, bes, 1, 0, FrontAndTop.NORTH_UP);
        link(m, bes, 1, 4, FrontAndTop.SOUTH_UP);
        linkFences(m);
        return new Built(m, bes, entities);
    }

    /** A 4-way crossing — the branch points, doors on all sides, rails meeting at the centre. */
    private static Built cross() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        tunnel(m, 0, 4, 0, 4);
        for (int i = 0; i <= 4; i++) {
            m.put(new BlockPos(2, 1, i), RAIL);
            m.put(new BlockPos(i, 1, 2), RAIL);
        }
        // corner support posts + a beam frame over the crossing
        for (final int[] c : new int[][]{{0, 0}, {4, 0}, {0, 4}, {4, 4}}) {
            m.put(new BlockPos(c[0], 1, c[1]), FENCE);
            m.put(new BlockPos(c[0], 2, c[1]), FENCE);
        }
        m.put(new BlockPos(2, 2, 2), WEB);
        link(m, bes, 2, 0, FrontAndTop.NORTH_UP);
        link(m, bes, 2, 4, FrontAndTop.SOUTH_UP);
        link(m, bes, 0, 2, FrontAndTop.WEST_UP);
        link(m, bes, 4, 2, FrontAndTop.EAST_UP);
        linkFences(m);
        return new Built(m, bes);
    }

    /** A large open chamber — a 7×7 carved room with oak-log pillars, a chest, cobwebs; doors N + S. */
    private static Built room() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        tunnel(m, 0, 6, 0, 6);
        for (int z = 0; z <= 6; z++) {
            m.put(new BlockPos(3, 1, z), RAIL);
        }
        // four oak-log support pillars to the (body) ceiling
        for (final int[] c : new int[][]{{1, 1}, {5, 1}, {1, 5}, {5, 5}}) {
            for (int y = 1; y <= 3; y++) {
                m.put(new BlockPos(c[0], y, c[1]), Blocks.OAK_LOG.defaultBlockState());
            }
        }
        m.put(new BlockPos(1, 2, 3), WEB);
        m.put(new BlockPos(5, 2, 3), WEB);
        m.put(new BlockPos(5, 1, 1), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.WEST));
        bes.put(new BlockPos(5, 1, 1), lootChest(LOOT));
        link(m, bes, 3, 0, FrontAndTop.NORTH_UP);
        link(m, bes, 3, 6, FrontAndTop.SOUTH_UP);
        return new Built(m, bes);
    }

    /** A short dead-end stub — one door; the body caps the far end. A cobweb curtain across the mouth. */
    private static Built deadEnd() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        tunnel(m, 0, 2, 0, 2);
        m.put(new BlockPos(1, 1, 0), RAIL);
        m.put(new BlockPos(1, 1, 1), RAIL);
        arch(m, 0, 2, 2);
        m.put(new BlockPos(1, 2, 1), WEB);
        m.put(new BlockPos(0, 1, 1), WEB);
        link(m, bes, 1, 0, FrontAndTop.NORTH_UP);
        linkFences(m);
        return new Built(m, bes);
    }

    /** The entrance hub: a carved 5×5 chamber with tunnel doors N/W/E and an open stepped stair climbing +Z to the
     *  surface (seated by {@code sink:6}). */
    private static Built start() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        tunnel(m, 0, 4, 0, 4);
        m.put(new BlockPos(2, 1, 1), RAIL);
        m.put(new BlockPos(2, 1, 2), RAIL);
        m.put(new BlockPos(2, 1, 3), RAIL);
        m.put(new BlockPos(1, 1, 2), RAIL);
        m.put(new BlockPos(3, 1, 2), RAIL);
        for (final int[] c : new int[][]{{0, 0}, {4, 0}, {0, 4}, {4, 4}}) {
            for (int y = 1; y <= 3; y++) {
                m.put(new BlockPos(c[0], y, c[1]), Blocks.OAK_LOG.defaultBlockState());
            }
        }
        link(m, bes, 2, 0, FrontAndTop.NORTH_UP);
        link(m, bes, 0, 2, FrontAndTop.WEST_UP);
        link(m, bes, 4, 2, FrontAndTop.EAST_UP);

        // Entrance: an open stepped stair climbing +Z out of the hub up to the surface (nbt y6 = sink 6).
        for (int i = 0; i <= 6; i++) {
            final int z = 5 + i;
            final int treadY = Math.min(6, i);
            m.put(new BlockPos(2, treadY, z), PLANKS);
            for (int y = treadY + 1; y <= 6; y++) {
                m.put(new BlockPos(2, y, z), AIR);
            }
        }
        anchor(m, bes, new BlockPos(2, 0, 2), "minecraft:oak_planks");
        return new Built(m, bes);
    }
}
