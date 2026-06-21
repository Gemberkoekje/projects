package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import net.minecraft.core.Holder;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.tags.TagKey;
import net.minecraft.world.level.biome.Biome;

import java.util.List;
import java.util.Optional;

/**
 * A per-biome tweak applied on top of a theme's base config when a seed germinates in a matching
 * biome (plan: "set up biomes to be (slightly) different"). {@code biomes} entries are either a biome
 * id ({@code minecraft:plains}) or a biome tag ({@code #minecraft:is_savanna}). Every other field is
 * optional and, when present, replaces the base theme's corresponding field for that island.
 */
public record BiomeOverride(
        List<String> biomes,
        Optional<ResourceLocation> surface,
        Optional<ResourceLocation> fill,
        Optional<ResourceLocation> core,
        Optional<Integer> fillDepth,
        Optional<List<GroundEntry>> surfaceScatter,
        Optional<Shape> shape,
        Optional<List<OreEntry>> ores,
        Optional<List<Variant>> variants,
        Optional<Pond> pond) {

    public static final Codec<BiomeOverride> CODEC = RecordCodecBuilder.create(i -> i.group(
            Codec.STRING.listOf().fieldOf("biomes").forGetter(BiomeOverride::biomes),
            ResourceLocation.CODEC.optionalFieldOf("surface").forGetter(BiomeOverride::surface),
            ResourceLocation.CODEC.optionalFieldOf("fill").forGetter(BiomeOverride::fill),
            ResourceLocation.CODEC.optionalFieldOf("core").forGetter(BiomeOverride::core),
            Codec.INT.optionalFieldOf("fill_depth").forGetter(BiomeOverride::fillDepth),
            GroundEntry.CODEC.listOf().optionalFieldOf("surface_scatter").forGetter(BiomeOverride::surfaceScatter),
            Shape.CODEC.optionalFieldOf("shape").forGetter(BiomeOverride::shape),
            OreEntry.CODEC.listOf().optionalFieldOf("ores").forGetter(BiomeOverride::ores),
            Variant.CODEC.listOf().optionalFieldOf("variants").forGetter(BiomeOverride::variants),
            Pond.CODEC.optionalFieldOf("pond").forGetter(BiomeOverride::pond)
    ).apply(i, BiomeOverride::new));

    /** True if {@code biome} matches any of this override's biome ids/tags. */
    public boolean matches(Holder<Biome> biome) {
        for (String entry : biomes) {
            if (entry.startsWith("#")) {
                ResourceLocation tagId = ResourceLocation.tryParse(entry.substring(1));
                if (tagId != null && biome.is(TagKey.create(Registries.BIOME, tagId))) {
                    return true;
                }
            } else {
                ResourceLocation id = ResourceLocation.tryParse(entry);
                if (id != null && biome.is(id)) {
                    return true;
                }
            }
        }
        return false;
    }
}
