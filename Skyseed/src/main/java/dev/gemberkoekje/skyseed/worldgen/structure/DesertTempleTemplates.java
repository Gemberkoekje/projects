package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The desert temple's buried treasure chamber: a sealed sandstone room with four chests
 * ({@code minecraft:chests/desert_pyramid}), a hidden 3×3 cache of TNT one block below the floor, and a
 * pressure-plate trap over hidden TNT at each floor corner. The anchor sits on the chamber floor (y1) so the
 * cache (y0) buries below. The plates are baked as wool markers and swapped in by {@link Traps} after the
 * jigsaw assembles (fragile blocks pop on that path); stepping on one fires the corner TNT into the cache.
 * See {@code SKYSTRUCTURESPLAN.md}.
 */
public final class DesertTempleTemplates {
    private DesertTempleTemplates() {}

    private record Built(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> blockEntities) {}

    public static void generateInto(Path dir) throws IOException {
        final Path file = dir.resolve("chamber.nbt");
        if (!Files.exists(file)) {
            final Built b = chamber();
            StructureWriter.write(b.blocks(), b.blockEntities(), file);
            Skyseed.LOGGER.info("[skyseed] generated structure template {}", file.getFileName());
        }
    }

    private static Built chamber() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState sandstone = Blocks.SANDSTONE.defaultBlockState();
        final BlockState cut = Blocks.CUT_SANDSTONE.defaultBlockState();
        final int max = 4, mid = 2;

        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                // y0 sub-floor with the buried 3×3 TNT under the centre.
                final boolean centre3 = x >= 1 && x <= 3 && z >= 1 && z <= 3;
                m.put(new BlockPos(x, 0, z), centre3 ? Blocks.TNT.defaultBlockState() : sandstone);
                // y1 chamber floor.
                m.put(new BlockPos(x, 1, z), sandstone);
                // y2-4 walls (cut sandstone), y5 roof.
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                if (perim) {
                    m.put(new BlockPos(x, 2, z), cut);
                    m.put(new BlockPos(x, 3, z), cut);
                    m.put(new BlockPos(x, 4, z), cut);
                }
                m.put(new BlockPos(x, 5, z), sandstone);
            }
        }
        // A pressure-plate trap at each floor corner over hidden TNT: the plate is baked as a wool marker and
        // swapped to a real plate after assembly (fragile blocks don't survive the jigsaw path). Step on one →
        // the corner TNT fires the buried 3×3 cache below. The plates sit clear of the four wall chests.
        for (int[] c : new int[][]{{1, 1}, {3, 1}, {1, 3}, {3, 3}}) {
            m.put(new BlockPos(c[0], 1, c[1]), Blocks.TNT.defaultBlockState());
            m.put(new BlockPos(c[0], 2, c[1]), Blocks.YELLOW_WOOL.defaultBlockState()); // → stone pressure plate
        }
        // Four treasure chests around the centre, facing in (the buried 3×3 TNT cache sits below them).
        m.put(new BlockPos(1, 2, mid), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.EAST));
        bes.put(new BlockPos(1, 2, mid), StructureParts.lootChest("minecraft:chests/desert_pyramid"));
        m.put(new BlockPos(3, 2, mid), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.WEST));
        bes.put(new BlockPos(3, 2, mid), StructureParts.lootChest("minecraft:chests/desert_pyramid"));
        m.put(new BlockPos(mid, 2, 1), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.SOUTH));
        bes.put(new BlockPos(mid, 2, 1), StructureParts.lootChest("minecraft:chests/desert_pyramid"));
        m.put(new BlockPos(mid, 2, 3), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.NORTH));
        bes.put(new BlockPos(mid, 2, 3), StructureParts.lootChest("minecraft:chests/desert_pyramid"));

        // Anchor on the centre floor block (becomes sandstone; the buried TNT sits directly below it).
        StructureParts.anchor(m, bes, new BlockPos(mid, 1, mid), "minecraft:sandstone");
        return new Built(m, bes);
    }
}
