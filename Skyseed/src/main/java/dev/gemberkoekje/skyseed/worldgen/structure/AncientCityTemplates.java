package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;

/**
 * The Ancient City — the huge Ancient island's rare deep-dark ruin (SKYHUGEPLAN §3). A 15×15 deepslate plaza
 * (tiles + brick borders, weathered with cracked brick) ringed by soul-lantern pillars, with a dark-oak reliquary
 * gazebo over a blue soul fire at its heart: a sculk catalyst, the central chest, and {@code can_summon} sculk
 * shriekers flush in the floor — walk it carelessly and you wake the Warden. Three chests carry the vanilla
 * {@code minecraft:chests/ancient_city} loot (echo shards, Swift Sneak, disc fragments). Anchored on its floor
 * centre so it sits flush on the island's deepslate surface. The structure is placed without block updates, so the
 * soul fire (on soul soil) and flush shriekers persist as authored.
 */
public final class AncientCityTemplates {
    private AncientCityTemplates() {}

    private static final String LOOT = "minecraft:chests/ancient_city";
    private static final int MAX = 14, MID = 7;

    public static void generateInto(Path dir) throws IOException {
        StructureParts.writeIfAbsent(dir.resolve("plaza.nbt"), plaza());
    }

    private static Built plaza() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();

        final BlockState tiles = Blocks.DEEPSLATE_TILES.defaultBlockState();
        final BlockState bricks = Blocks.DEEPSLATE_BRICKS.defaultBlockState();
        final BlockState cracked = Blocks.CRACKED_DEEPSLATE_BRICKS.defaultBlockState();
        final BlockState crackedTiles = Blocks.CRACKED_DEEPSLATE_TILES.defaultBlockState();
        final BlockState sculk = Blocks.SCULK.defaultBlockState();
        final BlockState log = Blocks.DARK_OAK_LOG.defaultBlockState();
        final BlockState planks = Blocks.DARK_OAK_PLANKS.defaultBlockState();

        // Two flush sculk shriekers that can wake the Warden, and the sculk patches that creep around them + the dais.
        final Set<Long> shriekers = Set.of(key(4, 4), key(10, 10));
        final Set<Long> sculkFloor = new HashSet<>();
        for (long s : shriekers) {
            final int sx = (int) (s >> 32), sz = (int) s;
            for (int dx = -1; dx <= 1; dx++) {
                for (int dz = -1; dz <= 1; dz++) {
                    sculkFloor.add(key(sx + dx, sz + dz));
                }
            }
        }
        for (int x = 5; x <= 9; x++) { // a sculk ring just outside the dais
            sculkFloor.add(key(x, 4));
            sculkFloor.add(key(x, 10));
            sculkFloor.add(key(4, x));
            sculkFloor.add(key(10, x));
        }

        // 1) The floor (y0): deepslate tiles, deepslate-brick border, weathered with cracked variants; sculk creeps in.
        for (int x = 0; x <= MAX; x++) {
            for (int z = 0; z <= MAX; z++) {
                final boolean border = x == 0 || x == MAX || z == 0 || z == MAX;
                final boolean weather = (x * 7 + z * 3) % 5 == 0;
                final BlockState floor;
                if (shriekers.contains(key(x, z))) {
                    floor = Blocks.SCULK_SHRIEKER.defaultBlockState().setValue(BlockStateProperties.CAN_SUMMON, true);
                } else if (sculkFloor.contains(key(x, z))) {
                    floor = sculk;
                } else if (border) {
                    floor = weather ? cracked : bricks;
                } else {
                    floor = weather ? crackedTiles : tiles;
                }
                m.put(new BlockPos(x, 0, z), floor);
            }
        }

        // 2) Soul-lantern corner pillars + brick gateposts framing the four entrances.
        for (int[] c : new int[][]{{1, 1}, {1, 13}, {13, 1}, {13, 13}}) {
            for (int y = 1; y <= 4; y++) {
                m.put(new BlockPos(c[0], y, c[1]), y == 2 ? cracked : bricks);
            }
            m.put(new BlockPos(c[0], 5, c[1]), Blocks.SOUL_LANTERN.defaultBlockState()
                    .setValue(BlockStateProperties.HANGING, false));
        }
        for (int[] g : new int[][]{{0, MID}, {MAX, MID}, {MID, 0}, {MID, MAX}}) {
            m.put(new BlockPos(g[0], 1, g[1]), bricks);
            m.put(new BlockPos(g[0], 2, g[1]), Blocks.SOUL_LANTERN.defaultBlockState()
                    .setValue(BlockStateProperties.HANGING, false));
        }

        // 3) The raised 5×5 reliquary dais (deepslate bricks).
        for (int x = 5; x <= 9; x++) {
            for (int z = 5; z <= 9; z++) {
                m.put(new BlockPos(x, 1, z), bricks);
            }
        }
        // Sculk sensors at the dais's near corners (the alarm).
        m.put(new BlockPos(5, 2, 5), Blocks.SCULK_SENSOR.defaultBlockState());
        m.put(new BlockPos(9, 2, 9), Blocks.SCULK_SENSOR.defaultBlockState());

        // 4) The dark-oak gazebo over the heart: log posts, a plank roof, a soul lantern hung beneath.
        for (int[] p : new int[][]{{6, 6}, {8, 6}, {6, 8}, {8, 8}}) {
            m.put(new BlockPos(p[0], 2, p[1]), log);
            m.put(new BlockPos(p[0], 3, p[1]), log);
        }
        for (int x = 6; x <= 8; x++) {
            for (int z = 6; z <= 8; z++) {
                m.put(new BlockPos(x, 4, z), planks);
            }
        }
        m.put(new BlockPos(MID, 3, MID), Blocks.SOUL_LANTERN.defaultBlockState()
                .setValue(BlockStateProperties.HANGING, true));

        // 5) The heart: blue soul fire on soul soil, a sculk catalyst, and the central reliquary chest.
        m.put(new BlockPos(MID, 1, MID), Blocks.SOUL_SOIL.defaultBlockState());
        m.put(new BlockPos(MID, 2, MID), Blocks.SOUL_FIRE.defaultBlockState());
        m.put(new BlockPos(MID, 2, 6), Blocks.SCULK_CATALYST.defaultBlockState());
        chest(m, bes, new BlockPos(MID, 2, 8), Direction.SOUTH);

        // 6) Two side chests on the plaza floor, facing in.
        chest(m, bes, new BlockPos(2, 1, MID), Direction.EAST);
        chest(m, bes, new BlockPos(12, 1, MID), Direction.WEST);

        // 7) Two lit candle clusters for the eerie glow.
        for (int[] c : new int[][]{{3, 11}, {11, 3}}) {
            m.put(new BlockPos(c[0], 1, c[1]), Blocks.CANDLE.defaultBlockState()
                    .setValue(BlockStateProperties.CANDLES, 4).setValue(BlockStateProperties.LIT, true));
        }

        // Anchor on the floor centre so the plaza lands flush on the island's deepslate surface.
        StructureParts.anchor(m, bes, new BlockPos(MID, 0, MID), "minecraft:deepslate_tiles");
        return new Built(m, bes);
    }

    private static void chest(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockPos p, Direction facing) {
        m.put(p, Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, facing));
        bes.put(p, StructureParts.lootChest(LOOT));
    }

    private static long key(int x, int z) {
        return (((long) x) << 32) | (z & 0xffffffffL);
    }
}
