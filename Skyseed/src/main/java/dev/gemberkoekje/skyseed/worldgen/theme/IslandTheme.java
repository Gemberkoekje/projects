package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;

import java.util.List;
import java.util.Optional;

/**
 * A datapack-defined island theme: shape, palette, ore table, and weighted decoration variants.
 * This codec is the keystone (README → Configuration) — recipes, the {@code skyseed:theme} component, and the
 * generator all key off the same theme ids. Loaded as the {@code skyseed:theme} datapack registry.
 * {@code structures} is an optional weighted pool of building templates (NBT structures) stamped on the
 * surface — e.g. a villager island's cottage; one is chosen per island — see {@code SKYVILLAGESPLAN.md}.
 */
public record IslandTheme(Shape shape, Palette palette, List<OreEntry> ores, List<Variant> variants,
                          List<BiomeOverride> biomeOverrides, Optional<Pond> pond, List<MobEntry> mobs,
                          List<StructureChoice> structures) {
    public static final Codec<IslandTheme> CODEC = RecordCodecBuilder.create(i -> i.group(
            Shape.CODEC.fieldOf("shape").forGetter(IslandTheme::shape),
            Palette.CODEC.fieldOf("palette").forGetter(IslandTheme::palette),
            OreEntry.CODEC.listOf().optionalFieldOf("ores", List.of()).forGetter(IslandTheme::ores),
            Variant.CODEC.listOf().optionalFieldOf("variants", List.of()).forGetter(IslandTheme::variants),
            BiomeOverride.CODEC.listOf().optionalFieldOf("biome_overrides", List.of()).forGetter(IslandTheme::biomeOverrides),
            Pond.CODEC.optionalFieldOf("pond").forGetter(IslandTheme::pond),
            MobEntry.CODEC.listOf().optionalFieldOf("mobs", List.of()).forGetter(IslandTheme::mobs),
            StructureChoice.CODEC.listOf().optionalFieldOf("structures", List.of()).forGetter(IslandTheme::structures)
    ).apply(i, IslandTheme::new));
}
