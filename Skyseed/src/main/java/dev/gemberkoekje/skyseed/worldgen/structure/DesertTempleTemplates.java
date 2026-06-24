package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The desert temple's buried treasure chamber, like the original: anchored on its ROOF so it sits flush with
 * the (all-sandstone) island surface and the chamber hangs buried below, with a single hole in the roof centre
 * inviting a drop-in. Four chests ({@code minecraft:chests/desert_pyramid}) line the walls; a pressure plate
 * sits dead-centre on the floor over a single TNT block, directly under the hole — drop in carelessly and you land
 * on it. A blast-resistant obsidian floor sits below the charge so the blast can't punch the island's bottom out
 * (it's buried under the floor, so only shows once it goes off). The plate is baked as a wool marker and swapped in
 * by {@link Traps} after the jigsaw assembles (fragile blocks pop on that path); the interior is carved with air.
 * See {@code SKYSTRUCTURESPLAN.md}.
 */
public final class DesertTempleTemplates {
    private DesertTempleTemplates() {}

    public static void generateInto(Path dir) throws IOException {
        final Path file = dir.resolve("chamber.nbt");
        if (!Files.exists(file)) {
            final Built b = chamber();
            StructureWriter.write(b.blocks(), b.blockEntities(), file);
            Skyseed.LOGGER.info("[skyseed] generated structure template {}", file.getFileName());
        }
    }

    private static Built chamber() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState sandstone = Blocks.SANDSTONE.defaultBlockState();
        final BlockState cut = Blocks.CUT_SANDSTONE.defaultBlockState();
        final BlockState tnt = Blocks.TNT.defaultBlockState();
        final BlockState obsidian = Blocks.OBSIDIAN.defaultBlockState();
        final BlockState air = Blocks.AIR.defaultBlockState();
        final int max = 4, mid = 2;

        // The chamber is anchored on its ROOF (y5) so it lands flush with the island surface and hangs buried
        // below — like the original. The interior is carved with explicit air; only the roof's centre is open.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                final boolean centre = x == mid && z == mid;
                m.put(new BlockPos(x, 0, z), obsidian);                        // y0 blast-resistant floor under the trap
                m.put(new BlockPos(x, 1, z), centre ? tnt : sandstone);        // y1 floor; centre TNT = the single trigger
                for (int y = 2; y <= 4; y++) {
                    m.put(new BlockPos(x, y, z), perim ? cut : air);           // y2-4 walls, hollow interior
                }
                m.put(new BlockPos(x, 5, z), centre ? air : sandstone);        // y5 roof; a hole over the centre
            }
        }
        // The central pressure-plate trap, baked as a wool marker swapped in by Traps after assembly — it sits
        // on the floor's centre TNT directly under the roof hole. Drop in carelessly and you land on it: the
        // centre TNT fires (a single charge, over an obsidian floor so it can't void the island's bottom).
        // Disarm it (mine it from above) or pillar down past it.
        m.put(new BlockPos(mid, 2, mid), Blocks.YELLOW_WOOL.defaultBlockState());

        // Four treasure chests against the walls, facing in.
        m.put(new BlockPos(1, 2, mid), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.EAST));
        bes.put(new BlockPos(1, 2, mid), StructureParts.lootChest("minecraft:chests/desert_pyramid"));
        m.put(new BlockPos(3, 2, mid), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.WEST));
        bes.put(new BlockPos(3, 2, mid), StructureParts.lootChest("minecraft:chests/desert_pyramid"));
        m.put(new BlockPos(mid, 2, 1), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.SOUTH));
        bes.put(new BlockPos(mid, 2, 1), StructureParts.lootChest("minecraft:chests/desert_pyramid"));
        m.put(new BlockPos(mid, 2, 3), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.NORTH));
        bes.put(new BlockPos(mid, 2, 3), StructureParts.lootChest("minecraft:chests/desert_pyramid"));

        // Punch the shaft one more block up (y6) through whatever surface sits above the roof. When this temple
        // is sunk a block (the rare desert-island version), that surface is the island's own sand and only the
        // hole shows; when not sunk it lands a block above the surface (already air), so it's harmless. Every
        // other y6 cell is left absent, so the island's surface stays intact around the hole.
        m.put(new BlockPos(mid, 6, mid), air);

        // Anchor on a roof block one off the central hole (it can't be the hole), so the structure centres there.
        StructureParts.anchor(m, bes, new BlockPos(mid, 5, mid + 1), "minecraft:sandstone");
        return new Built(m, bes);
    }
}
