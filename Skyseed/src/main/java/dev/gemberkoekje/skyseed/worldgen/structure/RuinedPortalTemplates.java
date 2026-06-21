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
 * A ruined Nether portal scene: a broken obsidian frame (some blocks crying obsidian, others missing) over
 * a scorched netherrack-and-gold patch, with a loot chest on the vanilla {@code minecraft:chests/ruined_portal}
 * table. The reward is crying obsidian — otherwise unobtainable in skyblock. See {@code SKYSTRUCTURESPLAN.md}.
 */
public final class RuinedPortalTemplates {
    private RuinedPortalTemplates() {}

    private record Built(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> blockEntities) {}

    public static void generateInto(Path dir) throws IOException {
        final Path file = dir.resolve("portal.nbt");
        if (!Files.exists(file)) {
            final Built b = portal();
            StructureWriter.write(b.blocks(), b.blockEntities(), file);
            Skyseed.LOGGER.info("[skyseed] generated structure template {}", file.getFileName());
        }
    }

    private static Built portal() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState obsidian = Blocks.OBSIDIAN.defaultBlockState();
        final BlockState crying = Blocks.CRYING_OBSIDIAN.defaultBlockState();

        // A broken 4-wide × 5-tall frame standing in the z=1 plane (some blocks missing = ruined).
        m.put(new BlockPos(0, 1, 1), obsidian);
        m.put(new BlockPos(1, 1, 1), crying);
        m.put(new BlockPos(2, 1, 1), obsidian);
        m.put(new BlockPos(3, 1, 1), obsidian);
        m.put(new BlockPos(0, 2, 1), obsidian);
        m.put(new BlockPos(0, 3, 1), crying);
        m.put(new BlockPos(0, 4, 1), obsidian);
        m.put(new BlockPos(3, 2, 1), obsidian);
        m.put(new BlockPos(3, 3, 1), obsidian);
        m.put(new BlockPos(1, 4, 1), obsidian); // top is partial (2,4 and 3,4 missing)

        // Scorched ground accents on the levelled pad: netherrack, a magma block, a gold block.
        m.put(new BlockPos(2, 1, 0), Blocks.NETHERRACK.defaultBlockState());
        m.put(new BlockPos(3, 1, 0), Blocks.MAGMA_BLOCK.defaultBlockState());
        m.put(new BlockPos(0, 1, 0), Blocks.GOLD_BLOCK.defaultBlockState());

        // Loot chest in front of the frame.
        m.put(new BlockPos(2, 1, 2), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.SOUTH));
        bes.put(new BlockPos(2, 1, 2), StructureParts.lootChest("minecraft:chests/ruined_portal"));

        StructureParts.anchor(m, bes, new BlockPos(1, 0, 1), "minecraft:basalt");
        return new Built(m, bes);
    }
}
