package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.worldgen.theme.LadderShaft;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.LadderBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.util.List;
import java.util.Map;

/**
 * Punches a vertical "way down" through an island's centre (README → ladder islands): the centre column becomes a
 * climbable shaft — ladders backed by a cobblestone wall — that hangs {@code depth} blocks below the island to a
 * small cobblestone landing at mining level, so you can get down without bridging out into the void. A
 * {@code waterfall_chance} roll instead leaves the shaft open and drops a <em>single</em> water source on the
 * surface; water physics carries it straight down as a tidy one-wide waterfall (the source position is recorded so
 * {@link GenerationJob} can schedule its fluid tick — the block fill is physics-free, so it would otherwise sit).
 *
 * <p>Geometry: the climb column is the island centre {@code (cx, cz)}; the backing wall is one block south
 * {@code (cx, cz+1)}, so the ladders face north and rest on the wall behind them. Within the island the body itself
 * backs the ladders; below it, a hung cobblestone column does. Deterministic given the island's RNG.
 */
final class ShaftPlanner {
    private ShaftPlanner() {}

    static void carve(Map<BlockPos, BlockState> blockMap, BlockPos center, LadderShaft cfg, RandomSource random,
                      List<BlockPos> fluidTicks) {
        final int cx = center.getX();
        final int cz = center.getZ();

        // Find the island's solid column extent at the centre.
        int colTop = Integer.MIN_VALUE;
        int colBottom = Integer.MAX_VALUE;
        for (final BlockPos p : blockMap.keySet()) {
            if (p.getX() == cx && p.getZ() == cz) {
                colTop = Math.max(colTop, p.getY());
                colBottom = Math.min(colBottom, p.getY());
            }
        }
        if (colTop == Integer.MIN_VALUE) {
            return; // no centre column to punch through (shouldn't happen for a real island)
        }

        final boolean waterfall = random.nextFloat() < cfg.waterfallChance();
        final int bz = cz + 1; // backing wall one block south → ladders face north (their support is behind them)
        final int landingY = colBottom - 1 - cfg.depth();
        final int shaftBottom = landingY + 1; // lowest shaft/backing block, one above the landing

        final BlockState cobble = Blocks.COBBLESTONE.defaultBlockState();

        if (waterfall) {
            // Clear the centre column to an open shaft and drop a single water source flush on the surface; water
            // physics carries it straight down (GenerationJob schedules the fluid tick). Falling water stays a tidy
            // one-wide column — no source-per-block to spill once the chunk ticks.
            for (int y = colTop; y >= shaftBottom; y--) {
                blockMap.remove(new BlockPos(cx, y, cz));
            }
            final BlockPos source = new BlockPos(cx, colTop, cz);
            blockMap.put(source, Blocks.WATER.defaultBlockState());
            fluidTicks.add(source);
        } else {
            // Fill the centre column with ladders, backed by the island body / the cobblestone wall below.
            final BlockState ladder = Blocks.LADDER.defaultBlockState().setValue(LadderBlock.FACING, Direction.NORTH);
            for (int y = colTop; y >= shaftBottom; y--) {
                blockMap.put(new BlockPos(cx, y, cz), ladder);
            }
        }
        blockMap.remove(new BlockPos(cx, colTop + 1, cz)); // clear any decoration over the entrance

        // The backing/"stack" wall: fill any gaps behind the shaft with cobblestone (the island body backs the rest).
        for (int y = colTop; y >= shaftBottom; y--) {
            blockMap.putIfAbsent(new BlockPos(cx, y, bz), cobble);
        }

        // A solid square cobblestone landing — the waterfall just pools on it and spills off the rim naturally.
        final int r = cfg.landingRadius();
        for (int dx = -r; dx <= r; dx++) {
            for (int dz = -r; dz <= r; dz++) {
                blockMap.put(new BlockPos(cx + dx, landingY, cz + dz), cobble);
            }
        }
    }
}
