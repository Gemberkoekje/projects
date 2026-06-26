package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import net.minecraft.core.BlockPos;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The <b>Return Portal shrine</b> (SKYENDPLAN — the way home): what the End-only Return Portal Seed grows. A small
 * end-stone island carrying an obsidian-framed <b>End exit portal</b> — a 3×3 of {@code end_portal} blocks which, in
 * the End, drop the player back to the overworld (the same block the dragon's exit fountain uses). Lit by end rods on
 * four corner spires and flanking the gateway. Skyseed's void End has no natural exit fountain, so this is how you get
 * out without dying.
 */
public final class ReturnPortalTemplates {
    private ReturnPortalTemplates() {}

    private static final BlockState BRICK = Blocks.END_STONE_BRICKS.defaultBlockState();
    private static final BlockState OBSIDIAN = Blocks.OBSIDIAN.defaultBlockState();
    private static final BlockState PORTAL = Blocks.END_PORTAL.defaultBlockState();
    private static final BlockState ROD = Blocks.END_ROD.defaultBlockState();

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("shrine.nbt"), shrine());
    }

    private static Built shrine() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int max = 8, mid = 4;   // 9×9 footprint

        // Platform (y1): end-stone bricks, an obsidian frame (the 5×5 ring), and the 3×3 exit portal at the centre.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                final boolean inner = x >= 2 && x <= 6 && z >= 2 && z <= 6;
                final boolean centre = x >= 3 && x <= 5 && z >= 3 && z <= 5;
                m.put(new BlockPos(x, 1, z), centre ? PORTAL : inner ? OBSIDIAN : BRICK);
            }
        }
        // End-rod glow: one flanking each side of the gateway, and one atop each of four obsidian corner spires.
        for (final int[] c : new int[][]{{mid, 2}, {mid, 6}, {2, mid}, {6, mid}}) {
            m.put(new BlockPos(c[0], 2, c[1]), ROD);
        }
        for (final int[] c : new int[][]{{1, 1}, {max - 1, 1}, {1, max - 1}, {max - 1, max - 1}}) {
            for (int y = 2; y <= 4; y++) {
                m.put(new BlockPos(c[0], y, c[1]), OBSIDIAN);
            }
            m.put(new BlockPos(c[0], 5, c[1]), ROD);
        }

        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:end_stone");
        return new Built(m, bes);
    }
}
