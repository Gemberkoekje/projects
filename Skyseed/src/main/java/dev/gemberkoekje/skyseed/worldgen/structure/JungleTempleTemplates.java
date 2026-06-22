package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.DispenserBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The jungle temple, in the vanilla spirit: a tiered, vine-draped cobblestone-and-moss ziggurat (a 9×9 base
 * stepping up through smaller tiers, with corner columns rising over it) over a sealed inner chamber. Two loot
 * chests ({@code minecraft:chests/jungle_temple}) and an arrow dispenser
 * ({@code minecraft:chests/jungle_temple_dispenser}) hide inside, rigged to a tripwire — the hooks/string are
 * baked as wool markers and swapped to real tripwire by {@link Traps} after assembly (fragile blocks pop on
 * the jigsaw path); trip it and the dispenser fires. See {@code SKYSTRUCTURESPLAN.md}.
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

    private static final BlockState COBBLE = Blocks.COBBLESTONE.defaultBlockState();
    private static final BlockState MOSSY = Blocks.MOSSY_COBBLESTONE.defaultBlockState();

    /** A deterministic mossy/plain cobble mix (≈⅓ mossy) for a weathered jungle look. */
    private static BlockState mix(int x, int y, int z) {
        return Math.floorMod(x * 7 + y * 13 + z * 5, 3) == 0 ? MOSSY : COBBLE;
    }

    private static Built temple() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int max = 8, mid = 4; // 9×9 footprint

        // y0: the 9×9 base platform.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), mix(x, 0, z));
            }
        }
        // Tier 1 (y1-3): the 7×7 chamber walls (inset 1) over a 5×5 interior; ceiling at y4.
        ring(m, 1, 7, 1, 3);
        for (int x = 1; x <= 7; x++) {
            for (int z = 1; z <= 7; z++) {
                m.put(new BlockPos(x, 4, z), mix(x, 4, z)); // ceiling
            }
        }
        // Tier 2 (y5-6): a 5×5 ring (inset 2) with a small hollow; ceiling at y7.
        ring(m, 2, 6, 5, 6);
        for (int x = 2; x <= 6; x++) {
            for (int z = 2; z <= 6; z++) {
                m.put(new BlockPos(x, 7, z), mix(x, 7, z));
            }
        }
        // Tier 3 (y8): a small 3×3 cap.
        for (int x = 3; x <= 5; x++) {
            for (int z = 3; z <= 5; z++) {
                m.put(new BlockPos(x, 8, z), mix(x, 8, z));
            }
        }
        // Corner columns at the four chamber corners, rising over the tiers.
        for (final int[] c : new int[][]{{1, 1}, {7, 1}, {1, 7}, {7, 7}}) {
            for (int y = 1; y <= 7; y++) {
                m.put(new BlockPos(c[0], y, c[1]), mix(c[0], y, c[1]));
            }
        }

        // A front doorway in the chamber (z=1 wall).
        m.remove(new BlockPos(mid, 1, 1));
        m.remove(new BlockPos(mid, 2, 1));

        // Inner chamber kit. Dispenser in the west wall aimed across the room; a tripwire across its line;
        // two loot chests against the back wall. Tripwire hooks/string are wool markers (see Traps).
        m.put(new BlockPos(1, 1, 4), Blocks.DISPENSER.defaultBlockState().setValue(DispenserBlock.FACING, Direction.EAST));
        bes.put(new BlockPos(1, 1, 4), dispenser());
        m.put(new BlockPos(2, 1, 4), Blocks.RED_WOOL.defaultBlockState());  // → tripwire hook on the dispenser
        m.put(new BlockPos(3, 1, 4), Blocks.LIME_WOOL.defaultBlockState()); // → tripwire string
        m.put(new BlockPos(4, 1, 4), Blocks.LIME_WOOL.defaultBlockState());
        m.put(new BlockPos(5, 1, 4), Blocks.LIME_WOOL.defaultBlockState());
        m.put(new BlockPos(6, 1, 4), Blocks.RED_WOOL.defaultBlockState());  // → tripwire hook on the far wall
        m.put(new BlockPos(3, 1, 6), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.NORTH));
        bes.put(new BlockPos(3, 1, 6), StructureParts.lootChest("minecraft:chests/jungle_temple"));
        m.put(new BlockPos(5, 1, 6), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.NORTH));
        bes.put(new BlockPos(5, 1, 6), StructureParts.lootChest("minecraft:chests/jungle_temple"));

        // Vines down the chamber walls and the columns, for the overgrown look. Skip the front face (the
        // doorway opening would leave them unsupported); each is anchored to a wall/ceiling block behind it.
        for (int z = 2; z <= 6; z += 2) {
            vines(m, 0, z, Direction.EAST, 3);   // west face → wall at x=1
            vines(m, 8, z, Direction.WEST, 3);   // east face → wall at x=7
        }
        for (int x = 2; x <= 6; x += 2) {
            vines(m, x, 8, Direction.NORTH, 3);  // back face → wall at z=7
        }
        for (final int[] c : new int[][]{{0, 1}, {8, 1}, {0, 7}, {8, 7}}) {
            vines(m, c[0], c[1], c[0] == 0 ? Direction.EAST : Direction.WEST, 5); // up the corner columns
        }

        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:cobblestone");
        return new Built(m, bes);
    }

    /** A square ring of cobble mix at the perimeter of {@code [lo..hi]²}, for the given y range. */
    private static void ring(Map<BlockPos, BlockState> m, int lo, int hi, int y0, int y1) {
        for (int x = lo; x <= hi; x++) {
            for (int z = lo; z <= hi; z++) {
                if (x == lo || x == hi || z == lo || z == hi) {
                    for (int y = y0; y <= y1; y++) {
                        m.put(new BlockPos(x, y, z), mix(x, y, z));
                    }
                }
            }
        }
    }

    /** A short hanging vine column at {@code (x, *, z)} attached to the wall in {@code dir}, {@code n} blocks tall. */
    private static void vines(Map<BlockPos, BlockState> m, int x, int z, Direction dir, int n) {
        BlockState vine = Blocks.VINE.defaultBlockState();
        switch (dir) {
            case NORTH -> vine = vine.setValue(BlockStateProperties.NORTH, true);
            case SOUTH -> vine = vine.setValue(BlockStateProperties.SOUTH, true);
            case EAST -> vine = vine.setValue(BlockStateProperties.EAST, true);
            default -> vine = vine.setValue(BlockStateProperties.WEST, true);
        }
        for (int i = 0; i < n; i++) {
            m.putIfAbsent(new BlockPos(x, 2 + i, z), vine); // within the wall/column height, don't overwrite solids
        }
    }

    private static CompoundTag dispenser() {
        final CompoundTag be = new CompoundTag();
        be.putString("id", "minecraft:dispenser");
        be.putString("LootTable", "minecraft:chests/jungle_temple_dispenser");
        return be;
    }
}
