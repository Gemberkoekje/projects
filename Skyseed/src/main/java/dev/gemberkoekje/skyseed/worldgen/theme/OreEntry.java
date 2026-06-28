package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import dev.gemberkoekje.skyseed.compat.Id;

/** One ore-table row: presence {@code chance}, how many veins ({@code count}), and vein size, at a depth. */
public record OreEntry(Id block, float chance, IntRange count, IntRange veinSize, OreDepth depth) {
    public static final Codec<OreEntry> CODEC = RecordCodecBuilder.create(i -> i.group(
            Id.CODEC.fieldOf("block").forGetter(OreEntry::block),
            Codec.FLOAT.fieldOf("chance").forGetter(OreEntry::chance),
            IntRange.CODEC.fieldOf("count").forGetter(OreEntry::count),
            IntRange.CODEC.fieldOf("vein_size").forGetter(OreEntry::veinSize),
            OreDepth.CODEC.optionalFieldOf("depth", OreDepth.CORE).forGetter(OreEntry::depth)
    ).apply(i, OreEntry::new));
}
