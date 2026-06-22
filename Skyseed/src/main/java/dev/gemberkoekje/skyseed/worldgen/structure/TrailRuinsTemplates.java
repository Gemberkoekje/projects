package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * Trail Ruins: a small buried archaeology site — a mud-brick floor under a gravel layer salted with
 * {@code suspicious_gravel} (brushable, the vanilla {@code archaeology/trail_ruins_*} loot — pottery sherds and
 * friends), low broken walls, and a few fragments poking up through the surface as the tell. Sunk a few blocks
 * (theme {@code sink}) so you spot the fragments, dig in, and brush out the sherds. See {@code SKYSTRUCTURESPLAN.md}.
 */
public final class TrailRuinsTemplates {
    private TrailRuinsTemplates() {}

    private static final BlockState MUD_BRICK = Blocks.MUD_BRICKS.defaultBlockState();
    private static final BlockState PACKED_MUD = Blocks.PACKED_MUD.defaultBlockState();
    private static final BlockState BRICK = Blocks.BRICKS.defaultBlockState();
    private static final BlockState TERRACOTTA = Blocks.TERRACOTTA.defaultBlockState();
    private static final BlockState GRAVEL = Blocks.GRAVEL.defaultBlockState();
    private static final BlockState SUS_GRAVEL = Blocks.SUSPICIOUS_GRAVEL.defaultBlockState();

    private record Built(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> blockEntities) {}

    public static void generateInto(Path dir) throws IOException {
        final Path file = dir.resolve("ruins.nbt");
        if (!Files.exists(file)) {
            final Built b = ruins();
            StructureWriter.write(b.blocks(), b.blockEntities(), file);
            Skyseed.LOGGER.info("[skyseed] generated structure template {}", file.getFileName());
        }
    }

    /** A weathered mud-brick/packed-mud/brick/terracotta mix for the masonry. */
    private static BlockState masonry(int x, int y, int z) {
        return switch (Math.floorMod(x * 5 + y * 11 + z * 7, 5)) {
            case 0, 1 -> MUD_BRICK;
            case 2 -> PACKED_MUD;
            case 3 -> BRICK;
            default -> TERRACOTTA;
        };
    }

    private static Built ruins() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int max = 4; // 5×5

        // y0: the buried mud-brick foundation.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), masonry(x, 0, z));
            }
        }
        // y1: the archaeology layer — gravel with suspicious gravel salted through it.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 1, z), GRAVEL);
            }
        }
        // Six brushable finds — the inner ring common, the centre rare.
        for (final int[] p : new int[][]{{1, 1}, {3, 1}, {1, 3}, {3, 3}, {2, 1}, {2, 3}}) {
            m.put(new BlockPos(p[0], 1, p[1]), SUS_GRAVEL);
            bes.put(new BlockPos(p[0], 1, p[1]), StructureParts.suspicious("minecraft:archaeology/trail_ruins_common"));
        }
        m.put(new BlockPos(2, 1, 2), SUS_GRAVEL);
        bes.put(new BlockPos(2, 1, 2), StructureParts.suspicious("minecraft:archaeology/trail_ruins_rare"));

        // y2: low broken walls — a ruined perimeter with gaps, plus a little gravel rubble.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                if (perim && Math.floorMod(x * 3 + z * 7, 3) != 0) { // ~⅔ of the ring stands, the rest collapsed
                    m.put(new BlockPos(x, 2, z), masonry(x, 2, z));
                } else if (!perim && Math.floorMod(x + z, 4) == 0) {
                    m.put(new BlockPos(x, 2, z), GRAVEL); // a few rubble piles inside
                }
            }
        }
        // y3: a handful of fragments poking up through the island surface — the only tell.
        m.put(new BlockPos(0, 3, 1), masonry(0, 3, 1));
        m.put(new BlockPos(max, 3, 3), masonry(max, 3, 3));
        m.put(new BlockPos(1, 3, max), masonry(1, 3, max));

        StructureParts.anchor(m, bes, new BlockPos(2, 0, 2), "minecraft:mud_bricks");
        return new Built(m, bes);
    }
}
