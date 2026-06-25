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
 * Bastion remnant structures — three weighted variants in the {@code skyseed:bastion/bastion} pool, mirroring the
 * vanilla bastion types:
 * <ul>
 *   <li><b>Treasure</b> — a blackstone ruin with a lodestone treasure plinth, a {@code bastion_treasure} chest and a
 *       caged magma-cube spawner.</li>
 *   <li><b>Bridge</b> — a raised blackstone bridge over a lava channel, with brick railings and a small stable
 *       platform holding a {@code bastion_other} chest.</li>
 *   <li><b>Housing</b> — a walled blackstone unit partitioned into rooms around a central chamber with a
 *       {@code bastion_other} chest.</li>
 * </ul>
 * Piglins, a brute, hoglins and magma cubes drift across whichever lands (the theme's {@code mobs}).
 * A fourth, more ruined {@link #remnant()} lives in its own {@code skyseed:bastion/remnant} pool: the rare-structure
 * roll on the bastion-biome Large Nether seeds (crying obsidian + cracked polished blackstone), distinct from the
 * dedicated seed's pool. See SKYNETHERPLAN.
 */
public final class BastionTemplates {
    private BastionTemplates() {}

    public static void generateInto(Path dir) throws IOException {
        StructureParts.writeIfAbsent(dir.resolve("treasure.nbt"), treasure());
        StructureParts.writeIfAbsent(dir.resolve("bridge.nbt"), bridge());
        StructureParts.writeIfAbsent(dir.resolve("housing.nbt"), housing());
        StructureParts.writeIfAbsent(dir.resolve("remnant.nbt"), remnant());
    }

    /** A 9×9 ruin with a lodestone treasure plinth, a caged magma-cube spawner and the best loot. */
    private static Built treasure() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState bs = Blocks.BLACKSTONE.defaultBlockState();
        final BlockState brick = Blocks.POLISHED_BLACKSTONE_BRICKS.defaultBlockState();
        final BlockState gild = Blocks.GILDED_BLACKSTONE.defaultBlockState();
        final BlockState gold = Blocks.GOLD_BLOCK.defaultBlockState();
        final BlockState chain = Blocks.CHAIN.defaultBlockState();
        final int max = 8, mid = 4;

        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), bs);
            }
        }
        for (int[] c : new int[][] { {1, 1}, {7, 1}, {1, 7}, {7, 7} }) {
            m.put(new BlockPos(c[0], 0, c[1]), gild);
        }
        // Ruined perimeter wall: north tallest, south nearly gone, with a doorway and a breach.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                if (x != 0 && x != max && z != 0 && z != max) {
                    continue;
                }
                int h = z == 0 ? 4 : x == 0 ? 3 : x == max ? 2 : 1;
                if (z == 0 && (x == 3 || x == 4)) {
                    h = 0;
                }
                if (x == max && z == 5) {
                    h = 0;
                }
                for (int y = 1; y <= h; y++) {
                    m.put(new BlockPos(x, y, z), brick);
                }
            }
        }
        m.put(new BlockPos(mid, 1, mid), Blocks.CHISELED_POLISHED_BLACKSTONE.defaultBlockState());
        m.put(new BlockPos(mid, 2, mid), Blocks.LODESTONE.defaultBlockState());
        m.put(new BlockPos(mid - 1, 1, mid), gold);
        m.put(new BlockPos(mid + 1, 1, mid), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.WEST));
        bes.put(new BlockPos(mid + 1, 1, mid), StructureParts.lootChest("minecraft:chests/bastion_treasure"));
        m.put(new BlockPos(2, 1, 2), Blocks.SPAWNER.defaultBlockState());
        bes.put(new BlockPos(2, 1, 2), StructureParts.mobSpawner("minecraft:magma_cube"));
        m.put(new BlockPos(6, 1, 6), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.NORTH));
        bes.put(new BlockPos(6, 1, 6), StructureParts.lootChest("minecraft:chests/bastion_other"));
        m.put(new BlockPos(2, 3, 1), chain);
        m.put(new BlockPos(6, 3, 1), chain);
        m.put(new BlockPos(1, 2, 0), gild);
        m.put(new BlockPos(7, 2, 0), gild);

        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:blackstone");
        return new Built(m, bes);
    }

    /** A raised blackstone bridge (11 long) on pillars over a lava channel, brick railings, a stable end with loot. */
    private static Built bridge() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState bs = Blocks.BLACKSTONE.defaultBlockState();
        final BlockState brick = Blocks.POLISHED_BLACKSTONE_BRICKS.defaultBlockState();
        final BlockState gild = Blocks.GILDED_BLACKSTONE.defaultBlockState();
        final BlockState gold = Blocks.GOLD_BLOCK.defaultBlockState();
        final int deck = 3; // deck height
        final int maxX = 10, midZ = 3;

        // Lava channel along the bottom centre, walled by blackstone so it stays put.
        for (int x = 0; x <= maxX; x++) {
            for (int dz = -1; dz <= 1; dz++) {
                m.put(new BlockPos(x, 0, midZ + dz), dz == 0 ? Blocks.LAVA.defaultBlockState() : bs);
            }
        }
        // Three support pillars up to the deck.
        for (int px : new int[] { 1, 5, 9 }) {
            for (int y = 1; y < deck; y++) {
                m.put(new BlockPos(px, y, midZ), bs);
            }
        }
        // The 3-wide deck, with gold/gilded accents and a ruined gap.
        for (int x = 0; x <= maxX; x++) {
            for (int dz = -1; dz <= 1; dz++) {
                if (x == 6 && dz != 0) {
                    continue; // a broken edge
                }
                BlockState top = bs;
                if (x == 5 && dz == 0) {
                    top = gold;
                } else if ((x == 2 || x == 8) && dz == 0) {
                    top = gild;
                }
                m.put(new BlockPos(x, deck, midZ + dz), top);
            }
        }
        // Brick railings along both deck edges, ruined in places.
        for (int x = 0; x <= maxX; x++) {
            if (x == 4 || x == 7) {
                continue;
            }
            m.put(new BlockPos(x, deck + 1, midZ - 1), brick);
            m.put(new BlockPos(x, deck + 1, midZ + 1), brick);
        }
        // A stable platform at the +X end: a wider blackstone landing with crimson nylium/roots and a bastion_other chest.
        for (int x = maxX - 2; x <= maxX; x++) {
            for (int z = midZ - 2; z <= midZ + 2; z++) {
                m.put(new BlockPos(x, deck, z), bs);
            }
        }
        m.put(new BlockPos(maxX - 1, deck + 1, midZ - 1), Blocks.CRIMSON_NYLIUM.defaultBlockState());
        m.put(new BlockPos(maxX - 1, deck + 2, midZ - 1), Blocks.CRIMSON_ROOTS.defaultBlockState());
        m.put(new BlockPos(maxX, deck + 1, midZ + 1),
                Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.WEST));
        bes.put(new BlockPos(maxX, deck + 1, midZ + 1), StructureParts.lootChest("minecraft:chests/bastion_other"));

        StructureParts.anchor(m, bes, new BlockPos(5, 0, midZ), "minecraft:blackstone");
        return new Built(m, bes);
    }

    /** A 9×9 walled unit partitioned into rooms around a central chamber with a bastion_other chest. */
    private static Built housing() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState bs = Blocks.BLACKSTONE.defaultBlockState();
        final BlockState brick = Blocks.POLISHED_BLACKSTONE_BRICKS.defaultBlockState();
        final BlockState gild = Blocks.GILDED_BLACKSTONE.defaultBlockState();
        final BlockState gold = Blocks.GOLD_BLOCK.defaultBlockState();
        final int max = 8, mid = 4;

        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), bs);
            }
        }
        // Ruined outer walls with a doorway on +X.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                if (x != 0 && x != max && z != 0 && z != max) {
                    continue;
                }
                int h = x == max ? 2 : 3;
                if (x == max && z == mid) {
                    h = 0; // doorway
                }
                for (int y = 1; y <= h; y++) {
                    m.put(new BlockPos(x, y, z), brick);
                }
            }
        }
        // Two offset interior partition stubs (rooms), kept clear of the centre.
        for (int z = 1; z <= 3; z++) {
            for (int y = 1; y <= 2; y++) {
                m.put(new BlockPos(2, y, z), brick);
            }
        }
        for (int z = 5; z <= 7; z++) {
            for (int y = 1; y <= 2; y++) {
                m.put(new BlockPos(6, y, z), brick);
            }
        }
        // Central loot: a bastion_other chest on the floor (reachable from the north), flanked by gold, with gilded
        // accents in two corners.
        m.put(new BlockPos(mid, 1, mid), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.NORTH));
        bes.put(new BlockPos(mid, 1, mid), StructureParts.lootChest("minecraft:chests/bastion_other"));
        m.put(new BlockPos(mid + 1, 1, mid), gold);
        m.put(new BlockPos(2, 0, 6), gild);
        m.put(new BlockPos(6, 0, 2), gild);

        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:blackstone");
        return new Built(m, bes);
    }

    /**
     * A ruined bastion <b>remnant</b> for the rare-structure roll (its own {@code skyseed:bastion/remnant} pool, not
     * part of the dedicated seed's treasure/bridge/housing): a crumbling rampart of cracked polished-blackstone
     * bricks on a polished-blackstone floor, with weeping {@code crying_obsidian} corner buttresses and a wept
     * central shard, gilded/gold accents, a caged magma-cube spawner and a {@code bastion_other} chest. A piglin
     * garrison spawns in (the rare structure's {@code mobs} pack). Open-topped, so the garrison stands on the floor.
     */
    private static Built remnant() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState floor = Blocks.POLISHED_BLACKSTONE.defaultBlockState();
        final BlockState crying = Blocks.CRYING_OBSIDIAN.defaultBlockState();
        final BlockState gild = Blocks.GILDED_BLACKSTONE.defaultBlockState();
        final BlockState gold = Blocks.GOLD_BLOCK.defaultBlockState();
        final int max = 8, mid = 4;

        // Polished-blackstone floor.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), floor);
            }
        }
        // Crumbling rampart of cracked polished-blackstone bricks: north wall tallest, tapering to a near-gone south,
        // with crying-obsidian corner buttresses, a north gateway breach and a wholly collapsed east span.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                if (x != 0 && x != max && z != 0 && z != max) {
                    continue;
                }
                int h = z == 0 ? 5 : x == 0 ? 4 : x == max ? 3 : 2;
                if (z == 0 && (x == 3 || x == 4)) {
                    h = 1; // gateway breach
                }
                if (x == max && z >= 4 && z <= 5) {
                    h = 0; // a collapsed span
                }
                final boolean corner = (x == 0 || x == max) && (z == 0 || z == max);
                for (int y = 1; y <= h; y++) {
                    m.put(new BlockPos(x, y, z), corner ? crying : ruinedBrick(x, y + z));
                }
            }
        }
        // The wept heart: a chiseled dais under a crying-obsidian shard, flanked by gold and gilded blackstone.
        m.put(new BlockPos(mid, 1, mid), Blocks.CHISELED_POLISHED_BLACKSTONE.defaultBlockState());
        m.put(new BlockPos(mid, 2, mid), crying);
        m.put(new BlockPos(mid - 1, 1, mid), gold);
        m.put(new BlockPos(mid + 1, 1, mid), gild);
        // A caged magma-cube spawner chained over one corner; a bastion chest in another.
        m.put(new BlockPos(2, 1, 2), Blocks.SPAWNER.defaultBlockState());
        bes.put(new BlockPos(2, 1, 2), StructureParts.mobSpawner("minecraft:magma_cube"));
        m.put(new BlockPos(2, 2, 2), Blocks.CHAIN.defaultBlockState());
        m.put(new BlockPos(6, 1, 6), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.NORTH));
        bes.put(new BlockPos(6, 1, 6), StructureParts.lootChest("minecraft:chests/bastion_other"));
        // Fallen rubble: a gilded chunk and a crying-obsidian shard knocked loose into the yard.
        m.put(new BlockPos(6, 1, 2), gild);
        m.put(new BlockPos(2, 1, 6), crying);

        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:polished_blackstone");
        return new Built(m, bes);
    }

    /** A deterministic cracked/intact polished-blackstone-brick mix for the ruined rampart (mostly cracked). */
    private static BlockState ruinedBrick(int a, int b) {
        return Math.floorMod(a * 7 + b * 5, 3) == 0
                ? Blocks.POLISHED_BLACKSTONE_BRICKS.defaultBlockState()
                : Blocks.CRACKED_POLISHED_BLACKSTONE_BRICKS.defaultBlockState();
    }
}
