package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import net.minecraft.resources.ResourceLocation;

import java.util.List;
import java.util.Optional;

/**
 * A datapack-defined island theme: shape, palette, ore table, and weighted decoration variants.
 * This codec is the keystone (README → Configuration) — recipes, the {@code skyseed:theme} component, and the
 * generator all key off the same theme ids. Loaded as the {@code skyseed:theme} datapack registry.
 * {@code structure} optionally names a hand-built feature stamped on the surface (e.g. a villager
 * island's cottage) — see {@code SKYVILLAGESPLAN.md}.
 */
public record IslandTheme(Shape shape, Palette palette, List<OreEntry> ores, List<Variant> variants,
                          List<BiomeOverride> biomeOverrides, Optional<Pond> pond, List<MobEntry> mobs,
                          Optional<ResourceLocation> structure) {
    public static final Codec<IslandTheme> CODEC = RecordCodecBuilder.create(i -> i.group(
            Shape.CODEC.fieldOf("shape").forGetter(IslandTheme::shape),
            Palette.CODEC.fieldOf("palette").forGetter(IslandTheme::palette),
            OreEntry.CODEC.listOf().optionalFieldOf("ores", List.of()).forGetter(IslandTheme::ores),
            Variant.CODEC.listOf().optionalFieldOf("variants", List.of()).forGetter(IslandTheme::variants),
            BiomeOverride.CODEC.listOf().optionalFieldOf("biome_overrides", List.of()).forGetter(IslandTheme::biomeOverrides),
            Pond.CODEC.optionalFieldOf("pond").forGetter(IslandTheme::pond),
            MobEntry.CODEC.listOf().optionalFieldOf("mobs", List.of()).forGetter(IslandTheme::mobs),
            ResourceLocation.CODEC.optionalFieldOf("structure").forGetter(IslandTheme::structure)
    ).apply(i, IslandTheme::new));
}
