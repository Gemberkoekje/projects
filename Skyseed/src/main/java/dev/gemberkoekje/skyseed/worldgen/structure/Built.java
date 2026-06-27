package dev.gemberkoekje.skyseed.worldgen.structure;

import net.minecraft.core.BlockPos;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.state.BlockState;

import java.util.Map;

/**
 * A code-authored structure template: its block states, per-position block-entity NBT, and (optionally) baked entities
 * keyed by their cell (e.g. a chest minecart on a mineshaft rail). Most pieces have no entities — the two-arg form
 * defaults them to none.
 */
public record Built(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> blockEntities,
                    Map<BlockPos, CompoundTag> entities) {
    public Built(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> blockEntities) {
        this(blocks, blockEntities, Map.of());
    }
}
