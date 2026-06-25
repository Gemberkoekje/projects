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
 * THROWAWAY spike pieces (SKYJIGSAWPLAN Phase 0/1): a self-connecting "street" pool that proves the jigsaw
 * sprawls AND drives the path/bridge marker surfacing. A solid {@code plaza} start piece carries four outward
 * connectors; the {@code streets} pool ({@code straight}/{@code corner}/{@code cross}/{@code end} + a weighted
 * empty terminator) chains off them at depth 6. The street pieces bake NO floor — each lays a
 * {@link PathSurfacer#MARKER} one block above every deck tile and clears its connector tile to air, so
 * {@link PathSurfacer} turns the run into a terrain-aware dirt path on the island and a self-railing wooden
 * bridge out over the void. Reached only by the creative {@code debug_streets} seed; delete the lot once the
 * real village system lands.
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

    /** 5×5 solid-cobblestone start plaza: the island anchor + a lantern, and four outward street connectors. */
    private static Built plaza() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState cob = Blocks.COBBLESTONE.defaultBlockState();
        for (int x = 0; x <= 4; x++) {
            for (int z = 0; z <= 4; z++) {
                m.put(new BlockPos(x, 0, z), cob);
            }
        }
        street(m, bes, new BlockPos(2, 0, 0), FrontAndTop.NORTH_UP, "minecraft:cobblestone");
        street(m, bes, new BlockPos(2, 0, 4), FrontAndTop.SOUTH_UP, "minecraft:cobblestone");
        street(m, bes, new BlockPos(0, 0, 2), FrontAndTop.WEST_UP, "minecraft:cobblestone");
        street(m, bes, new BlockPos(4, 0, 2), FrontAndTop.EAST_UP, "minecraft:cobblestone");
        m.put(new BlockPos(2, 1, 2), Blocks.LANTERN.defaultBlockState());
        anchor(m, bes, new BlockPos(2, 0, 2), "minecraft:cobblestone"); // last, so the floor loop can't clobber it
        return new Built(m, bes);
    }

    /** A straight marker segment: connectors west and east, so it passes through. */
    private static Built straight() {
        return segment(FrontAndTop.WEST_UP, FrontAndTop.EAST_UP);
    }

    /** An L-corner marker segment: connectors west and south, so the run turns. */
    private static Built corner() {
        return segment(FrontAndTop.WEST_UP, FrontAndTop.SOUTH_UP);
    }

    /** A 4-way crossing marker segment: connectors on all sides — the branch points. */
    private static Built cross() {
        return segment(FrontAndTop.WEST_UP, FrontAndTop.EAST_UP, FrontAndTop.NORTH_UP, FrontAndTop.SOUTH_UP);
    }

    /** A dead-end stub: one connector (west). PathSurfacer rails its open sides if it ends over the void. */
    private static Built end() {
        return segment(FrontAndTop.WEST_UP);
    }

    /** A 3×3 marker deck (no baked floor) with a street connector on each given side. */
    private static Built segment(FrontAndTop... sides) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState wool = PathSurfacer.MARKER.defaultBlockState();
        for (int x = 0; x <= 2; x++) {
            for (int z = 0; z <= 2; z++) {
                m.put(new BlockPos(x, 1, z), wool); // a path marker one block above every deck tile
            }
        }
        for (final FrontAndTop side : sides) {
            street(m, bes, edge(side), side, "minecraft:air"); // connector tile is left to PathSurfacer to fill
        }
        return new Built(m, bes);
    }

    /** The edge-midpoint connector position on a 3×3 deck for a given outward facing. */
    private static BlockPos edge(FrontAndTop side) {
        return switch (side) {
            case NORTH_UP -> new BlockPos(1, 0, 0);
            case SOUTH_UP -> new BlockPos(1, 0, 2);
            case WEST_UP -> new BlockPos(0, 0, 1);
            case EAST_UP -> new BlockPos(2, 0, 1);
            default -> throw new IllegalArgumentException("unsupported street side " + side);
        };
    }

    /** A street connector at {@code p} facing {@code dir}, self-linking into the streets pool. */
    private static void street(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockPos p,
                               FrontAndTop dir, String finalState) {
        m.put(p, Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, dir));
        bes.put(p, jig("skyseed:street", "skyseed:street", POOL, finalState));
    }
}
