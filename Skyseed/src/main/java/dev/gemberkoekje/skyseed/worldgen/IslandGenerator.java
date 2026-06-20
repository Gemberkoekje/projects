package dev.gemberkoekje.skyseed.worldgen;

import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;

/**
 * Places island blocks into the world. Deliberately a near-pure function of
 * {@code (ServerLevel, BlockPos center, RandomSource)} — independent of items/entities so it stays
 * reusable (the curated start island can share it) and testable (plan §6).
 *
 * <p>Milestone 4: an irregular teardrop blob with layered fill (plan §5 steps 1–2). The palette and
 * shape numbers are hardcoded to a forest-ish theme for now; they become theme JSON in milestone 6.
 * Ores and decoration arrive in milestone 5; tick-budgeted placement in milestone 9.
 */
public final class IslandGenerator {
    private IslandGenerator() {}

    public static void generateIsland(ServerLevel level, BlockPos center, RandomSource random) {
        // --- shape parameters (theme params, later from JSON) ---
        final int rMin = 7, rMax = 10;
        final int baseRadius = rMin + random.nextInt(rMax - rMin + 1);
        final double rimNoise = 0.40;     // how wavy the outline is
        final double maxDepth = baseRadius * 1.05; // underside bulge at the centre
        final int topDome = 1 + random.nextInt(2); // 1..2 — a flat-ish dome
        final int baseFill = 3;           // dirt band thickness under the grass

        // --- palette ---
        final BlockState surface = Blocks.GRASS_BLOCK.defaultBlockState();
        final BlockState fill = Blocks.DIRT.defaultBlockState();
        final BlockState core = Blocks.STONE.defaultBlockState();

        // Irregular rim: a few angular harmonics with random phase/amplitude, so every island's
        // outline is different (decorrelated per throw by the RandomSource) yet smooth.
        final int[] freq = { 2, 3, 5 };
        final double[] amp = new double[freq.length];
        final double[] phase = new double[freq.length];
        double ampSum = 0;
        for (int k = 0; k < freq.length; k++) {
            amp[k] = 0.3 + random.nextDouble();
            ampSum += amp[k];
            phase[k] = random.nextDouble() * Math.PI * 2.0;
        }
        for (int k = 0; k < freq.length; k++) {
            amp[k] = amp[k] / ampSum * rimNoise; // normalise so total wobble ~= rimNoise
        }

        final int maxR = (int) Math.ceil(baseRadius * (1.0 + rimNoise)) + 1;
        final BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();

        for (int dx = -maxR; dx <= maxR; dx++) {
            for (int dz = -maxR; dz <= maxR; dz++) {
                final double dist = Math.sqrt((double) dx * dx + (double) dz * dz);
                final double angle = Math.atan2(dz, dx);

                double rim = baseRadius;
                for (int k = 0; k < freq.length; k++) {
                    rim += baseRadius * amp[k] * Math.sin(freq[k] * angle + phase[k]);
                }
                if (dist > rim) {
                    continue; // outside this island's outline
                }

                final double t = Math.min(1.0, dist / Math.max(0.001, rim)); // 0 centre .. 1 rim
                final double bulge = 1.0 - t * t;

                final int dome = (int) Math.round(topDome * bulge);
                int depth = (int) Math.round(maxDepth * Math.pow(bulge, 0.85));
                if (random.nextFloat() < 0.3) {
                    depth += random.nextInt(2); // roughen the underside a touch
                }

                final int surfaceY = center.getY() + dome;
                final int bottomY = center.getY() - depth;
                final int fillThickness = baseFill + random.nextInt(3) - 1; // 2..4 jitter

                for (int y = bottomY; y <= surfaceY; y++) {
                    final BlockState state;
                    if (y == surfaceY) {
                        state = surface;
                    } else if (y >= surfaceY - fillThickness) {
                        state = fill;
                    } else {
                        state = core;
                    }
                    // UPDATE_CLIENTS only: show the block without neighbour/physics cascades.
                    level.setBlock(pos.set(center.getX() + dx, y, center.getZ() + dz), state, Block.UPDATE_CLIENTS);
                }
            }
        }
    }
}
