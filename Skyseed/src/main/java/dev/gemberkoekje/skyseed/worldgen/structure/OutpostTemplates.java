package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.FrontAndTop;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.JigsawBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The Pillager Outpost: a dark-oak watchtower (the vanilla outpost translated to a floating island — just the
 * tower). Three floors up a corner ladder; a fenced cage at the base holds the iron golem (spawned at the
 * jigsaw origin via the theme's {@code iron_golems}, so it lands inside), and the top floor carries a pillager
 * spawner + a {@code minecraft:chests/pillager_outpost} chest. Fight your way up; free the golem and it turns
 * on the pillagers. See {@code SKYSTRUCTURESPLAN.md}.
 */
public final class OutpostTemplates {
    private OutpostTemplates() {}

    private static final BlockState LOG = Blocks.DARK_OAK_LOG.defaultBlockState();
    private static final BlockState PLANK = Blocks.DARK_OAK_PLANKS.defaultBlockState();
    private static final BlockState FENCE = Blocks.DARK_OAK_FENCE.defaultBlockState();
    private static final BlockState AIR = Blocks.AIR.defaultBlockState();

    private record Built(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> blockEntities) {}

    public static void generateInto(Path dir) throws IOException {
        final Path file = dir.resolve("tower.nbt");
        if (!Files.exists(file)) {
            final Built b = tower();
            StructureWriter.write(b.blocks(), b.blockEntities(), file);
            Skyseed.LOGGER.info("[skyseed] generated structure template {}", file.getFileName());
        }
    }

    private static Built tower() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int max = 4, mid = 2;           // 5×5 footprint, 3×3 interior
        final int[] floors = {0, 4, 8};       // floor slabs; interiors y1-3, y5-7, y9-11
        final int roofY = 12;

        // Shell: dark-oak log corners + plank walls from the base up to the roofline.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                final boolean corner = (x == 0 || x == max) && (z == 0 || z == max);
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                m.put(new BlockPos(x, 0, z), PLANK);     // base floor
                m.put(new BlockPos(x, roofY, z), PLANK);  // roof
                if (perim) {
                    for (int y = 1; y < roofY; y++) {
                        m.put(new BlockPos(x, y, z), corner ? LOG : PLANK);
                    }
                }
            }
        }
        // Interior floor slabs (skip the base) with a ladder hole at corner (1,1).
        for (int fi = 1; fi < floors.length; fi++) {
            for (int x = 1; x < max; x++) {
                for (int z = 1; z < max; z++) {
                    m.put(new BlockPos(x, floors[fi], z), (x == 1 && z == 1) ? AIR : PLANK);
                }
            }
        }

        // Arrow slits: a one-block gap mid-wall on each side, on every floor.
        for (final int fy : new int[]{2, 6, 10}) {
            m.put(new BlockPos(mid, fy, 0), AIR);
            m.put(new BlockPos(mid, fy, max), AIR);
            m.put(new BlockPos(0, fy, mid), AIR);
            m.put(new BlockPos(max, fy, mid), AIR);
        }

        // Ladder up corner (1,1), fixed to the north (z=0) wall, holes already cut through each floor.
        final BlockState ladder = Blocks.LADDER.defaultBlockState().setValue(BlockStateProperties.HORIZONTAL_FACING, Direction.SOUTH);
        for (int y = 1; y <= 11; y++) {
            m.put(new BlockPos(1, y, 1), ladder);
        }

        // Ground floor: the golem cage — a 3-tall dark-oak fence box around the centre column (golem spawns
        // here, at the jigsaw origin), and an entrance doorway in the east wall leading to the open corner.
        for (int y = 1; y <= 3; y++) {
            m.put(new BlockPos(mid - 1, y, mid), FENCE);
            m.put(new BlockPos(mid + 1, y, mid), FENCE);
            m.put(new BlockPos(mid, y, mid - 1), FENCE);
            m.put(new BlockPos(mid, y, mid + 1), FENCE);
        }
        m.put(new BlockPos(max, 1, 3), AIR); // doorway
        m.put(new BlockPos(max, 2, 3), AIR);

        // Top floor: pillager spawner on the centre of the upper floor, a loot chest beside it.
        m.put(new BlockPos(mid, 9, mid), Blocks.SPAWNER.defaultBlockState());
        bes.put(new BlockPos(mid, 9, mid), spawner("minecraft:pillager"));
        m.put(new BlockPos(3, 9, 3), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.WEST));
        bes.put(new BlockPos(3, 9, 3), lootChest("minecraft:chests/pillager_outpost"));

        // Battlement: a low log parapet ringing the roof.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                if ((x == 0 || x == max || z == 0 || z == max) && (x + z) % 2 == 0) {
                    m.put(new BlockPos(x, roofY + 1, z), LOG);
                }
            }
        }

        anchor(m, bes, new BlockPos(mid, 0, mid));
        return new Built(m, bes);
    }

    private static void anchor(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockPos p) {
        m.put(p, Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.DOWN_SOUTH));
        final CompoundTag t = new CompoundTag();
        t.putString("id", "minecraft:jigsaw");
        t.putString("name", "minecraft:bottom");
        t.putString("target", "minecraft:empty");
        t.putString("pool", "minecraft:empty");
        t.putString("final_state", "minecraft:dark_oak_planks");
        t.putString("joint", "rollable");
        bes.put(p, t);
    }

    private static CompoundTag spawner(String mobId) {
        final CompoundTag entity = new CompoundTag();
        entity.putString("id", mobId);
        final CompoundTag spawnData = new CompoundTag();
        spawnData.put("entity", entity.copy());
        final CompoundTag potData = new CompoundTag();
        potData.put("entity", entity.copy());
        final CompoundTag potential = new CompoundTag();
        potential.putInt("weight", 1);
        potential.put("data", potData);
        final ListTag potentials = new ListTag();
        potentials.add(potential);
        final CompoundTag be = new CompoundTag();
        be.putString("id", "minecraft:mob_spawner");
        be.put("SpawnData", spawnData);
        be.put("SpawnPotentials", potentials);
        return be;
    }

    private static CompoundTag lootChest(String table) {
        final CompoundTag be = new CompoundTag();
        be.putString("id", "minecraft:chest");
        be.putString("LootTable", table);
        return be;
    }
}
