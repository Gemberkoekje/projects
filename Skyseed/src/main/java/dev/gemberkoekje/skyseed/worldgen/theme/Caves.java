package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;

/**
 * Internal cave systems carved into a (huge) island's body — SKYHUGEPLAN Phase 2. {@code rooms} ellipsoid chambers of
 * {@code size} radius, connected by tunnels, hollowed out of the interior while keeping a solid skin below the surface
 * and above the underside (so the island never opens to the void by accident). Decorated from the variant's underside
 * palette (dripstone, cave vines, glow lichen) with a default if it has none; reachability — hidden / sinkhole /
 * gash — is rolled per island by the carver. See {@code worldgen/CaveCarver}.
 */
public record Caves(IntRange rooms, IntRange size) {
    public static final Codec<Caves> CODEC = RecordCodecBuilder.create(i -> i.group(
            IntRange.CODEC.optionalFieldOf("rooms", new IntRange(3, 6)).forGetter(Caves::rooms),
            IntRange.CODEC.optionalFieldOf("size", new IntRange(3, 5)).forGetter(Caves::size)
    ).apply(i, Caves::new));
}
