package dev.gemberkoekje.skyseed.worldgen.structure;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.TripWireHookBlock;

import java.util.ArrayList;
import java.util.List;

/**
 * Re-adds the support-dependent "trap" blocks AFTER a structure is assembled. {@code JigsawPlacement} places
 * blocks with no neighbour updates, so pressure plates, tripwire hooks and tripwire string pop straight off —
 * so the structure {@code .nbt} bakes them as solid WOOL markers (full blocks survive the jigsaw path and
 * rotate with the piece), and this pass scans the assembled structure and swaps each marker for its real trap
 * block with full block updates (so it validates support and connects). A hook's facing is taken from the
 * adjacent wire marker. This is what gives the Desert Temple plate (over hidden TNT) and the Jungle Temple
 * tripwire → dispenser their teeth back. See {@code SKYSTRUCTURESPLAN.md}.
 */
public final class Traps {
    private Traps() {}

    private static final Block PLATE_MARKER = Blocks.YELLOW_WOOL; // → stone pressure plate (over baked TNT)
    private static final Block HOOK_MARKER = Blocks.RED_WOOL;     // → tripwire hook (faces the adjacent wire)
    private static final Block WIRE_MARKER = Blocks.LIME_WOOL;    // → tripwire string

    /** Scan a small box around the structure's centre for markers and swap each for its trap block. */
    public static void applyAfterJigsaw(ServerLevel level, BlockPos origin) {
        final List<BlockPos> plates = new ArrayList<>();
        final List<BlockPos> hooks = new ArrayList<>();
        final List<BlockPos> wires = new ArrayList<>();
        final BlockPos.MutableBlockPos p = new BlockPos.MutableBlockPos();
        for (int dx = -6; dx <= 6; dx++) {
            for (int dz = -6; dz <= 6; dz++) {
                for (int dy = -7; dy <= 5; dy++) {
                    p.set(origin.getX() + dx, origin.getY() + dy, origin.getZ() + dz);
                    final Block b = level.getBlockState(p).getBlock();
                    if (b == PLATE_MARKER) {
                        plates.add(p.immutable());
                    } else if (b == HOOK_MARKER) {
                        hooks.add(p.immutable());
                    } else if (b == WIRE_MARKER) {
                        wires.add(p.immutable());
                    }
                }
            }
        }
        if (plates.isEmpty() && hooks.isEmpty() && wires.isEmpty()) {
            return;
        }
        for (final BlockPos pp : plates) {
            level.setBlock(pp, Blocks.STONE_PRESSURE_PLATE.defaultBlockState(), Block.UPDATE_ALL);
        }
        for (final BlockPos pp : hooks) {
            Direction face = Direction.NORTH;
            for (final Direction d : Direction.Plane.HORIZONTAL) {
                if (wires.contains(pp.relative(d))) {
                    face = d; // the hook faces along the wire; its support is the solid block behind it
                    break;
                }
            }
            level.setBlock(pp, Blocks.TRIPWIRE_HOOK.defaultBlockState().setValue(TripWireHookBlock.FACING, face), Block.UPDATE_ALL);
        }
        for (final BlockPos pp : wires) { // last, so the string connects to the hooks already placed
            level.setBlock(pp, Blocks.TRIPWIRE.defaultBlockState(), Block.UPDATE_ALL);
        }
    }
}
