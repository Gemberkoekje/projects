package dev.gemberkoekje.skyseed.worldgen.structure;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * A bastion remnant: a 9×9 blackstone ruin with polished-blackstone-brick walls collapsed to a different height on
 * each side, gilded-blackstone and gold-block accents, a central lodestone-topped treasure plinth with a
 * {@code bastion_treasure} chest, a {@code bastion_other} chest in a corner, and a caged-corner <b>magma-cube
 * spawner</b>. Nether-native — piglins, a brute and magma cubes drift across it (the theme's {@code mobs}).
 *
 * <p>This is the first, "treasure"-flavoured variant; the planned Bridge / Housing variants will join it as more
 * weighted entries in the {@code skyseed:bastion/bastion} pool. See {@code SKYNETHERPLAN.md}.
 */
public final class BastionTemplates {
    private BastionTemplates() {}

    public static void generateInto(Path dir) throws IOException {
        StructureParts.writeIfAbsent(dir.resolve("bastion.nbt"), bastion());
    }

    private static Built bastion() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState bs = Blocks.BLACKSTONE.defaultBlockState();
        final BlockState brick = Blocks.POLISHED_BLACKSTONE_BRICKS.defaultBlockState();
        final BlockState gild = Blocks.GILDED_BLACKSTONE.defaultBlockState();
        final BlockState gold = Blocks.GOLD_BLOCK.defaultBlockState();
        final BlockState chain = Blocks.CHAIN.defaultBlockState();
        final int max = 8, mid = 4;

        // Floor: blackstone, with gilded-blackstone in the corners.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), bs);
            }
        }
        for (int[] c : new int[][] { {1, 1}, {7, 1}, {1, 7}, {7, 7} }) {
            m.put(new BlockPos(c[0], 0, c[1]), gild);
        }

        // Ruined perimeter wall: each side survives to a different height (north tallest, south nearly gone), with a
        // doorway in the north wall and a breach in the east — for the "remnant" look.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                if (x != 0 && x != max && z != 0 && z != max) {
                    continue;
                }
                int h;
                if (z == 0) {
                    h = 4;
                } else if (x == 0) {
                    h = 3;
                } else if (x == max) {
                    h = 2;
                } else {
                    h = 1;
                }
                if (z == 0 && (x == 3 || x == 4)) {
                    h = 0; // north doorway
                }
                if (x == max && z == 5) {
                    h = 0; // east breach
                }
                for (int y = 1; y <= h; y++) {
                    m.put(new BlockPos(x, y, z), brick);
                }
            }
        }

        // Central treasure plinth: chiseled blackstone topped with a lodestone, a gold block beside it, and the
        // bastion_treasure chest.
        m.put(new BlockPos(mid, 1, mid), Blocks.CHISELED_POLISHED_BLACKSTONE.defaultBlockState());
        m.put(new BlockPos(mid, 2, mid), Blocks.LODESTONE.defaultBlockState());
        m.put(new BlockPos(mid - 1, 1, mid), gold);
        m.put(new BlockPos(mid + 1, 1, mid), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.WEST));
        bes.put(new BlockPos(mid + 1, 1, mid), StructureParts.lootChest("minecraft:chests/bastion_treasure"));

        // A magma-cube spawner in the (wall-shielded) north-west corner — the treasure's guardian.
        m.put(new BlockPos(2, 1, 2), Blocks.SPAWNER.defaultBlockState());
        bes.put(new BlockPos(2, 1, 2), StructureParts.mobSpawner("minecraft:magma_cube"));

        // A bastion_other chest tucked in the far corner.
        m.put(new BlockPos(6, 1, 6), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.NORTH));
        bes.put(new BlockPos(6, 1, 6), StructureParts.lootChest("minecraft:chests/bastion_other"));

        // Chains hanging off the tall north wall, gilded accents up high.
        m.put(new BlockPos(2, 3, 1), chain);
        m.put(new BlockPos(6, 3, 1), chain);
        m.put(new BlockPos(1, 2, 0), gild);
        m.put(new BlockPos(7, 2, 0), gild);

        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:blackstone");
        return new Built(m, bes);
    }
}
