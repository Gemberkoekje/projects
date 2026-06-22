package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.DispenserBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The jungle temple's puzzle room: a sealed mossy-cobblestone chamber with two loot chests
 * ({@code minecraft:chests/jungle_temple}) and an arrow dispenser set into the wall
 * ({@code minecraft:chests/jungle_temple_dispenser}), rigged to a tripwire across the room. The tripwire hooks
 * and string are baked as wool markers and swapped in by {@link Traps} after the jigsaw assembles (fragile
 * blocks pop on that path); tripping the wire powers the hook on the dispenser, which fires. The dispenser
 * loot fills on its first shot. See {@code SKYSTRUCTURESPLAN.md}.
 */
public final class JungleTempleTemplates {
    private JungleTempleTemplates() {}

    private record Built(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> blockEntities) {}

    public static void generateInto(Path dir) throws IOException {
        final Path file = dir.resolve("temple.nbt");
        if (!Files.exists(file)) {
            final Built b = temple();
            StructureWriter.write(b.blocks(), b.blockEntities(), file);
            Skyseed.LOGGER.info("[skyseed] generated structure template {}", file.getFileName());
        }
    }

    private static Built temple() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState mossy = Blocks.MOSSY_COBBLESTONE.defaultBlockState();
        final int maxX = 4, maxZ = 6, midX = 2;

        for (int x = 0; x <= maxX; x++) {
            for (int z = 0; z <= maxZ; z++) {
                m.put(new BlockPos(x, 0, z), mossy);      // floor
                m.put(new BlockPos(x, 4, z), mossy);      // roof
                final boolean perim = x == 0 || x == maxX || z == 0 || z == maxZ;
                if (perim) {
                    m.put(new BlockPos(x, 1, z), mossy);
                    m.put(new BlockPos(x, 2, z), mossy);
                    m.put(new BlockPos(x, 3, z), mossy);
                }
            }
        }
        // Doorway in the front (z=0) wall.
        m.remove(new BlockPos(midX, 1, 0));
        m.remove(new BlockPos(midX, 2, 0));

        // Two loot chests against the back wall, facing into the room.
        m.put(new BlockPos(1, 1, 5), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.NORTH));
        bes.put(new BlockPos(1, 1, 5), StructureParts.lootChest("minecraft:chests/jungle_temple"));
        m.put(new BlockPos(3, 1, 5), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.NORTH));
        bes.put(new BlockPos(3, 1, 5), StructureParts.lootChest("minecraft:chests/jungle_temple"));

        // An arrow dispenser set into the side wall, aimed across the room.
        m.put(new BlockPos(0, 1, 2), Blocks.DISPENSER.defaultBlockState().setValue(DispenserBlock.FACING, Direction.EAST));
        bes.put(new BlockPos(0, 1, 2), dispenser());

        // Tripwire trap along the dispenser's line of fire: a hook mounted on the dispenser, a string across the
        // room, and a hook on the far wall. Baked as wool markers and swapped to real tripwire by Traps after
        // assembly (fragile blocks pop on the jigsaw path). Trip the string → the hook fires the dispenser.
        m.put(new BlockPos(1, 1, 2), Blocks.RED_WOOL.defaultBlockState());  // → tripwire hook on the dispenser
        m.put(new BlockPos(2, 1, 2), Blocks.LIME_WOOL.defaultBlockState()); // → tripwire string
        m.put(new BlockPos(3, 1, 2), Blocks.RED_WOOL.defaultBlockState());  // → tripwire hook on the far wall

        StructureParts.anchor(m, bes, new BlockPos(midX, 0, 3), "minecraft:mossy_cobblestone");
        return new Built(m, bes);
    }

    private static CompoundTag dispenser() {
        final CompoundTag be = new CompoundTag();
        be.putString("id", "minecraft:dispenser");
        be.putString("LootTable", "minecraft:chests/jungle_temple_dispenser");
        return be;
    }
}
