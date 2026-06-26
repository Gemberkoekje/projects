package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The Ocean Monument — the floating-island translation of the overworld monument. A 13×13 prismarine basin
 * (prismarine / prismarine bricks / dark prismarine, lit by sea lanterns) holds a contained, open-top pool of
 * water with dark-prismarine corner towers rising clear of the surface. An elder guardian and its guardians
 * spawn in the pool (via the theme's {@code animals} / rare-structure {@code mobs} pack), guarding a sea-lantern-lit
 * cache of gold blocks and a {@code chests/buried_treasure} chest — a Heart of the Sea + nautilus shells for a
 * conduit. Mining the build is the prismarine + sea-lantern source, and the guardians are the prismarine-shard
 * source (which also un-blocks the Aquarium recipe). A sponge niche sits in the west wall. See {@code
 * MISSINGBLOCKSPLAN.md}.
 */
public final class OceanMonumentTemplates {
    private OceanMonumentTemplates() {}

    private static final BlockState PRISM = Blocks.PRISMARINE.defaultBlockState();
    private static final BlockState BRICK = Blocks.PRISMARINE_BRICKS.defaultBlockState();
    private static final BlockState DARK = Blocks.DARK_PRISMARINE.defaultBlockState();
    private static final BlockState LAMP = Blocks.SEA_LANTERN.defaultBlockState();
    private static final BlockState WATER = Blocks.WATER.defaultBlockState();
    private static final BlockState SPONGE = Blocks.WET_SPONGE.defaultBlockState();

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("monument.nbt"), monument());
    }

    /** A deterministic prismarine masonry mix — mostly bricks, with dark-prismarine and plain-prismarine accents. */
    private static BlockState mix(int a, int b) {
        return switch (Math.floorMod(a * 7 + b * 5, 6)) {
            case 0, 1, 2 -> BRICK;
            case 3 -> DARK;
            default -> PRISM;
        };
    }

    private static Built monument() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int max = 12, mid = 6;   // 13×13 footprint
        final int wallTop = 5;         // basin walls + water at y1-5, surface flush with the rim

        // Floor (y0, with a sea-lantern grid) + perimeter walls (y1-5, with lantern bands) + the contained pool.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), (x % 4 == 0 && z % 4 == 0) ? LAMP : mix(x, z));
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                for (int y = 1; y <= wallTop; y++) {
                    if (perim) {
                        m.put(new BlockPos(x, y, z), (y == 3 && (x % 4 == 2 || z % 4 == 2)) ? LAMP : mix(x, y + z));
                    } else {
                        m.put(new BlockPos(x, y, z), WATER); // the pool, walled in on all sides + floor, open on top
                    }
                }
            }
        }

        // Dark-prismarine towers (corners + cardinal mid-edges) rising above the water, each lantern-capped.
        for (final int[] c : new int[][]{{1, 1}, {max - 1, 1}, {1, max - 1}, {max - 1, max - 1},
                {mid, 1}, {mid, max - 1}, {1, mid}, {max - 1, mid}}) {
            for (int y = 1; y <= 7; y++) {
                m.put(new BlockPos(c[0], y, c[1]), DARK);
            }
            m.put(new BlockPos(c[0], 8, c[1]), LAMP);
        }

        // Sponge niche set into the west wall, visible from the pool.
        for (int z = mid - 1; z <= mid + 1; z++) {
            m.put(new BlockPos(1, 2, z), SPONGE);
        }

        // Treasure in the back corner, ringed with sea lanterns: a gold-block cache + two buried-treasure chests
        // (Heart of the Sea + nautilus shells → a conduit) flanking it, west and north. The elder guardian roams the
        // centre, guarding them. Two chests so the End-chapter monument relic — gated to this buried-treasure table —
        // gets two rolls per monument (a lone chest left it the rarest relic to find; see SKYENDPLAN Phase 1).
        for (final int[] g : new int[][]{{9, 9}, {10, 9}, {9, 10}, {10, 10}}) {
            m.put(new BlockPos(g[0], 1, g[1]), Blocks.GOLD_BLOCK.defaultBlockState());
        }
        m.put(new BlockPos(9, 0, 9), LAMP);
        m.put(new BlockPos(10, 0, 10), LAMP);
        m.put(new BlockPos(8, 1, 9), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.WEST));
        bes.put(new BlockPos(8, 1, 9), lootChest("minecraft:chests/buried_treasure"));
        m.put(new BlockPos(9, 1, 8), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.NORTH));
        bes.put(new BlockPos(9, 1, 8), lootChest("minecraft:chests/buried_treasure"));

        // Anchor at the centre base; the guardians spawn in the open water just above it.
        anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:prismarine");
        return new Built(m, bes);
    }
}
