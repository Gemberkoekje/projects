package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;

/** Silhouette + vertical profile parameters (README → Generation algorithm). */
public record Shape(IntRange radius, float rimNoise, Underside underside, IntRange topDome) {
    public static final Codec<Shape> CODEC = RecordCodecBuilder.create(i -> i.group(
            IntRange.CODEC.fieldOf("radius").forGetter(Shape::radius),
            Codec.FLOAT.optionalFieldOf("rim_noise", 0.40f).forGetter(Shape::rimNoise),
            Underside.CODEC.optionalFieldOf("underside", Underside.TEARDROP).forGetter(Shape::underside),
            IntRange.CODEC.optionalFieldOf("top_dome", new IntRange(1, 2)).forGetter(Shape::topDome)
    ).apply(i, Shape::new));
}
