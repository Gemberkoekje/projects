package dev.gemberkoekje.skyseed.worldgen.structure;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.LanternBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The Piglin Trading Post — the Nether's "village". A single blackstone hall: an open-top colonnade (walls with
 * window arches and doorways, rafter beams and hanging lanterns overhead) around a central gold "trade pillar", with
 * gilded-blackstone accents and two bastion loot chests. The open top lets the theme's piglins spawn on the floor
 * inside; drop a gold ingot near one and it barters (vanilla behaviour). See SKYNETHERPLAN.
 */
public final class PiglinTradingPostTemplates {
    private PiglinTradingPostTemplates() {}

    public static void generateInto(Path dir) throws IOException {
        StructureParts.writeIfAbsent(dir.resolve("trading_post.nbt"), hall());
    }

    private static Built hall() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState bs = Blocks.BLACKSTONE.defaultBlockState();
        final BlockState brick = Blocks.POLISHED_BLACKSTONE_BRICKS.defaultBlockState();
        final BlockState gold = Blocks.GOLD_BLOCK.defaultBlockState();
        final BlockState gild = Blocks.GILDED_BLACKSTONE.defaultBlockState();
        final BlockState glow = Blocks.GLOWSTONE.defaultBlockState();
        final BlockState hangingLantern = Blocks.LANTERN.defaultBlockState().setValue(LanternBlock.HANGING, Boolean.TRUE);
        final int maxX = 10, maxZ = 8, midX = 5, midZ = 4, wallTop = 3;

        // Floor.
        for (int x = 0; x <= maxX; x++) {
            for (int z = 0; z <= maxZ; z++) {
                m.put(new BlockPos(x, 0, z), bs);
            }
        }
        // Perimeter walls: doorways on the short sides, window arches on the long sides, two glowing wall blocks.
        for (int x = 0; x <= maxX; x++) {
            for (int z = 0; z <= maxZ; z++) {
                if (x != 0 && x != maxX && z != 0 && z != maxZ) {
                    continue;
                }
                for (int y = 1; y <= wallTop; y++) {
                    if ((x == 0 || x == maxX) && z == midZ && y <= 2) {
                        continue; // doorway
                    }
                    if ((z == 0 || z == maxZ) && y == 2 && (x == 2 || x == 4 || x == 6 || x == 8)) {
                        continue; // window arches
                    }
                    final boolean glowSpot = (z == 0 || z == maxZ) && y == 2 && x == midX;
                    m.put(new BlockPos(x, y, z), glowSpot ? glow : brick);
                }
            }
        }
        // Rafter beams across the hall (open top — piglins spawn on the floor below), with hanging lanterns.
        for (int rx : new int[] { 2, midX, 8 }) {
            for (int z = 0; z <= maxZ; z++) {
                m.put(new BlockPos(rx, wallTop + 1, z), brick);
            }
        }
        m.put(new BlockPos(2, wallTop, midZ), hangingLantern);
        m.put(new BlockPos(8, wallTop, midZ), hangingLantern);
        // Central gold "trade pillar" — the gold-economy hub, lit from the top.
        m.put(new BlockPos(midX, 1, midZ), gild);
        m.put(new BlockPos(midX, 2, midZ), gold);
        m.put(new BlockPos(midX, wallTop, midZ), glow);
        // Gilded-blackstone floor accents.
        for (int[] g : new int[][] { {2, 2}, {8, 2}, {2, 6}, {8, 6} }) {
            m.put(new BlockPos(g[0], 0, g[1]), gild);
        }
        // Two bastion loot chests — the post's stock.
        m.put(new BlockPos(2, 1, 1), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.SOUTH));
        bes.put(new BlockPos(2, 1, 1), StructureParts.lootChest("minecraft:chests/bastion_other"));
        m.put(new BlockPos(8, 1, maxZ - 1), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.NORTH));
        bes.put(new BlockPos(8, 1, maxZ - 1), StructureParts.lootChest("minecraft:chests/bastion_other"));

        StructureParts.anchor(m, bes, new BlockPos(midX, 0, midZ), "minecraft:blackstone");
        return new Built(m, bes);
    }
}
