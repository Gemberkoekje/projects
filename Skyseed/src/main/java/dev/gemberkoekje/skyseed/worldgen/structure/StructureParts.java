package dev.gemberkoekje.skyseed.worldgen.structure;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.FrontAndTop;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.JigsawBlock;
import net.minecraft.world.level.block.StairBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.util.Map;

/** Shared building blocks for the code-authored structure-island templates: the jigsaw anchor, loot chests, roofs. */
public final class StructureParts {
    private StructureParts() {}

    /**
     * Add a pitched gable roof of stairs over a building footprint {@code [x0..x1]×[z0..z1]}: the ridge runs
     * along Z at the centre X column (a solid plank beam), the roof slopes down in X with stairs facing uphill
     * toward the ridge (so the eave edges taper thin), and the two gable ends (z0 / z1) are filled to the
     * roofline. {@code eaveY} is the lowest roof course (sit it on the wall top). Pass {@code ov = 1} for a
     * one-block overhang on every side — the building must be inset so {@code x0-1} and {@code z0-1} stay ≥ 0
     * (structure NBT can't hold negative positions) — or {@code ov = 0} for flush eaves.
     */
    public static void gableRoof(Map<BlockPos, BlockState> m, int x0, int x1, int z0, int z1, int eaveY,
                                 BlockState planks, Block stairs, int ov) {
        final int mid = (x0 + x1) / 2;
        final int ridgeY = eaveY + (mid - x0) + ov;
        for (int x = x0 - ov; x <= x1 + ov; x++) {
            final int ry = ridgeY - Math.abs(x - mid);
            final BlockState roof = x == mid ? planks
                    : stairs.defaultBlockState().setValue(StairBlock.FACING, x < mid ? Direction.EAST : Direction.WEST);
            for (int z = z0 - ov; z <= z1 + ov; z++) {
                m.put(new BlockPos(x, ry, z), roof);
            }
        }
        for (int x = x0; x <= x1; x++) {
            final int top = ridgeY - Math.abs(x - mid);
            for (int y = eaveY + 1; y < top; y++) {
                m.put(new BlockPos(x, y, z0), planks);
                m.put(new BlockPos(x, y, z1), planks);
            }
        }
    }

    /**
     * Place the "bottom" anchor jigsaw at {@code p} (the piece centres on the island here, then becomes
     * {@code finalState} after assembly). Call this LAST so a floor loop doesn't overwrite the jigsaw block.
     */
    public static void anchor(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> bes, BlockPos p, String finalState) {
        blocks.put(p, Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.DOWN_SOUTH));
        final CompoundTag t = new CompoundTag();
        t.putString("id", "minecraft:jigsaw");
        t.putString("name", "minecraft:bottom");
        t.putString("target", "minecraft:empty");
        t.putString("pool", "minecraft:empty");
        t.putString("final_state", finalState);
        t.putString("joint", "rollable");
        bes.put(p, t);
    }

    /** A chest block-entity bound to a vanilla loot table id (filled on first open). */
    public static CompoundTag lootChest(String lootTable) {
        final CompoundTag be = new CompoundTag();
        be.putString("id", "minecraft:chest");
        be.putString("LootTable", lootTable);
        return be;
    }

    /**
     * A brushable (suspicious sand/gravel) block-entity bound to an archaeology loot table — the player
     * brushes it for a single drop. Pair with a {@code minecraft:suspicious_sand}/{@code _gravel} block.
     */
    public static CompoundTag suspicious(String lootTable) {
        final CompoundTag be = new CompoundTag();
        be.putString("id", "minecraft:brushable_block");
        be.putString("LootTable", lootTable);
        return be;
    }
}
