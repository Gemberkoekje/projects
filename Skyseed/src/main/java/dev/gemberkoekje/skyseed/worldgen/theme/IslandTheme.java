package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;

import java.util.List;

/**
 * A datapack-defined island theme: shape, palette, ore table, and weighted decoration variants.
 * This codec is the keystone (plan §4) — recipes, the {@code skyseed:theme} component, and the
 * generator all key off the same theme ids. Loaded as the {@code skyseed:theme} datapack registry.
 */
public record IslandTheme(Shape shape, Palette palette, List<OreEntry> ores, List<Variant> variants) {
    public static final Codec<IslandTheme> CODEC = RecordCodecBuilder.create(i -> i.group(
            Shape.CODEC.fieldOf("shape").forGetter(IslandTheme::shape),
            Palette.CODEC.fieldOf("palette").forGetter(IslandTheme::palette),
            OreEntry.CODEC.listOf().optionalFieldOf("ores", List.of()).forGetter(IslandTheme::ores),
            Variant.CODEC.listOf().optionalFieldOf("variants", List.of()).forGetter(IslandTheme::variants)
    ).apply(i, IslandTheme::new));
}
