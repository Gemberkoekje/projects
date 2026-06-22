package dev.gemberkoekje.skyseed.worldgen;

import net.minecraft.util.RandomSource;

/**
 * A small bundle of angular harmonics (frequencies {@code 2,3,5}) that give a circle an irregular, organic edge —
 * used for both the island rim ({@link ShapeBuilder}) and the pond blob ({@link PondCarver}). {@link #sample}
 * rolls the per-harmonic amplitudes and phases from a {@link RandomSource} (consuming six doubles), normalized so
 * the amplitudes sum to {@code strength}; {@link #rim} then evaluates the wobbled radius at a given angle.
 */
final class RimNoise {
    private static final int[] FREQ = { 2, 3, 5 };

    private final double[] amp;
    private final double[] phase;

    private RimNoise(double[] amp, double[] phase) {
        this.amp = amp;
        this.phase = phase;
    }

    /** Roll the harmonics from {@code random} (six doubles), normalized so the amplitudes sum to {@code strength}. */
    static RimNoise sample(RandomSource random, double strength) {
        final double[] amp = new double[FREQ.length];
        final double[] phase = new double[FREQ.length];
        double sum = 0;
        for (int k = 0; k < FREQ.length; k++) {
            amp[k] = 0.3 + random.nextDouble();
            sum += amp[k];
            phase[k] = random.nextDouble() * Math.PI * 2.0;
        }
        for (int k = 0; k < FREQ.length; k++) {
            amp[k] = amp[k] / sum * strength;
        }
        return new RimNoise(amp, phase);
    }

    /** The wobbled radius at {@code angle}: {@code base} plus each harmonic's contribution (scaled by {@code base}). */
    double rim(double base, double angle) {
        double rim = base;
        for (int k = 0; k < FREQ.length; k++) {
            rim += base * amp[k] * Math.sin(FREQ[k] * angle + phase[k]);
        }
        return rim;
    }
}
