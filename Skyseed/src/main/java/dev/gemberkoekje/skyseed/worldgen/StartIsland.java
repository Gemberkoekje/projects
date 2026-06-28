package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.compat.Id;
import dev.gemberkoekje.skyseed.compat.Lookup;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.LeavesBlock;
import net.minecraft.world.level.block.entity.ChestBlockEntity;
import net.minecraft.world.level.block.state.BlockState;

/**
 * The curated starting island (README → World & progression setup): hand-authored block-by-block so it is soft-lock-proof by
 * construction — a small grass/dirt teardrop with one guaranteed oak tree. From this a player can
 * always craft the first (2×2, no table) Forest Skyseed: chop the tree → planks, dig the island →
 * dirt. Deliberately NOT procedural, so it can never roll without wood.
 */
public final class StartIsland {
    private static final int FLAGS = Block.UPDATE_CLIENTS;

    private StartIsland() {}

    /**
     * Builds the island centred on {@code center} (its grass surface) and returns the spawn pos on top.
     * If {@code bonusChest} is set (the vanilla "Generate Bonus Chest" world option), a starter chest is placed
     * on the grass beside the spawn with a small leg-up kit.
     */
    public static BlockPos build(ServerLevel level, BlockPos center, boolean bonusChest) {
        final BlockState grass = Blocks.GRASS_BLOCK.defaultBlockState();
        final BlockState dirt = Blocks.DIRT.defaultBlockState();
        final BlockState stone = Blocks.STONE.defaultBlockState();
        final BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();

        // Layered teardrop: grass top, dirt body, a small stone tip — radius shrinks with depth.
        layer(level, center, 0, 3, grass, pos);
        layer(level, center, -1, 3, dirt, pos);
        layer(level, center, -2, 3, dirt, pos);
        layer(level, center, -3, 2, dirt, pos);
        layer(level, center, -4, 1, stone, pos);

        // A guaranteed oak, offset from the spawn point so the player doesn't stand in the trunk.
        buildOak(level, center.offset(2, 0, 2), pos);

        if (bonusChest) {
            placeBonusChest(level, center);
        }

        return center.above(); // stand on the grass at the centre
    }

    /** A starter chest on the grass just beside the spawn: a wooden tool set, torches, and a little food + fuel. */
    private static void placeBonusChest(ServerLevel level, BlockPos center) {
        final BlockPos chestPos = center.offset(-1, 1, 0); // on the grass, one block west of where the player stands
        level.setBlock(chestPos, Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.EAST), FLAGS);
        if (level.getBlockEntity(chestPos) instanceof ChestBlockEntity chest) {
            int s = 0;
            chest.setItem(s++, new ItemStack(Items.WOODEN_SWORD));
            chest.setItem(s++, new ItemStack(Items.WOODEN_PICKAXE));
            chest.setItem(s++, new ItemStack(Items.WOODEN_AXE));
            chest.setItem(s++, new ItemStack(Items.WOODEN_SHOVEL));
            chest.setItem(s++, new ItemStack(Items.WOODEN_HOE));
            chest.setItem(s++, new ItemStack(Items.TORCH, 16));
            chest.setItem(s++, new ItemStack(Items.APPLE, 6));
            chest.setItem(s++, new ItemStack(Items.BREAD, 4));
            chest.setItem(s++, new ItemStack(Items.OAK_LOG, 8));
            chest.setItem(s, new ItemStack(Items.COAL, 4));
        }
    }

    private static void layer(ServerLevel level, BlockPos center, int dy, int radius, BlockState state,
                              BlockPos.MutableBlockPos pos) {
        final int r2 = radius * radius + 1; // +1 rounds the corners
        for (int dx = -radius; dx <= radius; dx++) {
            for (int dz = -radius; dz <= radius; dz++) {
                if (dx * dx + dz * dz <= r2) {
                    level.setBlock(pos.set(center.getX() + dx, center.getY() + dy, center.getZ() + dz), state, FLAGS);
                }
            }
        }
    }

    private static void buildOak(ServerLevel level, BlockPos ground, BlockPos.MutableBlockPos pos) {
        final BlockPos treePos = ground.above();
        level.setBlock(treePos, Blocks.AIR.defaultBlockState(), FLAGS); // clear the spot for the feature
        // A real Minecraft oak feature places natural, distance-tracked leaves (persistent=false), so a fast-leaf-decay
        // mod treats it like any other tree — unlike the old hand-placed PERSISTENT leaves, which never decayed.
        final var oak = Lookup.configuredFeature(level.registryAccess(), Id.of("minecraft:oak"));
        if (oak.isPresent()
                && oak.get().place(level, level.getChunkSource().getGenerator(), level.getRandom(), treePos)) {
            return;
        }

        // Defensive fallback (the curated island must never roll without wood — vanilla's oak feature is reliable on
        // a cleared grass+air spot, so this is essentially never hit): the old hand-built persistent oak.
        final BlockState log = Blocks.OAK_LOG.defaultBlockState();
        final BlockState leaves = Blocks.OAK_LEAVES.defaultBlockState().setValue(LeavesBlock.PERSISTENT, Boolean.TRUE);
        final int gx = ground.getX(), gy = ground.getY(), gz = ground.getZ();

        // Trunk: 5 logs above the ground (gy is the grass block).
        for (int i = 1; i <= 5; i++) {
            level.setBlock(pos.set(gx, gy + i, gz), log, FLAGS);
        }
        // Canopy: two 5x5 rings (corners trimmed) around the top logs, a 3x3, then a plus cap.
        leafSquare(level, gx, gy + 4, gz, 2, leaves, pos);
        leafSquare(level, gx, gy + 5, gz, 2, leaves, pos);
        leafSquare(level, gx, gy + 6, gz, 1, leaves, pos);
        level.setBlock(pos.set(gx, gy + 7, gz), leaves, FLAGS);
        level.setBlock(pos.set(gx + 1, gy + 7, gz), leaves, FLAGS);
        level.setBlock(pos.set(gx - 1, gy + 7, gz), leaves, FLAGS);
        level.setBlock(pos.set(gx, gy + 7, gz + 1), leaves, FLAGS);
        level.setBlock(pos.set(gx, gy + 7, gz - 1), leaves, FLAGS);
    }

    /** A filled leaf square of the given radius, skipping the trunk column and the far corners. */
    private static void leafSquare(ServerLevel level, int gx, int y, int gz, int radius, BlockState leaves,
                                   BlockPos.MutableBlockPos pos) {
        for (int dx = -radius; dx <= radius; dx++) {
            for (int dz = -radius; dz <= radius; dz++) {
                if (dx == 0 && dz == 0) {
                    continue; // trunk
                }
                if (Math.abs(dx) == radius && Math.abs(dz) == radius) {
                    continue; // trim corners
                }
                level.setBlock(pos.set(gx + dx, y, gz + dz), leaves, FLAGS);
            }
        }
    }
}
