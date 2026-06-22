package dev.gemberkoekje.skyseed.worldgen.structure;

import net.minecraft.core.BlockPos;
import net.minecraft.core.FrontAndTop;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.JigsawBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.util.Map;

/** Shared building blocks for the code-authored structure-island templates: the jigsaw anchor and loot chests. */
public final class StructureParts {
    private StructureParts() {}

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
