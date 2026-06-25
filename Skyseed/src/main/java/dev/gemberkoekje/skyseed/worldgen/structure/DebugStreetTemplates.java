package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import net.minecraft.core.BlockPos;
import net.minecraft.core.FrontAndTop;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.JigsawBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * THROWAWAY spike pieces (SKYJIGSAWPLAN Phase 0): a self-connecting "street" pool to prove the jigsaw really
 * sprawls — branching, twisting, and (on a real island) running straight out over the void. A {@code plaza}
 * start piece carries four outward street connectors; the {@code streets} pool ({@code straight} / {@code corner}
 * / {@code cross} / {@code end} + a weighted empty terminator) chains off them at depth 6. Plain cobblestone
 * decks for now — the terrain-aware path / self-railing-bridge surfacing (SKYJIGSAWPLAN §3a) is Phase 1.
 * Reached only by the creative {@code debug_streets} seed; delete the lot once the real village system lands.
 */
public final class DebugStreetTemplates {
    private DebugStreetTemplates() {}

    /** Every street piece self-links through this pool — name = target = {@code skyseed:street}. */
    private static final String POOL = "skyseed:debug_streets/streets";

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("plaza.nbt"), plaza());
        writeIfAbsent(dir.resolve("straight.nbt"), straight());
        writeIfAbsent(dir.resolve("corner.nbt"), corner());
        writeIfAbsent(dir.resolve("cross.nbt"), cross());
        writeIfAbsent(dir.resolve("end.nbt"), end());
    }

    /** 5×5 cobblestone start plaza: the island anchor + a lantern, and four outward street connectors. */
    private static Built plaza() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        floor(m, 4);
        street(m, bes, new BlockPos(2, 0, 0), FrontAndTop.NORTH_UP);
        street(m, bes, new BlockPos(2, 0, 4), FrontAndTop.SOUTH_UP);
        street(m, bes, new BlockPos(0, 0, 2), FrontAndTop.WEST_UP);
        street(m, bes, new BlockPos(4, 0, 2), FrontAndTop.EAST_UP);
        m.put(new BlockPos(2, 1, 2), Blocks.LANTERN.defaultBlockState());
        anchor(m, bes, new BlockPos(2, 0, 2), "minecraft:cobblestone"); // last, so the floor loop can't clobber it
        return new Built(m, bes);
    }

    /** A straight 3×3 segment: connectors west and east, so it passes through. */
    private static Built straight() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        floor(m, 2);
        street(m, bes, new BlockPos(0, 0, 1), FrontAndTop.WEST_UP);
        street(m, bes, new BlockPos(2, 0, 1), FrontAndTop.EAST_UP);
        return new Built(m, bes);
    }

    /** An L-corner 3×3 segment: connectors west and south, so the run turns. */
    private static Built corner() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        floor(m, 2);
        street(m, bes, new BlockPos(0, 0, 1), FrontAndTop.WEST_UP);
        street(m, bes, new BlockPos(1, 0, 2), FrontAndTop.SOUTH_UP);
        return new Built(m, bes);
    }

    /** A 4-way crossing 3×3 segment: connectors on all sides — the branch points. */
    private static Built cross() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        floor(m, 2);
        street(m, bes, new BlockPos(1, 0, 0), FrontAndTop.NORTH_UP);
        street(m, bes, new BlockPos(1, 0, 2), FrontAndTop.SOUTH_UP);
        street(m, bes, new BlockPos(0, 0, 1), FrontAndTop.WEST_UP);
        street(m, bes, new BlockPos(2, 0, 1), FrontAndTop.EAST_UP);
        return new Built(m, bes);
    }

    /** A dead-end cap 3×3: one connector (west) and a little lamp post so the end of a run reads clearly. */
    private static Built end() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        floor(m, 2);
        street(m, bes, new BlockPos(0, 0, 1), FrontAndTop.WEST_UP);
        m.put(new BlockPos(1, 1, 1), Blocks.OAK_FENCE.defaultBlockState());
        m.put(new BlockPos(1, 2, 1), Blocks.OAK_FENCE.defaultBlockState());
        m.put(new BlockPos(1, 3, 1), Blocks.LANTERN.defaultBlockState());
        return new Built(m, bes);
    }

    /** A square cobblestone deck on y=0 from (0,0,0) to (max,0,max). */
    private static void floor(Map<BlockPos, BlockState> m, int max) {
        final BlockState cob = Blocks.COBBLESTONE.defaultBlockState();
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), cob);
            }
        }
    }

    /** A street connector at {@code p} facing {@code dir}, self-linking into the streets pool. */
    private static void street(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockPos p, FrontAndTop dir) {
        m.put(p, Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, dir));
        bes.put(p, jig("skyseed:street", "skyseed:street", POOL, "minecraft:cobblestone"));
    }
}
