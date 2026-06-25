package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;

import java.util.Optional;

/**
 * Silhouette + vertical profile parameters (README → Generation algorithm). {@code maxUnderDepth}, when set, caps how
 * far the teardrop underside hangs below the centre — normally it scales with the radius (~radius deep), so a very wide
 * island would dangle a very deep point; the cap keeps a huge island a wide plateau instead of a bottomless cone.
 */
public record Shape(IntRange radius, float rimNoise, Underside underside, IntRange topDome,
                    Optional<Integer> maxUnderDepth) {
    public static final Codec<Shape> CODEC = RecordCodecBuilder.create(i -> i.group(
            IntRange.CODEC.fieldOf("radius").forGetter(Shape::radius),
            Codec.FLOAT.optionalFieldOf("rim_noise", 0.40f).forGetter(Shape::rimNoise),
            Underside.CODEC.optionalFieldOf("underside", Underside.TEARDROP).forGetter(Shape::underside),
            IntRange.CODEC.optionalFieldOf("top_dome", new IntRange(1, 2)).forGetter(Shape::topDome),
            Codec.INT.optionalFieldOf("max_under_depth").forGetter(Shape::maxUnderDepth)
    ).apply(i, Shape::new));
}
