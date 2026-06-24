package dev.gemberkoekje.skyseed.worldgen.structure;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.NetherWartBlock;
import net.minecraft.world.level.block.StairBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * A Nether Fortress fragment — deliberately NOT a box. A raised nether-brick walkway is carried on an
 * <em>arcade of arches</em> over a glowing magma channel; the bridge runs straight out of a square <em>keep</em>
 * with a pitched roof, fence-grate windows and a caged <b>blaze spawner</b>, and ends in a little nether-wart
 * garden with a bridge-loot chest. Built block-by-block (no jigsaw sub-pieces). See SKYNETHERPLAN.
 */
public final class NetherFortressTemplates {
    private NetherFortressTemplates() {}

    private static final int LEN = 12;   // bridge spans x = 0..12
    private static final int DECK = 2;   // walkway deck height

    public static void generateInto(Path dir) throws IOException {
        StructureParts.writeIfAbsent(dir.resolve("fortress.nbt"), fortress());
        StructureParts.writeIfAbsent(dir.resolve("blaze_room.nbt"), blazeRoom());
    }

    private static Built fortress() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState nb = Blocks.NETHER_BRICKS.defaultBlockState();
        final BlockState fence = Blocks.NETHER_BRICK_FENCE.defaultBlockState();

        arcadeAndDeck(m, nb, fence);
        keep(m, bes, nb, fence);
        wartEnd(m, bes, nb);

        StructureParts.linkFences(m);
        StructureParts.anchor(m, bes, new BlockPos(LEN / 2, 0, 3), "minecraft:magma_block");
        return new Built(m, bes);
    }

    /** The two arched side-walls (z=1, z=5) that carry the deck, a magma channel between them, and the railed deck. */
    private static void arcadeAndDeck(Map<BlockPos, BlockState> m, BlockState nb, BlockState fence) {
        for (int z = 1; z <= 5; z += 4) {
            for (int p = 0; p <= LEN; p += 4) {          // pillars every 4 blocks
                m.put(new BlockPos(p, 0, z), nb);
                m.put(new BlockPos(p, 1, z), nb);
            }
            for (int p = 0; p < LEN; p += 4) {           // arch springers lean toward each pillar; bay centre stays open
                m.put(new BlockPos(p + 1, 1, z), stair(Direction.WEST));
                m.put(new BlockPos(p + 3, 1, z), stair(Direction.EAST));
            }
        }
        for (int x = 1; x < LEN; x++) {                  // magma channel glowing up through the arches
            m.put(new BlockPos(x, 0, 3), Blocks.MAGMA_BLOCK.defaultBlockState());
        }
        for (int x = 0; x <= LEN; x++) {                 // 5-wide deck + edge railings
            for (int z = 1; z <= 5; z++) {
                m.put(new BlockPos(x, DECK, z), nb);
            }
            m.put(new BlockPos(x, DECK + 1, 1), fence);
            m.put(new BlockPos(x, DECK + 1, 5), fence);
            if (x % 4 == 0) {                            // taller posts over the pillars
                m.put(new BlockPos(x, DECK + 1, 1), nb);
                m.put(new BlockPos(x, DECK + 2, 1), nb);
                m.put(new BlockPos(x, DECK + 1, 5), nb);
                m.put(new BlockPos(x, DECK + 2, 5), nb);
            }
        }
    }

    /** The keep: a 5×5 tower (x=0..4) with a doorway onto the bridge, fence windows, a blaze spawner, a pitched roof. */
    private static void keep(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockState nb, BlockState fence) {
        final int x0 = 0, x1 = 4, z0 = 1, z1 = 5, wallTop = DECK + 4;
        for (int y = DECK + 1; y <= wallTop; y++) {
            for (int x = x0; x <= x1; x++) {
                for (int z = z0; z <= z1; z++) {
                    if (x != x0 && x != x1 && z != z0 && z != z1) {
                        continue; // interior — leave open
                    }
                    if (x == x1 && z == 3 && (y == DECK + 1 || y == DECK + 2)) {
                        continue; // doorway out onto the bridge
                    }
                    m.put(new BlockPos(x, y, z), nb);
                }
            }
        }
        // Fence-grate windows punched into three walls.
        for (int[] w : new int[][] { {1, z0}, {3, z0}, {1, z1}, {3, z1}, {x0, 2}, {x0, 4} }) {
            m.put(new BlockPos(w[0], DECK + 3, w[1]), fence);
        }
        // Caged blaze spawner on a plinth, dead centre.
        m.put(new BlockPos(2, DECK + 1, 3), nb);
        m.put(new BlockPos(2, DECK + 2, 3), Blocks.SPAWNER.defaultBlockState());
        bes.put(new BlockPos(2, DECK + 2, 3), StructureParts.mobSpawner("minecraft:blaze"));
        // Soul-sand braziers with grown wart in two corners.
        for (int[] c : new int[][] { {1, 2}, {3, 4} }) {
            m.put(new BlockPos(c[0], DECK + 1, c[1]), Blocks.SOUL_SAND.defaultBlockState());
            m.put(new BlockPos(c[0], DECK + 2, c[1]), grownWart());
        }
        StructureParts.gableRoof(m, x0, x1, z0, z1, wallTop + 1, nb,
                Blocks.NETHER_BRICK_STAIRS, Blocks.NETHER_BRICK_SLAB, 0);
    }

    /** The far end: a nether-wart garden and a bridge-loot chest under a small stair canopy. */
    private static void wartEnd(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockState nb) {
        for (int x = LEN - 3; x <= LEN - 1; x++) {
            for (int z = 2; z <= 4; z += 2) {
                m.put(new BlockPos(x, DECK + 1, z), Blocks.SOUL_SAND.defaultBlockState());
                m.put(new BlockPos(x, DECK + 2, z), grownWart());
            }
        }
        m.put(new BlockPos(LEN, DECK + 1, 3), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.WEST));
        bes.put(new BlockPos(LEN, DECK + 1, 3), StructureParts.lootChest("minecraft:chests/nether_bridge"));
        for (int z = 2; z <= 4; z++) {
            m.put(new BlockPos(LEN, DECK + 3, z), stair(Direction.WEST)); // little canopy over the chest
        }
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
