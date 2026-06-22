package dev.gemberkoekje.skyseed.worldgen;

import net.minecraft.world.level.block.state.BlockState;

/** A resolved surface-scatter choice: a block and the chance it replaces a column's default surface. */
record Scatter(BlockState state, float chance) {}
