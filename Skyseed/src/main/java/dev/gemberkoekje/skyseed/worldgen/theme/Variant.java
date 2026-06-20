package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import net.minecraft.resources.ResourceLocation;

import java.util.Optional;

/** One weighted look for a theme (oak/birch/…); exactly one is rolled per island. */
public record Variant(int weight, Optional<String> name, Optional<ResourceLocation> surfaceOverride, Decoration decoration) {
    public static final Codec<Variant> CODEC = RecordCodecBuilder.create(i -> i.group(
            Codec.INT.optionalFieldOf("weight", 1).forGetter(Variant::weight),
            Codec.STRING.optionalFieldOf("name").forGetter(Variant::name),
            ResourceLocation.CODEC.optionalFieldOf("surface_override").forGetter(Variant::surfaceOverride),
            Decoration.CODEC.optionalFieldOf("decoration", Decoration.EMPTY).forGetter(Variant::decoration)
    ).apply(i, Variant::new));
}
