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
 * {@code jigsaw} optionally assembles a building (or cluster) on the surface from a jigsaw template pool,
 * like a vanilla village — e.g. a villager island's cottage — see {@code SKYVILLAGESPLAN.md}.
 * {@code animals} optionally rolls one weighted pack of farm animals into the jigsaw enclosure's centre,
 * for the dedicated Animal Islands — see {@code SKYANIMALSPLAN.md}.
 * {@code rare_structures} optionally lets a chance-gated structure (igloo, haunted cottage, flooded ruin)
 * germinate in place of the usual island — see {@code SKYSTRUCTURESPLAN.md}.
 * {@code dimensions} declares which dimensions the <em>base</em> config is an implementation for (default
 * {@code [minecraft:overworld]}). A seed thrown in a dimension that is neither in {@code dimensions} nor covered by
 * a dimension-keyed {@link BiomeOverride} fizzles instead of growing the foreign base form — see SKYNETHERPLAN.
 */
public record IslandTheme(Shape shape, Palette palette, List<OreEntry> ores, List<Variant> variants,
                          List<BiomeOverride> biomeOverrides, Optional<Pond> pond, List<MobEntry> mobs,
                          Optional<JigsawConfig> jigsaw, List<AnimalPack> animals, List<RareStructure> rareStructures,
                          Optional<Lava> lava, List<String> dimensions, boolean twin) {

    /** True if this theme's base config is an implementation for {@code dim} (its declared {@code dimensions}). */
    public boolean baseValidIn(ResourceLocation dim) {
        return dimensions.contains(dim.toString());
    }

    public static final Codec<IslandTheme> CODEC = RecordCodecBuilder.create(i -> i.group(
            Shape.CODEC.fieldOf("shape").forGetter(IslandTheme::shape),
            Palette.CODEC.fieldOf("palette").forGetter(IslandTheme::palette),
            OreEntry.CODEC.listOf().optionalFieldOf("ores", List.of()).forGetter(IslandTheme::ores),
            Variant.CODEC.listOf().optionalFieldOf("variants", List.of()).forGetter(IslandTheme::variants),
            BiomeOverride.CODEC.listOf().optionalFieldOf("biome_overrides", List.of()).forGetter(IslandTheme::biomeOverrides),
            Pond.CODEC.optionalFieldOf("pond").forGetter(IslandTheme::pond),
            MobEntry.CODEC.listOf().optionalFieldOf("mobs", List.of()).forGetter(IslandTheme::mobs),
            JigsawConfig.CODEC.optionalFieldOf("jigsaw").forGetter(IslandTheme::jigsaw),
            AnimalPack.CODEC.listOf().optionalFieldOf("animals", List.of()).forGetter(IslandTheme::animals),
            RareStructure.CODEC.listOf().optionalFieldOf("rare_structures", List.of()).forGetter(IslandTheme::rareStructures),
            Lava.CODEC.optionalFieldOf("lava").forGetter(IslandTheme::lava),
            Codec.STRING.listOf().optionalFieldOf("dimensions", List.of("minecraft:overworld")).forGetter(IslandTheme::dimensions),
            // When true, germinating this island also grows a matching island at the vanilla 8:1 dimension-linked
            // coordinate in the other dimension (overworld <-> nether). Used by the Ruined Portal — see SKYNETHERPLAN.
            Codec.BOOL.optionalFieldOf("twin", false).forGetter(IslandTheme::twin)
    ).apply(i, IslandTheme::new));
}
