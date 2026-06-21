package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import net.minecraft.resources.ResourceLocation;

/**
 * One entry in a theme's weighted pool of building templates. {@code template} is a structure id
 * resolving to {@code data/<ns>/structure/<path>.nbt}; {@code weight} is its relative chance. The
 * generator picks one and stamps it on the island (see {@code SKYVILLAGESPLAN.md}).
 */
public record StructureChoice(ResourceLocation template, int weight) {
    public static final Codec<StructureChoice> CODEC = RecordCodecBuilder.create(i -> i.group(
            ResourceLocation.CODEC.fieldOf("template").forGetter(StructureChoice::template),
            Codec.INT.optionalFieldOf("weight", 1).forGetter(StructureChoice::weight)
    ).apply(i, StructureChoice::new));
}
