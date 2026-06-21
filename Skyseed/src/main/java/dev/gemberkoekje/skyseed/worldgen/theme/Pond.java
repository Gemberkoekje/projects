package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import net.minecraft.resources.ResourceLocation;

/**
 * A small contained pool carved into the island's top centre (a lake/pond). {@code block} is the
 * fluid (default water), resolved at gen time. The pool is walled by the island's domed rim, and is
 * placed without block updates so it stays still and never spills into the void.
 */
public record Pond(ResourceLocation block, int radius, int depth) {
    public static final Codec<Pond> CODEC = RecordCodecBuilder.create(i -> i.group(
            ResourceLocation.CODEC.optionalFieldOf("block", ResourceLocation.withDefaultNamespace("water")).forGetter(Pond::block),
            Codec.INT.optionalFieldOf("radius", 3).forGetter(Pond::radius),
            Codec.INT.optionalFieldOf("depth", 2).forGetter(Pond::depth)
    ).apply(i, Pond::new));
}
