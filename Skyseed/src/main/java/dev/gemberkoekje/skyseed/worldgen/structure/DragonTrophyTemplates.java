package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import net.minecraft.core.BlockPos;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The <b>Dragon Monument</b> (SKYENDPLAN Phase 6 — the capstone trophy). What the Dragon Trophy Seed grows: a grand
 * stepped end-stone-brick dais carrying a central purpur-capped pedestal for the <b>dragon egg</b> (left empty — the
 * player sets their one earned egg on it), flanked by end rods, with four obsidian obelisks crowned by inward-facing
 * <b>dragon heads</b> guarding it. A set-piece only — it never touches the fixed dragon fight or {@code EndDragonFight}.
 */
public final class DragonTrophyTemplates {
    private DragonTrophyTemplates() {}

    private static final BlockState BRICK = Blocks.END_STONE_BRICKS.defaultBlockState();
    private static final BlockState OBSIDIAN = Blocks.OBSIDIAN.defaultBlockState();
    private static final BlockState PURPUR = Blocks.PURPUR_BLOCK.defaultBlockState();
    private static final BlockState ROD = Blocks.END_ROD.defaultBlockState();

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("monument.nbt"), monument());
    }

    private static Built monument() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();

        // Stepped dais (end-stone bricks): 11×11 → 9×9 → 7×7.
        for (int x = 0; x <= 10; x++) for (int z = 0; z <= 10; z++) m.put(new BlockPos(x, 1, z), BRICK);
        for (int x = 1; x <= 9; x++) for (int z = 1; z <= 9; z++) m.put(new BlockPos(x, 2, z), BRICK);
        for (int x = 2; x <= 8; x++) for (int z = 2; z <= 8; z++) m.put(new BlockPos(x, 3, z), BRICK);

        // Central egg pedestal: obsidian base, a purpur cap, the egg seat (5,6,5) left empty for the player's egg.
        m.put(new BlockPos(5, 4, 5), OBSIDIAN);
        m.put(new BlockPos(5, 5, 5), PURPUR);
        for (final int[] r : new int[][]{{4, 5}, {6, 5}, {5, 4}, {5, 6}}) m.put(new BlockPos(r[0], 4, r[1]), ROD);

        // Four obsidian obelisks at the top-dais corners, each crowned by a dragon head facing the egg.
        final int[][] corners = {{2, 2, 10}, {8, 2, 6}, {2, 8, 14}, {8, 8, 2}};   // x, z, dragon-head rotation toward centre
        for (final int[] c : corners) {
            for (int y = 4; y <= 6; y++) m.put(new BlockPos(c[0], y, c[1]), OBSIDIAN);
            m.put(new BlockPos(c[0], 7, c[1]),
                    Blocks.DRAGON_HEAD.defaultBlockState().setValue(BlockStateProperties.ROTATION_16, c[2]));
        }

        StructureParts.anchor(m, bes, new BlockPos(5, 0, 5), "minecraft:end_stone");
        return new Built(m, bes);
    }
}
