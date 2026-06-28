package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import dev.gemberkoekje.skyseed.compat.Id;

import java.util.Optional;

/**
 * One weighted look for a theme (oak/birch/…); exactly one is rolled per island. {@code snow}, when set, is this
 * variant's per-column snow-cap probability (0–1), overriding the override/palette {@code snow} — so e.g. a snowy
 * variant can cap heavily while an icy one keeps most of its ice showing (see {@link Palette#snow()}).
 * {@code surfaceOverride}/{@code fillOverride}/{@code coreOverride}, when set, each replace the resolved
 * palette/biome-override block for this island — so a variant can re-skin the whole body (e.g. a stony island
 * rolling a diorite/granite/andesite look), not just its top layer.
 */
public record Variant(int weight, Optional<String> name, Optional<Id> surfaceOverride,
                      Optional<Id> fillOverride, Optional<Id> coreOverride,
                      Decoration decoration, Optional<Float> snow) {
    public static final Codec<Variant> CODEC = RecordCodecBuilder.create(i -> i.group(
            Codec.INT.optionalFieldOf("weight", 1).forGetter(Variant::weight),
            Codec.STRING.optionalFieldOf("name").forGetter(Variant::name),
            Id.CODEC.optionalFieldOf("surface_override").forGetter(Variant::surfaceOverride),
            Id.CODEC.optionalFieldOf("fill_override").forGetter(Variant::fillOverride),
            Id.CODEC.optionalFieldOf("core_override").forGetter(Variant::coreOverride),
            Decoration.CODEC.optionalFieldOf("decoration", Decoration.EMPTY).forGetter(Variant::decoration),
            Codec.FLOAT.optionalFieldOf("snow").forGetter(Variant::snow)
    ).apply(i, Variant::new));
}
