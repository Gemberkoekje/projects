package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

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

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("portal.nbt"), portal());
        writeIfAbsent(dir.resolve("portal_nether.nbt"), portalNether());
    }

    private static void writeIfAbsent(Path file, Built b) throws IOException {
        if (!Files.exists(file)) {
            StructureWriter.write(b.blocks(), b.blockEntities(), file);
            Skyseed.LOGGER.info("[skyseed] generated structure template {}", file.getFileName());
        }
    }

    private static Built portal() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState obsidian = Blocks.OBSIDIAN.defaultBlockState();
        final BlockState crying = Blocks.CRYING_OBSIDIAN.defaultBlockState();

        // A broken 4-wide × 5-tall frame standing in the z=1 plane — a real, repairable portal shape (2×3 inner)
        // with its top-right corner decayed away and some blocks turned to crying obsidian = ruined. Completing
        // the two missing blocks (2,5)+(3,5) yields a lightable portal.
        m.put(new BlockPos(0, 1, 1), obsidian);
        m.put(new BlockPos(1, 1, 1), crying);
        m.put(new BlockPos(2, 1, 1), obsidian);
        m.put(new BlockPos(3, 1, 1), obsidian);
        m.put(new BlockPos(0, 2, 1), obsidian);
        m.put(new BlockPos(0, 3, 1), crying);
        m.put(new BlockPos(0, 4, 1), obsidian);
        m.put(new BlockPos(0, 5, 1), obsidian);
        m.put(new BlockPos(3, 2, 1), obsidian);
        m.put(new BlockPos(3, 3, 1), obsidian);
        m.put(new BlockPos(3, 4, 1), crying);
        m.put(new BlockPos(1, 5, 1), obsidian); // top is partial — (2,5) and the (3,5) corner are missing

        // Scorched ground accents on the levelled pad: netherrack, a magma block, a gold block.
        m.put(new BlockPos(2, 1, 0), Blocks.NETHERRACK.defaultBlockState());
        m.put(new BlockPos(3, 1, 0), Blocks.MAGMA_BLOCK.defaultBlockState());
        m.put(new BlockPos(0, 1, 0), Blocks.GOLD_BLOCK.defaultBlockState());

        // Loot chest in front of the frame.
        m.put(new BlockPos(2, 1, 2), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.SOUTH));
        bes.put(new BlockPos(2, 1, 2), StructureParts.lootChest("minecraft:chests/ruined_portal"));

        // A small lava spill at the foot of the frame, sunk flush into the levelled pad so the surrounding
        // ground walls it in (it stays put and can't run off the island).
        m.put(new BlockPos(0, 0, 1), Blocks.LAVA.defaultBlockState());
        m.put(new BlockPos(0, 0, 2), Blocks.LAVA.defaultBlockState());
        m.put(new BlockPos(0, 0, 3), Blocks.LAVA.defaultBlockState());

        StructureParts.anchor(m, bes, new BlockPos(1, 0, 1), "minecraft:basalt");
        return new Built(m, bes);
    }

    /**
     * The Nether twin: the same repairable 4×5 frame, but <em>unfinished and empty</em> — no loot chest and no gold
     * block, just scorched netherrack and a walled-in lava dribble on a small netherrack island. The "goodies" live
     * only on the Overworld side; this is the free linked frame at the divided coordinate (see SKYNETHERPLAN). Picked
     * automatically in the Nether via the {@code skyseed:ruined_portal/portal_nether} dimension-variant pool.
     */
    private static Built portalNether() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState obsidian = Blocks.OBSIDIAN.defaultBlockState();
        final BlockState crying = Blocks.CRYING_OBSIDIAN.defaultBlockState();

        // Identical frame shape so it's repairable into a real, lightable portal (complete (2,5)+(3,5)).
        m.put(new BlockPos(0, 1, 1), obsidian);
        m.put(new BlockPos(1, 1, 1), crying);
        m.put(new BlockPos(2, 1, 1), obsidian);
        m.put(new BlockPos(3, 1, 1), obsidian);
        m.put(new BlockPos(0, 2, 1), obsidian);
        m.put(new BlockPos(0, 3, 1), crying);
        m.put(new BlockPos(0, 4, 1), obsidian);
        m.put(new BlockPos(0, 5, 1), obsidian);
        m.put(new BlockPos(3, 2, 1), obsidian);
        m.put(new BlockPos(3, 3, 1), obsidian);
        m.put(new BlockPos(3, 4, 1), crying);
        m.put(new BlockPos(1, 5, 1), obsidian); // top partial — (2,5) and (3,5) missing, repair to light

        // Scorch underfoot — netherrack + a magma block, but NO gold block and NO chest (the twin carries no loot).
        m.put(new BlockPos(2, 1, 0), Blocks.NETHERRACK.defaultBlockState());
        m.put(new BlockPos(3, 1, 0), Blocks.MAGMA_BLOCK.defaultBlockState());

        // A small walled-in lava dribble at the foot of the frame.
        m.put(new BlockPos(0, 0, 1), Blocks.LAVA.defaultBlockState());
        m.put(new BlockPos(0, 0, 2), Blocks.LAVA.defaultBlockState());

        StructureParts.anchor(m, bes, new BlockPos(1, 0, 1), "minecraft:netherrack");
        return new Built(m, bes);
    }
}
