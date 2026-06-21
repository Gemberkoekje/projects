package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.FrontAndTop;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.FenceGateBlock;
import net.minecraft.world.level.block.JigsawBlock;
import net.minecraft.world.level.block.LanternBlock;
import net.minecraft.world.level.block.LayeredCauldronBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * Code-authored Animal Island enclosures, generated to {@code .nbt} at dev time (see {@link DevStructureGenerator}).
 * Each is a jigsaw single-piece (a "bottom" anchor centres it on the levelled pad); the animals themselves are
 * rolled and spawned separately from the theme's {@code animals} packs. See {@code SKYANIMALSPLAN.md}.
 */
public final class AnimalTemplates {
    private AnimalTemplates() {}

    private record Built(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> blockEntities) {}

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("pasture_pen.nbt"), pasturePen());
        writeIfAbsent(dir.resolve("poultry_coop.nbt"), poultryCoop());
        writeIfAbsent(dir.resolve("wool_pen.nbt"), woolPen());
        writeIfAbsent(dir.resolve("stable.nbt"), stable());
        writeIfAbsent(dir.resolve("aquarium.nbt"), aquarium());
    }

    private static void writeIfAbsent(Path file, Built b) throws IOException {
        if (!Files.exists(file)) {
            StructureWriter.write(b.blocks(), b.blockEntities(), file);
            Skyseed.LOGGER.info("[skyseed] generated structure template {}", file.getFileName());
        }
    }

    private static void anchor(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockPos p, String floor) {
        m.put(p, Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.DOWN_SOUTH));
        final CompoundTag t = new CompoundTag();
        t.putString("id", "minecraft:jigsaw");
        t.putString("name", "minecraft:bottom");
        t.putString("target", "minecraft:empty");
        t.putString("pool", "minecraft:empty");
        t.putString("final_state", floor);
        t.putString("joint", "rollable");
        bes.put(p, t);
    }

    /** A square oak-fence ring at y=1 (perimeter of an {@code n}×{@code n} pad), with gates north and south. */
    private static void fenceRing(Map<BlockPos, BlockState> m, int n) {
        final BlockState fence = Blocks.OAK_FENCE.defaultBlockState();
        final int max = n - 1, mid = n / 2;
        for (int x = 0; x < n; x++) {
            for (int z = 0; z < n; z++) {
                if (x == 0 || x == max || z == 0 || z == max) {
                    m.put(new BlockPos(x, 1, z), fence);
                }
            }
        }
        final BlockState gate = Blocks.OAK_FENCE_GATE.defaultBlockState().setValue(FenceGateBlock.FACING, Direction.NORTH);
        m.put(new BlockPos(mid, 1, 0), gate);
        m.put(new BlockPos(mid, 1, max), gate);
    }

    /** 🌾 Pasture: a 7×7 fenced field with a hay bale and a water trough. */
    private static Built pasturePen() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        anchor(m, bes, new BlockPos(3, 0, 3), "minecraft:grass_block");
        fenceRing(m, 7);
        m.put(new BlockPos(2, 1, 2), Blocks.HAY_BLOCK.defaultBlockState());
        m.put(new BlockPos(4, 1, 4), Blocks.WATER_CAULDRON.defaultBlockState().setValue(LayeredCauldronBlock.LEVEL, 3));
        return new Built(m, bes);
    }

    /** 🐔 Poultry: a small enclosed 5×5 coop (chickens jump fences) with a slab roof, composter and hay. */
    private static Built poultryCoop() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        anchor(m, bes, new BlockPos(2, 0, 2), "minecraft:grass_block");
        final BlockState plank = Blocks.OAK_PLANKS.defaultBlockState();
        final int max = 4, mid = 2;
        for (int x = 0; x < 5; x++) {
            for (int z = 0; z < 5; z++) {
                if (x == 0 || x == max || z == 0 || z == max) {
                    m.put(new BlockPos(x, 1, z), plank);
                    m.put(new BlockPos(x, 2, z), plank);
                }
                m.put(new BlockPos(x, 3, z), Blocks.OAK_SLAB.defaultBlockState()); // low roof
            }
        }
        m.put(new BlockPos(mid, 1, 0), Blocks.OAK_FENCE_GATE.defaultBlockState().setValue(FenceGateBlock.FACING, Direction.NORTH));
        m.put(new BlockPos(1, 1, 1), Blocks.HAY_BLOCK.defaultBlockState());
        m.put(new BlockPos(3, 1, 3), Blocks.COMPOSTER.defaultBlockState());
        m.put(new BlockPos(mid, 2, mid), Blocks.LANTERN.defaultBlockState().setValue(LanternBlock.HANGING, true));
        return new Built(m, bes);
    }

    /** 🐑 Wool Farm: a roomy 9×9 fenced pen with a partial inner divider (color-sorting hint). */
    private static Built woolPen() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        anchor(m, bes, new BlockPos(4, 0, 4), "minecraft:grass_block");
        fenceRing(m, 9);
        final BlockState fence = Blocks.OAK_FENCE.defaultBlockState();
        for (int x = 1; x <= 7; x++) {
            if (x != 4) { // leave a gap to pass through
                m.put(new BlockPos(x, 1, 6), fence);
            }
        }
        m.put(new BlockPos(2, 1, 2), Blocks.HAY_BLOCK.defaultBlockState());
        m.put(new BlockPos(6, 1, 2), Blocks.HAY_BLOCK.defaultBlockState());
        return new Built(m, bes);
    }

    /** 🐴 Stable: a 7×5 roofed shelter, front fence + gates, a loot chest and hay. */
    private static Built stable() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        anchor(m, bes, new BlockPos(3, 0, 2), "minecraft:grass_block");
        final BlockState plank = Blocks.OAK_PLANKS.defaultBlockState();
        final int maxX = 6, maxZ = 4, midX = 3;
        for (int x = 0; x <= maxX; x++) {
            for (int z = 0; z <= maxZ; z++) {
                m.put(new BlockPos(x, 4, z), plank); // roof
                final boolean back = z == maxZ;
                final boolean side = x == 0 || x == maxX;
                if (back || side) {
                    for (int y = 1; y <= 3; y++) {
                        m.put(new BlockPos(x, y, z), plank);
                    }
                }
            }
        }
        // Front (z=0): fence with three stall gates.
        final BlockState fence = Blocks.OAK_FENCE.defaultBlockState();
        final BlockState gate = Blocks.OAK_FENCE_GATE.defaultBlockState().setValue(FenceGateBlock.FACING, Direction.NORTH);
        for (int x = 0; x <= maxX; x++) {
            m.put(new BlockPos(x, 1, 0), fence);
        }
        m.put(new BlockPos(1, 1, 0), gate);
        m.put(new BlockPos(3, 1, 0), gate);
        m.put(new BlockPos(5, 1, 0), gate);
        // Loot chest, hay, light.
        m.put(new BlockPos(1, 1, 3), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.SOUTH));
        final CompoundTag chest = new CompoundTag();
        chest.putString("id", "minecraft:chest");
        chest.putString("LootTable", "skyseed:chests/stable");
        bes.put(new BlockPos(1, 1, 3), chest);
        m.put(new BlockPos(5, 1, 3), Blocks.HAY_BLOCK.defaultBlockState());
        m.put(new BlockPos(midX, 3, 2), Blocks.LANTERN.defaultBlockState().setValue(LanternBlock.HANGING, true));
        return new Built(m, bes);
    }

    /** 🌊 Aquarium: a 9×9 glass tank of water over a sea-lantern-lit sand floor with coral and lily pads. */
    private static Built aquarium() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState sand = Blocks.SAND.defaultBlockState();
        final BlockState glass = Blocks.GLASS.defaultBlockState();
        final BlockState water = Blocks.WATER.defaultBlockState();
        final int max = 8;
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), sand); // floor
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                if (perim) {
                    for (int y = 1; y <= 4; y++) {
                        m.put(new BlockPos(x, y, z), glass); // tank walls
                    }
                } else {
                    for (int y = 1; y <= 3; y++) {
                        m.put(new BlockPos(x, y, z), water); // the water body
                    }
                }
            }
        }
        // Sea lanterns underfoot, coral, and lily pads on the surface.
        for (int[] c : new int[][]{{2, 2}, {6, 6}, {2, 6}, {6, 2}}) {
            m.put(new BlockPos(c[0], 0, c[1]), Blocks.SEA_LANTERN.defaultBlockState());
        }
        m.put(new BlockPos(3, 0, 3), Blocks.TUBE_CORAL_BLOCK.defaultBlockState());
        m.put(new BlockPos(5, 0, 5), Blocks.BRAIN_CORAL_BLOCK.defaultBlockState());
        m.put(new BlockPos(5, 0, 3), Blocks.HORN_CORAL_BLOCK.defaultBlockState());
        m.put(new BlockPos(2, 4, 2), Blocks.LILY_PAD.defaultBlockState());
        m.put(new BlockPos(6, 4, 6), Blocks.LILY_PAD.defaultBlockState());
        // Anchor last, so the sand-floor loop above doesn't overwrite the jigsaw block.
        anchor(m, bes, new BlockPos(4, 0, 4), "minecraft:sand");
        return new Built(m, bes);
    }
}
