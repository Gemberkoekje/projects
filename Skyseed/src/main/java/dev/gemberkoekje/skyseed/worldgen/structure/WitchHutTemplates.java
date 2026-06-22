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
        final BlockState glass = Blocks.GLASS.defaultBlockState();
        final int lo = 1, hi = 5, mid = 3, ceil = 4; // inset one block so the roof overhang stays in bounds

        for (int x = lo; x <= hi; x++) {
            for (int z = lo; z <= hi; z++) {
                m.put(new BlockPos(x, 0, z), plank); // floor
                final boolean perim = x == lo || x == hi || z == lo || z == hi;
                final boolean corner = (x == lo || x == hi) && (z == lo || z == hi);
                if (perim) {
                    for (int h = 1; h <= 3; h++) {
                        m.put(new BlockPos(x, h, z), corner ? log : plank);
                    }
                }
                m.put(new BlockPos(x, ceil, z), plank); // ceiling
            }
        }
        // Open doorway in the front (z = lo) wall; a window on each of the other three walls.
        m.remove(new BlockPos(mid, 1, lo));
        m.remove(new BlockPos(mid, 2, lo));
        m.put(new BlockPos(lo, 2, mid), glass);
        m.put(new BlockPos(hi, 2, mid), glass);
        m.put(new BlockPos(mid, 2, hi), glass);

        // The witch's kit: a water cauldron, a crafting table, a potted red mushroom (the centre kept clear).
        m.put(new BlockPos(lo + 1, 1, lo + 1), Blocks.WATER_CAULDRON.defaultBlockState().setValue(LayeredCauldronBlock.LEVEL, 3));
        m.put(new BlockPos(hi - 1, 1, lo + 1), Blocks.CRAFTING_TABLE.defaultBlockState());
        m.put(new BlockPos(lo + 1, 1, hi - 1), Blocks.POTTED_RED_MUSHROOM.defaultBlockState());

        // Pitched spruce gable roof with a one-block overhang.
        StructureParts.gableRoof(m, lo, hi, lo, hi, ceil, plank, Blocks.SPRUCE_STAIRS, Blocks.SPRUCE_SLAB, 1);
        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:spruce_planks");
        return new Built(m, bes);
    }
}
