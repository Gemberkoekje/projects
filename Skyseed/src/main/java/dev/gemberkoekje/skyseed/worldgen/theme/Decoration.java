package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;

import java.util.List;

/**
 * What grows on a variant: count-based trees, per-column ground cover, and per-column {@code underside}
 * features that hang from the island's bottom (dripstone, cave vines, spore blossoms, hanging roots).
 * (README → Generation algorithm.)
 */
public record Decoration(List<TreeEntry> trees, List<GroundEntry> ground, List<GroundEntry> underside) {
    public static final Decoration EMPTY = new Decoration(List.of(), List.of(), List.of());

    public static final Codec<Decoration> CODEC = RecordCodecBuilder.create(i -> i.group(
            TreeEntry.CODEC.listOf().optionalFieldOf("trees", List.of()).forGetter(Decoration::trees),
            GroundEntry.CODEC.listOf().optionalFieldOf("ground", List.of()).forGetter(Decoration::ground),
            GroundEntry.CODEC.listOf().optionalFieldOf("underside", List.of()).forGetter(Decoration::underside)
    ).apply(i, Decoration::new));
}
