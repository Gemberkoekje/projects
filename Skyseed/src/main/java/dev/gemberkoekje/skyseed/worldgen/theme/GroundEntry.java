package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import net.minecraft.resources.ResourceLocation;

/** A ground-cover plant placed on a surface column with per-column probability {@code chance}. */
public record GroundEntry(ResourceLocation block, float chance) {
    public static final Codec<GroundEntry> CODEC = RecordCodecBuilder.create(i -> i.group(
            ResourceLocation.CODEC.fieldOf("block").forGetter(GroundEntry::block),
            Codec.FLOAT.fieldOf("chance").forGetter(GroundEntry::chance)
    ).apply(i, GroundEntry::new));
}
