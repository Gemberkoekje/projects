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
 * The Abandoned Mineshaft (SKYDUNGEONPLAN Part B/Phase 4) — a recursing jigsaw modeled piece-for-piece on the vanilla
 * mineshaft: 3-wide carved tunnels with a plank floor, a centre rail line, fence-post-and-plank support arches,
 * scattered cobwebs and the occasional cave-spider nest or chest minecart. Pieces <em>carve</em> (they bake the floor +
 * arches + rails + air interior but NOT the side walls or ceiling), so a sunk run reads as a tunnel through the rock and
 * an over-void run as an open trestle-walkway (which {@code PathSurfacer.supportTrestles} stilts).
 *
 * <p>Generated in two palette {@link Variant variants} from the one geometry: an <b>oak</b> set ({@code mineshaft/}) and
 * a <b>dark-oak mesa</b> set ({@code mineshaft_mesa/}, dark-oak wood + sprinkled gold blocks, à la the vanilla badlands
 * mineshaft). Each variant's pieces self-link through its own {@code <variant>/parts} pool. See
 * {@link DevStructureGenerator}.
 */
public final class MineshaftTemplates {
    private MineshaftTemplates() {}

    private static final BlockState RAIL = Blocks.RAIL.defaultBlockState();
    private static final BlockState WEB = Blocks.COBWEB.defaultBlockState();
    private static final BlockState AIR = Blocks.AIR.defaultBlockState();
    private static final BlockState GOLD = Blocks.GOLD_BLOCK.defaultBlockState();
    private static final String LOOT = "minecraft:chests/abandoned_mineshaft";

    /** A wood palette + its self-link pool: the oak default, or the dark-oak mesa flavour (with sprinkled gold). */
    private record Variant(String dir, String pool, BlockState planks, BlockState fence, BlockState log, boolean gold) {}

    private static final Variant OAK = new Variant("mineshaft", "skyseed:mineshaft/parts",
            Blocks.OAK_PLANKS.defaultBlockState(), Blocks.OAK_FENCE.defaultBlockState(),
            Blocks.OAK_LOG.defaultBlockState(), false);
    private static final Variant MESA = new Variant("mineshaft_mesa", "skyseed:mineshaft_mesa/parts",
            Blocks.DARK_OAK_PLANKS.defaultBlockState(), Blocks.DARK_OAK_FENCE.defaultBlockState(),
            Blocks.DARK_OAK_LOG.defaultBlockState(), true);

    public static void generateInto(Path base) throws IOException {
        for (final Variant v : new Variant[]{OAK, MESA}) {
            final Path dir = base.resolve(v.dir());
            writeIfAbsent(dir.resolve("start.nbt"), start(v));
            writeIfAbsent(dir.resolve("corridor.nbt"), corridor(v, false, false));
            writeIfAbsent(dir.resolve("corridor_loot.nbt"), corridor(v, true, false));
            writeIfAbsent(dir.resolve("corridor_spawner.nbt"), corridor(v, false, true));
            writeIfAbsent(dir.resolve("cross.nbt"), cross(v));
            writeIfAbsent(dir.resolve("room.nbt"), room(v));
            writeIfAbsent(dir.resolve("dead_end.nbt"), deadEnd(v));
        }
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    /** A mineshaft connector at {@code (x,1,z)} facing {@code dir}; its final state is a rail, so the centre track
     *  carries straight through a join. Self-links into the variant's parts pool. */
    private static void link(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int z, FrontAndTop dir, Variant v) {
        m.put(new BlockPos(x, 1, z), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, dir));
        bes.put(new BlockPos(x, 1, z), jig("skyseed:mineshaft", "skyseed:mineshaft", v.pool(), "minecraft:rail"));
    }

    /** A plank floor + a carved 2-tall air tunnel over it; the mesa variant sprinkles a little gold into the floor. */
    private static void tunnel(Map<BlockPos, BlockState> m, int x0, int x1, int z0, int z1, Variant v) {
        for (int x = x0; x <= x1; x++) {
            for (int z = z0; z <= z1; z++) {
                m.put(new BlockPos(x, 0, z), v.gold() && (x * 7 + z * 5) % 16 == 0 ? GOLD : v.planks());
                m.put(new BlockPos(x, 1, z), AIR);
                m.put(new BlockPos(x, 2, z), AIR);
            }
        }
    }

    /** A support arch across a 3-wide tunnel at {@code z}: fence posts at x0/x2, a plank beam over the top. */
    private static void arch(Map<BlockPos, BlockState> m, int x0, int x2, int z, Variant v) {
        m.put(new BlockPos(x0, 1, z), v.fence());
        m.put(new BlockPos(x0, 2, z), v.fence());
        m.put(new BlockPos(x2, 1, z), v.fence());
        m.put(new BlockPos(x2, 2, z), v.fence());
        for (int x = x0; x <= x2; x++) {
            m.put(new BlockPos(x, 3, z), v.planks());
        }
    }

    // ---- pieces ------------------------------------------------------------------------------------------------

    /** A straight 3-wide tunnel (rotated for E–W), doors N + S. {@code minecart} adds a chest-minecart on the rail;
     *  {@code spawner} buries a cave-spider spawner in a cobweb nest at the centre. */
    private static Built corridor(Variant v, boolean minecart, boolean spawner) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final Map<BlockPos, CompoundTag> entities = new HashMap<>();
        tunnel(m, 0, 2, 0, 4, v);
        for (int z = 0; z <= 4; z++) {
            m.put(new BlockPos(1, 1, z), RAIL); // the centre track
        }
        arch(m, 0, 2, 1, v);
        arch(m, 0, 2, 3, v);
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
        link(m, bes, 1, 0, FrontAndTop.NORTH_UP, v);
        link(m, bes, 1, 4, FrontAndTop.SOUTH_UP, v);
        linkFences(m);
        return new Built(m, bes, entities);
    }

    /** A 4-way crossing — the branch points, doors on all sides, rails meeting at the centre. */
    private static Built cross(Variant v) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        tunnel(m, 0, 4, 0, 4, v);
        for (int i = 0; i <= 4; i++) {
            m.put(new BlockPos(2, 1, i), RAIL);
            m.put(new BlockPos(i, 1, 2), RAIL);
        }
        for (final int[] c : new int[][]{{0, 0}, {4, 0}, {0, 4}, {4, 4}}) {
            m.put(new BlockPos(c[0], 1, c[1]), v.fence());
            m.put(new BlockPos(c[0], 2, c[1]), v.fence());
        }
        m.put(new BlockPos(2, 2, 2), WEB);
        link(m, bes, 2, 0, FrontAndTop.NORTH_UP, v);
        link(m, bes, 2, 4, FrontAndTop.SOUTH_UP, v);
        link(m, bes, 0, 2, FrontAndTop.WEST_UP, v);
        link(m, bes, 4, 2, FrontAndTop.EAST_UP, v);
        linkFences(m);
        return new Built(m, bes);
    }

    /** A large open chamber — a 7×7 carved room with log support pillars, a chest, cobwebs; doors N + S. */
    private static Built room(Variant v) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        tunnel(m, 0, 6, 0, 6, v);
        for (int z = 0; z <= 6; z++) {
            m.put(new BlockPos(3, 1, z), RAIL);
        }
        for (final int[] c : new int[][]{{1, 1}, {5, 1}, {1, 5}, {5, 5}}) {
            for (int y = 1; y <= 3; y++) {
                m.put(new BlockPos(c[0], y, c[1]), v.log());
            }
        }
        m.put(new BlockPos(1, 2, 3), WEB);
        m.put(new BlockPos(5, 2, 3), WEB);
        m.put(new BlockPos(5, 1, 1), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.WEST));
        bes.put(new BlockPos(5, 1, 1), lootChest(LOOT));
        link(m, bes, 3, 0, FrontAndTop.NORTH_UP, v);
        link(m, bes, 3, 6, FrontAndTop.SOUTH_UP, v);
        return new Built(m, bes);
    }

    /** A short dead-end stub — one door; the body caps the far end. A cobweb curtain across the mouth. */
    private static Built deadEnd(Variant v) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        tunnel(m, 0, 2, 0, 2, v);
        m.put(new BlockPos(1, 1, 0), RAIL);
        m.put(new BlockPos(1, 1, 1), RAIL);
        arch(m, 0, 2, 2, v);
        m.put(new BlockPos(1, 2, 1), WEB);
        m.put(new BlockPos(0, 1, 1), WEB);
        link(m, bes, 1, 0, FrontAndTop.NORTH_UP, v);
        linkFences(m);
        return new Built(m, bes);
    }

    /** A buried 5×5 crossing hub: four tunnel branches, no surface entrance — the sink seats the mineshaft deep in the
     *  island (you stumble on it by digging in). */
    private static Built start(Variant v) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        tunnel(m, 0, 4, 0, 4, v);
        m.put(new BlockPos(2, 1, 1), RAIL);
        m.put(new BlockPos(2, 1, 2), RAIL);
        m.put(new BlockPos(2, 1, 3), RAIL);
        m.put(new BlockPos(1, 1, 2), RAIL);
        m.put(new BlockPos(3, 1, 2), RAIL);
        for (final int[] c : new int[][]{{0, 0}, {4, 0}, {0, 4}, {4, 4}}) {
            for (int y = 1; y <= 3; y++) {
                m.put(new BlockPos(c[0], y, c[1]), v.log());
            }
        }
        // Four tunnel branches; no surface stair — the mineshaft is fully buried (the sink puts it deep in the island).
        link(m, bes, 2, 0, FrontAndTop.NORTH_UP, v);
        link(m, bes, 2, 4, FrontAndTop.SOUTH_UP, v);
        link(m, bes, 0, 2, FrontAndTop.WEST_UP, v);
        link(m, bes, 4, 2, FrontAndTop.EAST_UP, v);
        anchor(m, bes, new BlockPos(2, 0, 2), "minecraft:" + (v.gold() ? "dark_oak" : "oak") + "_planks");
        return new Built(m, bes);
    }
}
