package dev.gemberkoekje.skyseed.worldgen.structure;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.FrontAndTop;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.JigsawBlock;
import net.minecraft.world.level.block.NetherWartBlock;
import net.minecraft.world.level.block.StairBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The Nether Fortress as a small jigsaw network (SKYJIGSAWPLAN §4d), modelled on vanilla's NetherFortressPieces but
 * with a tight, BOUNDED vocabulary so one seed doesn't sprawl to the horizon. A <em>keep</em> with the caged blaze
 * spawner is the start; self-connecting arcaded <em>bridge spans</em> (a 5-wide deck on arches over a glowing magma
 * channel) march out over the void; and a railed wart-garden <em>end</em> caps each run. The theme bounds it with a
 * low {@code depth} and a {@code span_} cap (surplus spans become ends), so the fortress stays a compact, varied
 * branching bridge rather than vanilla's depth-30 sprawl. The standalone {@code blaze_room} (the 5% rare roll on Large
 * Nether seeds) is kept too. Every deck/connector sits at {@link #DECK}, two blocks up on the arch base, so the pieces
 * tile cleanly. See SKYNETHERPLAN.
 */
public final class NetherFortressTemplates {
    private NetherFortressTemplates() {}

    private static final int DECK = 2;   // walkway deck height — connectors sit here so pieces tile
    private static final int BAY = 4;    // one arcade bay: a span is x = 0..3, the next pillar lands at x = 4
    private static final String SPANS = "skyseed:nether_fortress/spans";

    public static void generateInto(Path dir) throws IOException {
        StructureParts.writeIfAbsent(dir.resolve("keep.nbt"), keep());
        StructureParts.writeIfAbsent(dir.resolve("span_bridge.nbt"), spanBridge());
        StructureParts.writeIfAbsent(dir.resolve("end.nbt"), end());
        StructureParts.writeIfAbsent(dir.resolve("blaze_room.nbt"), blazeRoom());
    }

    /**
     * The start: a 5×5 keep on a short arch base, with a caged blaze spawner, fence windows, a pitched roof, and a
     * doorway onto a {@code bridge} connector out the +X side. The island anchor ({@code bottom}) sits at its base.
     */
    private static Built keep() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState nb = Blocks.NETHER_BRICKS.defaultBlockState();
        final BlockState fence = Blocks.NETHER_BRICK_FENCE.defaultBlockState();
        final int x0 = 0, x1 = 4, z0 = 1, z1 = 5, wallTop = DECK + 4;

        // Arch base + 5×5 deck under the tower (corner pillars carry it; a brazier of magma glows at the centre).
        for (int x = x0; x <= x1; x += 4) {
            for (int z = z0; z <= z1; z += 4) {
                m.put(new BlockPos(x, 0, z), nb);
                m.put(new BlockPos(x, 1, z), nb);
            }
        }
        m.put(new BlockPos(2, 1, 3), Blocks.MAGMA_BLOCK.defaultBlockState());
        for (int x = x0; x <= x1; x++) {
            for (int z = z0; z <= z1; z++) {
                m.put(new BlockPos(x, DECK, z), nb);
            }
        }
        // Walls (perimeter only), with the doorway out onto the bridge on the +X wall.
        for (int y = DECK + 1; y <= wallTop; y++) {
            for (int x = x0; x <= x1; x++) {
                for (int z = z0; z <= z1; z++) {
                    if (x != x0 && x != x1 && z != z0 && z != z1) {
                        continue;
                    }
                    if (x == x1 && z == 3 && (y == DECK + 1 || y == DECK + 2)) {
                        continue; // doorway
                    }
                    m.put(new BlockPos(x, y, z), nb);
                }
            }
        }
        for (int[] w : new int[][] { {1, z0}, {3, z0}, {1, z1}, {3, z1}, {x0, 2}, {x0, 4} }) {
            m.put(new BlockPos(w[0], DECK + 3, w[1]), fence); // fence-grate windows
        }
        // Caged blaze spawner on a plinth, dead centre; soul-sand/wart braziers in two corners.
        m.put(new BlockPos(2, DECK + 1, 3), nb);
        m.put(new BlockPos(2, DECK + 2, 3), Blocks.SPAWNER.defaultBlockState());
        bes.put(new BlockPos(2, DECK + 2, 3), StructureParts.mobSpawner("minecraft:blaze"));
        for (int[] c : new int[][] { {1, 2}, {3, 4} }) {
            m.put(new BlockPos(c[0], DECK + 1, c[1]), Blocks.SOUL_SAND.defaultBlockState());
            m.put(new BlockPos(c[0], DECK + 2, c[1]), grownWart());
        }
        StructureParts.gableRoof(m, x0, x1, z0, z1, wallTop + 1, nb,
                Blocks.NETHER_BRICK_STAIRS, Blocks.NETHER_BRICK_SLAB, 0);

        bridgeConn(m, bes, new BlockPos(x1, DECK, 3), FrontAndTop.EAST_UP, SPANS); // the bridge out
        StructureParts.linkFences(m);
        StructureParts.anchor(m, bes, new BlockPos(2, 0, 3), "minecraft:nether_bricks");
        return new Built(m, bes);
    }

    /**
     * One arcaded bridge bay (x = 0..3): a 5-wide nether-brick deck carried on an arch springing off a pillar, over a
     * glowing magma channel, with fence railings. A {@code bridge} connector at each end self-connects into the spans
     * pool, so a run of these marches out over the void.
     */
    private static Built spanBridge() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState nb = Blocks.NETHER_BRICKS.defaultBlockState();
        final BlockState fence = Blocks.NETHER_BRICK_FENCE.defaultBlockState();

        for (int z = 1; z <= 5; z += 4) {
            m.put(new BlockPos(0, 0, z), nb);                 // a pillar at the near end
            m.put(new BlockPos(0, 1, z), nb);
            m.put(new BlockPos(1, 1, z), stair(Direction.WEST));   // arch springs back to this pillar
            m.put(new BlockPos(3, 1, z), stair(Direction.EAST));   // and forward to the next bay's pillar
        }
        for (int x = 0; x < BAY; x++) {
            m.put(new BlockPos(x, 1, 3), Blocks.MAGMA_BLOCK.defaultBlockState()); // magma channel under the deck
            for (int z = 1; z <= 5; z++) {
                m.put(new BlockPos(x, DECK, z), nb);          // the 5-wide deck
            }
            m.put(new BlockPos(x, DECK + 1, 1), fence);       // edge railings
            m.put(new BlockPos(x, DECK + 1, 5), fence);
        }
        m.put(new BlockPos(0, DECK + 1, 1), nb);              // taller posts over the pillar
        m.put(new BlockPos(0, DECK + 2, 1), nb);
        m.put(new BlockPos(0, DECK + 1, 5), nb);
        m.put(new BlockPos(0, DECK + 2, 5), nb);

        bridgeConn(m, bes, new BlockPos(0, DECK, 3), FrontAndTop.WEST_UP, SPANS);       // entry
        bridgeConn(m, bes, new BlockPos(BAY - 1, DECK, 3), FrontAndTop.EAST_UP, SPANS); // onward
        StructureParts.linkFences(m);
        return new Built(m, bes);
    }

    /**
     * A run's end (x = 0..2): a short railed deck with a nether-wart garden and a bridge-loot chest under a stair
     * canopy. Its lone {@code bridge} connector only receives (pool {@code empty}), so the branch stops cleanly. This
     * is the {@code cap_filler} the span cap re-stamps surplus spans into, which is what bounds the sprawl.
     */
    private static Built end() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState nb = Blocks.NETHER_BRICKS.defaultBlockState();
        final BlockState fence = Blocks.NETHER_BRICK_FENCE.defaultBlockState();

        for (int z = 1; z <= 5; z += 4) {
            m.put(new BlockPos(0, 0, z), nb);
            m.put(new BlockPos(0, 1, z), nb);
        }
        for (int x = 0; x <= 2; x++) {
            for (int z = 1; z <= 5; z++) {
                m.put(new BlockPos(x, DECK, z), nb);
            }
            m.put(new BlockPos(x, DECK + 1, 1), fence);
            m.put(new BlockPos(x, DECK + 1, 5), fence);
        }
        m.put(new BlockPos(2, DECK + 1, 3), fence);           // railed cap across the far end
        // A nether-wart garden and a loot chest under a small stair canopy.
        for (int z = 2; z <= 4; z += 2) {
            m.put(new BlockPos(1, DECK + 1, z), Blocks.SOUL_SAND.defaultBlockState());
            m.put(new BlockPos(1, DECK + 2, z), grownWart());
        }
        m.put(new BlockPos(2, DECK + 1, 3), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.WEST));
        bes.put(new BlockPos(2, DECK + 1, 3), StructureParts.lootChest("minecraft:chests/nether_bridge"));
        for (int z = 2; z <= 4; z++) {
            m.put(new BlockPos(2, DECK + 3, z), stair(Direction.WEST));
        }

        // Receive-only connector (pool empty), name "bridge" so it mates with a span's onward connector.
        m.put(new BlockPos(0, DECK, 3), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.WEST_UP));
        bes.put(new BlockPos(0, DECK, 3), StructureParts.jig("bridge", "empty", "minecraft:empty", "minecraft:nether_bricks"));
        StructureParts.linkFences(m);
        return new Built(m, bes);
    }

    /** A {@code bridge} jigsaw connector (name = target = "bridge") that pulls/continues from {@code pool}. */
    private static void bridgeConn(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockPos pos,
                                   FrontAndTop dir, String pool) {
        m.put(pos, Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, dir));
        bes.put(pos, StructureParts.jig("bridge", "bridge", pool, "minecraft:nether_bricks"));
    }

    /**
     * The standalone surprise: a 9×9 open-top nether-brick room — fence-grate windows, a doorway, a caged
     * <b>blaze spawner</b> on a plinth, soul-sand/wart braziers and a bridge-loot chest. The 5% rare roll on Large
     * Nether seeds, and the debug seed. The keep's design pulled out of the fortress to stand on its own.
     */
    private static Built blazeRoom() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState nb = Blocks.NETHER_BRICKS.defaultBlockState();
        final BlockState fence = Blocks.NETHER_BRICK_FENCE.defaultBlockState();
        final int x0 = 0, x1 = 8, z0 = 0, z1 = 8, wallTop = 4, mid = 4;

        for (int x = x0; x <= x1; x++) {
            for (int z = z0; z <= z1; z++) {
                m.put(new BlockPos(x, 0, z), nb);     // floor
            }
        }
        for (int y = 1; y <= wallTop; y++) {           // walls, with a doorway on the +X wall
            for (int x = x0; x <= x1; x++) {
                for (int z = z0; z <= z1; z++) {
                    if (x != x0 && x != x1 && z != z0 && z != z1) {
                        continue;
                    }
                    if (x == x1 && z == mid && (y == 1 || y == 2)) {
                        continue; // doorway
                    }
                    m.put(new BlockPos(x, y, z), nb);
                }
            }
        }
        for (int[] w : new int[][] { {2, z0}, {6, z0}, {2, z1}, {6, z1}, {x0, 2}, {x0, 6}, {x1, 2}, {x1, 6} }) {
            m.put(new BlockPos(w[0], 3, w[1]), fence); // fence-grate windows
        }
        // Caged blaze spawner on a plinth, dead centre.
        m.put(new BlockPos(mid, 1, mid), nb);
        m.put(new BlockPos(mid, 2, mid), Blocks.SPAWNER.defaultBlockState());
        bes.put(new BlockPos(mid, 2, mid), StructureParts.mobSpawner("minecraft:blaze"));
        // Soul-sand braziers with grown wart in two corners.
        for (int[] c : new int[][] { {1, 1}, {7, 7} }) {
            m.put(new BlockPos(c[0], 1, c[1]), Blocks.SOUL_SAND.defaultBlockState());
            m.put(new BlockPos(c[0], 2, c[1]), grownWart());
        }
        // A bridge-loot chest in a corner.
        m.put(new BlockPos(7, 1, 1), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.WEST));
        bes.put(new BlockPos(7, 1, 1), StructureParts.lootChest("minecraft:chests/nether_bridge"));

        StructureParts.linkFences(m);
        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:nether_bricks");
        return new Built(m, bes);
    }

    private static BlockState stair(Direction facing) {
        return Blocks.NETHER_BRICK_STAIRS.defaultBlockState().setValue(StairBlock.FACING, facing);
    }

    private static BlockState grownWart() {
        return Blocks.NETHER_WART.defaultBlockState().setValue(NetherWartBlock.AGE, 3);
    }
}
