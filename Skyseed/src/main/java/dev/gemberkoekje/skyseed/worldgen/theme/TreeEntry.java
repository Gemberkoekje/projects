package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import dev.gemberkoekje.skyseed.compat.Id;

/** A configured feature (usually a tree) to attempt {@code tries} times, kept {@code spacing} blocks apart. */
public record TreeEntry(Id feature, int tries, int spacing) {
    public static final Codec<TreeEntry> CODEC = RecordCodecBuilder.create(i -> i.group(
            Id.CODEC.fieldOf("feature").forGetter(TreeEntry::feature),
            Codec.INT.optionalFieldOf("tries", 3).forGetter(TreeEntry::tries),
            Codec.INT.optionalFieldOf("spacing", 3).forGetter(TreeEntry::spacing)
    ).apply(i, TreeEntry::new));
}
