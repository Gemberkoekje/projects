package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import dev.gemberkoekje.skyseed.compat.Id;

/** A ground-cover plant placed on a surface column with per-column probability {@code chance}. */
public record GroundEntry(Id block, float chance) {
    public static final Codec<GroundEntry> CODEC = RecordCodecBuilder.create(i -> i.group(
            Id.CODEC.fieldOf("block").forGetter(GroundEntry::block),
            Codec.FLOAT.fieldOf("chance").forGetter(GroundEntry::chance)
    ).apply(i, GroundEntry::new));
}
