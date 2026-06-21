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

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * Code-authored Dungeon rooms, generated to {@code .nbt} at dev time (see {@link DevStructureGenerator}).
 * A sealed 5×5×5 cobblestone cube — a vanilla mob spawner in the centre and two loot chests on opposite
 * walls (the vanilla {@code minecraft:chests/simple_dungeon} table). Three weighted variants differ only in
 * the spawner's mob, exactly like a vanilla dungeon. Sealed so the interior stays dark and the spawner runs;
 * the {@code hamlet_weathering} processor mosses the cobble. See {@code SKYSTRUCTURESPLAN.md}.
 */
public final class DungeonTemplates {
    private DungeonTemplates() {}

    private record Built(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> blockEntities) {}

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("room_zombie.nbt"), room("minecraft:zombie"));
        writeIfAbsent(dir.resolve("room_skeleton.nbt"), room("minecraft:skeleton"));
        writeIfAbsent(dir.resolve("room_spider.nbt"), room("minecraft:spider"));
    }

    private static void writeIfAbsent(Path file, Built b) throws IOException {
        if (!Files.exists(file)) {
            StructureWriter.write(b.blocks(), b.blockEntities(), file);
            Skyseed.LOGGER.info("[skyseed] generated structure template {}", file.getFileName());
        }
    }

    private static void anchor(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockPos p) {
        m.put(p, Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.DOWN_SOUTH));
        final CompoundTag t = new CompoundTag();
        t.putString("id", "minecraft:jigsaw");
        t.putString("name", "minecraft:bottom");
        t.putString("target", "minecraft:empty");
        t.putString("pool", "minecraft:empty");
        t.putString("final_state", "minecraft:cobblestone");
        t.putString("joint", "rollable");
        bes.put(p, t);
    }

    /** A vanilla-style mob-spawner block entity bound to one mob id. */
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

    private static CompoundTag lootChest() {
        final CompoundTag be = new CompoundTag();
        be.putString("id", "minecraft:chest");
        be.putString("LootTable", "minecraft:chests/simple_dungeon");
        return be;
    }

    /** A sealed 5×5×5 cobblestone cube with a centre spawner and two loot chests. */
    private static Built room(String mobId) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState cobble = Blocks.COBBLESTONE.defaultBlockState();
        final int max = 4, mid = 2;
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                for (int y = 0; y <= max; y++) {
                    final boolean shell = x == 0 || x == max || z == 0 || z == max || y == 0 || y == max;
                    if (shell) {
                        m.put(new BlockPos(x, y, z), cobble);
                    }
                }
            }
        }
        // Spawner in the centre of the floor.
        m.put(new BlockPos(mid, 1, mid), Blocks.SPAWNER.defaultBlockState());
        bes.put(new BlockPos(mid, 1, mid), spawner(mobId));
        // A loot chest against each side wall, facing inward.
        m.put(new BlockPos(1, 1, mid), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.EAST));
        bes.put(new BlockPos(1, 1, mid), lootChest());
        m.put(new BlockPos(3, 1, mid), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.WEST));
        bes.put(new BlockPos(3, 1, mid), lootChest());
        // Anchor (becomes cobblestone) centred on the floor underside.
        anchor(m, bes, new BlockPos(mid, 0, mid));
        return new Built(m, bes);
    }
}
