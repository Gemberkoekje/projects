package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import net.minecraft.resources.ResourceLocation;

import java.util.Optional;

/**
 * One weighted look for a theme (oak/birch/…); exactly one is rolled per island. {@code snow}, when set, is this
 * variant's per-column snow-cap probability (0–1), overriding the override/palette {@code snow} — so e.g. a snowy
 * variant can cap heavily while an icy one keeps most of its ice showing (see {@link Palette#snow()}).
 */
public record Variant(int weight, Optional<String> name, Optional<ResourceLocation> surfaceOverride,
                      Decoration decoration, Optional<Float> snow) {
    public static final Codec<Variant> CODEC = RecordCodecBuilder.create(i -> i.group(
            Codec.INT.optionalFieldOf("weight", 1).forGetter(Variant::weight),
            Codec.STRING.optionalFieldOf("name").forGetter(Variant::name),
            ResourceLocation.CODEC.optionalFieldOf("surface_override").forGetter(Variant::surfaceOverride),
            Decoration.CODEC.optionalFieldOf("decoration", Decoration.EMPTY).forGetter(Variant::decoration),
            Codec.FLOAT.optionalFieldOf("snow").forGetter(Variant::snow)
    ).apply(i, Variant::new));
}
