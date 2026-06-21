package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;

import java.util.List;

/** What grows on a variant's surface: count-based trees plus per-column ground cover (README → Generation algorithm). */
public record Decoration(List<TreeEntry> trees, List<GroundEntry> ground) {
    public static final Decoration EMPTY = new Decoration(List.of(), List.of());

    public static final Codec<Decoration> CODEC = RecordCodecBuilder.create(i -> i.group(
            TreeEntry.CODEC.listOf().optionalFieldOf("trees", List.of()).forGetter(Decoration::trees),
            GroundEntry.CODEC.listOf().optionalFieldOf("ground", List.of()).forGetter(Decoration::ground)
    ).apply(i, Decoration::new));
}
