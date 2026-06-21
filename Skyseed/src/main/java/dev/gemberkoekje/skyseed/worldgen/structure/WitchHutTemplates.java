package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.LayeredCauldronBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The swamp witch hut: a small spruce cabin with a water cauldron, a crafting table and a potted red
 * mushroom — the witch's brewing aesthetic. No chest; the witch (spawned via the theme's {@code animals}
 * pack, alongside a cat) is the reward when killed. See {@code SKYSTRUCTURESPLAN.md}.
 */
public final class WitchHutTemplates {
    private WitchHutTemplates() {}

    private record Built(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> blockEntities) {}

    public static void generateInto(Path dir) throws IOException {
        final Path file = dir.resolve("hut.nbt");
        if (!Files.exists(file)) {
            final Built b = hut();
            StructureWriter.write(b.blocks(), b.blockEntities(), file);
            Skyseed.LOGGER.info("[skyseed] generated structure template {}", file.getFileName());
        }
    }

    private static Built hut() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState plank = Blocks.SPRUCE_PLANKS.defaultBlockState();
        final BlockState log = Blocks.SPRUCE_LOG.defaultBlockState();
        final int max = 4, mid = 2;

        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), plank); // floor
                m.put(new BlockPos(x, 3, z), plank); // roof
                final boolean corner = (x == 0 || x == max) && (z == 0 || z == max);
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                if (corner) {
                    m.put(new BlockPos(x, 1, z), log);
                    m.put(new BlockPos(x, 2, z), log);
                } else if (perim) {
                    m.put(new BlockPos(x, 1, z), plank);
                    m.put(new BlockPos(x, 2, z), plank);
                }
            }
        }
        // Doorway in the front (z=0) wall.
        m.remove(new BlockPos(mid, 1, 0));
        m.remove(new BlockPos(mid, 2, 0));

        // The witch's kit: a water cauldron, a crafting table, a potted red mushroom.
        m.put(new BlockPos(1, 1, 1), Blocks.WATER_CAULDRON.defaultBlockState().setValue(LayeredCauldronBlock.LEVEL, 3));
        m.put(new BlockPos(3, 1, 1), Blocks.CRAFTING_TABLE.defaultBlockState());
        m.put(new BlockPos(1, 1, 3), Blocks.POTTED_RED_MUSHROOM.defaultBlockState());

        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:spruce_planks");
        return new Built(m, bes);
    }
}
