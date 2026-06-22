package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.BedBlock;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.DoorBlock;
import net.minecraft.world.level.block.LadderBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BedPart;
import net.minecraft.world.level.block.state.properties.DoubleBlockHalf;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * Code-authored Hamlet cottages, generated to {@code .nbt} structure templates at dev time (see
 * {@link DevStructureGenerator}). Each is a small house with log corner posts, glass windows, a bed, and — the
 * thing that sells it — a pitched, overhanging {@code stairs} roof rising to a gable ridge. Three variants in
 * different woods (oak / spruce / birch) give the Hamlet variety; the jigsaw rotates each randomly. A downward
 * "bottom" jigsaw at the floor centre lets the assembler anchor it. Replace any file with a structure-block
 * {@code .nbt} of the same name to override it.
 */
public final class HamletTemplates {
    private HamletTemplates() {}

    /** A wood palette for one cottage variant: body planks, corner-post log, roof stairs, ridge slab, door. */
    private record Wood(BlockState planks, BlockState log, Block stairs, Block slab, Block door) {}

    private static final Wood OAK = new Wood(Blocks.OAK_PLANKS.defaultBlockState(),
            Blocks.OAK_LOG.defaultBlockState(), Blocks.OAK_STAIRS, Blocks.OAK_SLAB, Blocks.OAK_DOOR);
    private static final Wood SPRUCE = new Wood(Blocks.SPRUCE_PLANKS.defaultBlockState(),
            Blocks.SPRUCE_LOG.defaultBlockState(), Blocks.SPRUCE_STAIRS, Blocks.SPRUCE_SLAB, Blocks.SPRUCE_DOOR);
    private static final Wood BIRCH = new Wood(Blocks.BIRCH_PLANKS.defaultBlockState(),
            Blocks.BIRCH_LOG.defaultBlockState(), Blocks.BIRCH_STAIRS, Blocks.BIRCH_SLAB, Blocks.BIRCH_DOOR);

    public static void generateInto(Path structureDir) throws IOException {
        writeIfAbsent(structureDir.resolve("cottage_oak.nbt"), cottage(7, OAK, true));
        writeIfAbsent(structureDir.resolve("cottage_spruce.nbt"), cottage(7, SPRUCE, false));
        writeIfAbsent(structureDir.resolve("cottage_small.nbt"), cottage(5, BIRCH, true));
    }

    /**
     * An n×n cottage (n odd) with a gabled stair roof. Laid out with a one-block border (the building sits at
     * [1..n]) so the roof's overhang stays within the template's non-negative bounds. The ridge runs along Z at
     * the centre; the roof slopes down in X and overhangs a block on every side. The door wall is a gable end.
     */
    private static Built cottage(int n, Wood w, boolean doorOnNorth) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState planks = w.planks();
        final BlockState log = w.log();
        final BlockState glass = Blocks.GLASS.defaultBlockState();
        final int lo = 1, hi = n;               // building spans [1..n]; the border at 0 / n+1 carries the eaves
        final int mid = (n + 1) / 2;            // centre column (ridge line)
        final int wallTop = 3, ceil = 4;        // walls y1..3, flat ceiling at y4
        final int ridgeY = ceil + mid;          // eave at y4 (x = 0 / n+1), ridge at the centre

        // Floor, walls (log corners), flat ceiling.
        for (int x = lo; x <= hi; x++) {
            for (int z = lo; z <= hi; z++) {
                m.put(new BlockPos(x, 0, z), planks);
                final boolean perim = x == lo || x == hi || z == lo || z == hi;
                final boolean corner = (x == lo || x == hi) && (z == lo || z == hi);
                if (perim) {
                    for (int h = 1; h <= wallTop; h++) {
                        m.put(new BlockPos(x, h, z), corner ? log : planks);
                    }
                }
                m.put(new BlockPos(x, ceil, z), planks);
            }
        }

        // Windows on the two side walls and the back wall.
        final int frontZ = doorOnNorth ? lo : hi;
        final int backZ = doorOnNorth ? hi : lo;
        m.put(new BlockPos(lo, 2, mid), glass);
        m.put(new BlockPos(hi, 2, mid), glass);
        m.put(new BlockPos(mid, 2, backZ), glass);

        // Door in the front gable wall.
        final Direction face = doorOnNorth ? Direction.SOUTH : Direction.NORTH;
        m.put(new BlockPos(mid, 1, frontZ), w.door().defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.LOWER).setValue(DoorBlock.FACING, face));
        m.put(new BlockPos(mid, 2, frontZ), w.door().defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.UPPER).setValue(DoorBlock.FACING, face));

        // Furniture: a bed (a villager's home), a crafting table, a torch.
        m.put(new BlockPos(2, 1, 2), Blocks.RED_BED.defaultBlockState()
                .setValue(BedBlock.PART, BedPart.FOOT).setValue(BedBlock.FACING, Direction.SOUTH));
        m.put(new BlockPos(2, 1, 3), Blocks.RED_BED.defaultBlockState()
                .setValue(BedBlock.PART, BedPart.HEAD).setValue(BedBlock.FACING, Direction.SOUTH));
        m.put(new BlockPos(hi - 1, 1, hi - 1), Blocks.CRAFTING_TABLE.defaultBlockState());
        m.put(new BlockPos(hi - 1, 1, 2), Blocks.TORCH.defaultBlockState());

        // Gabled stair roof with a one-block overhang, plus a glass loft window in the front gable.
        StructureParts.gableRoof(m, lo, hi, lo, hi, ceil, planks, w.stairs(), w.slab(), 1);
        if (ceil + 1 < ridgeY) {
            m.put(new BlockPos(mid, ceil + 1, frontZ), glass);
        }

        // A ladder up the back wall to the loft above the ceiling — the topmost rung punches the ceiling hole.
        final int ladderZ = doorOnNorth ? hi - 1 : lo + 1;
        final Direction ladderFace = doorOnNorth ? Direction.NORTH : Direction.SOUTH;
        for (int y = 1; y <= ceil; y++) {
            m.put(new BlockPos(mid, y, ladderZ), Blocks.LADDER.defaultBlockState().setValue(LadderBlock.FACING, ladderFace));
        }

        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), BuiltInRegistries.BLOCK.getKey(planks.getBlock()).toString());
        return new Built(m, bes);
    }
}
