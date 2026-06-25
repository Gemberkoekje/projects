package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.worldgen.theme.Shape;
import net.minecraft.core.BlockPos;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.state.BlockState;

import java.util.List;
import java.util.Map;

/**
 * Pass 1 of generation (README → solid island): samples the radius/dome, gives the rim an irregular edge from a few
 * angular harmonics, and fills a domed, layered body (surface / fill / core, or banded strata) into {@code blockMap}.
 * Populates the core/surface/bottom column lists the later planners work from and returns the shape values
 * ({@code baseRadius}, {@code topDome}) and core Y-range the orchestrator still needs.
 */
final class ShapeBuilder {
    private ShapeBuilder() {}

    /** Falloff exponent for the body's depth vs. the centre bulge: lower = a flatter, more plateau-like underside. */
    private static final double DEPTH_BULGE_EXP = 0.85;

    /** The shape values the orchestrator reuses after pass 1: the rolled radius/dome and the core Y-range (for ores). */
    record Result(int baseRadius, int topDome, int minCoreY, int maxCoreY) {}

    static Result build(BlockPos center, Shape shape, BlockState surface, BlockState fill, BlockState core,
                        List<Scatter> scatter, List<BlockState> bands, int bandThickness, int baseFill, RandomSource random,
                        Map<BlockPos, BlockState> blockMap, List<BlockPos> coreList, List<BlockPos> surfaceList,
                        List<BlockPos> bottomList) {
        // --- shape parameters ---
        final int baseRadius = Math.max(1, shape.radius().sample(random));
        final double rimNoise = shape.rimNoise();
        final int topDome = shape.topDome().sample(random);
        // The teardrop normally hangs ~radius deep; a per-shape cap keeps a huge island a wide plateau, not a deep cone.
        final double maxDepth = Math.min(baseRadius * 1.05, shape.maxUnderDepth().orElse(Integer.MAX_VALUE));

        // Irregular rim from a few angular harmonics (decorrelated per island via the RandomSource).
        final RimNoise edge = RimNoise.sample(random, rimNoise);

        final int maxR = (int) Math.ceil(baseRadius * (1.0 + rimNoise)) + 1;
        int minCoreY = Integer.MAX_VALUE;
        int maxCoreY = Integer.MIN_VALUE;

        // --- pass 1: solid island, layered fill ---
        for (int dx = -maxR; dx <= maxR; dx++) {
            for (int dz = -maxR; dz <= maxR; dz++) {
                final double dist = Math.sqrt((double) dx * dx + (double) dz * dz);
                final double angle = Math.atan2(dz, dx);

                final double rim = edge.rim(baseRadius, angle);
                if (dist > rim) {
                    continue;
                }

                final double t = Math.min(1.0, dist / Math.max(0.001, rim));
                final double bulge = 1.0 - t * t;

                final int dome = (int) Math.round(topDome * bulge);
                int depth = (int) Math.round(maxDepth * Math.pow(bulge, DEPTH_BULGE_EXP));
                if (random.nextFloat() < 0.3) {
                    depth += random.nextInt(2);
                }

                final int wx = center.getX() + dx;
                final int wz = center.getZ() + dz;
                final int surfaceY = center.getY() + dome;
                final int bottomY = center.getY() - depth;
                final int fillThickness = baseFill + random.nextInt(3) - 1;

                for (int y = bottomY; y <= surfaceY; y++) {
                    final BlockPos p = new BlockPos(wx, y, wz);
                    if (y == surfaceY) {
                        blockMap.put(p, scatterSurface(surface, scatter, random));
                        surfaceList.add(p);
                    } else if (y >= surfaceY - fillThickness) {
                        blockMap.put(p, bands.isEmpty() ? fill : bandAt(bands, y, bandThickness));
                    } else {
                        blockMap.put(p, bands.isEmpty() ? core : bandAt(bands, y, bandThickness));
                        coreList.add(p);
                        minCoreY = Math.min(minCoreY, y);
                        maxCoreY = Math.max(maxCoreY, y);
                    }
                }
                bottomList.add(new BlockPos(wx, bottomY, wz));
            }
        }
        return new Result(baseRadius, topDome, minCoreY, maxCoreY);
    }

    /** The band block for a given world Y: strata {@code thickness} tall, cycling through the list. */
    private static BlockState bandAt(List<BlockState> bands, int y, int thickness) {
        return bands.get(Math.floorMod(Math.floorDiv(y, thickness), bands.size()));
    }

    /** The surface block for a column: the default, unless a surface-scatter entry rolls in. */
    private static BlockState scatterSurface(BlockState surface, List<Scatter> scatter, RandomSource random) {
        if (scatter.isEmpty()) {
            return surface;
        }
        float roll = random.nextFloat();
        for (Scatter s : scatter) {
            roll -= s.chance();
            if (roll < 0) {
                return s.state();
            }
        }
        return surface;
    }
}
