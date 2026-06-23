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
 * A conditional tweak applied on top of a theme's base config when a seed germinates somewhere that
 * matches. A match requires the biome to be in {@code biomes} (empty = any biome) AND the germination
 * Y to be within {@code min_y}..{@code max_y} (each optional) AND, when {@code dimension} is set, the
 * germination dimension to equal it (e.g. {@code minecraft:the_nether} — this is how an overworld seed
 * adapts to its Nether form, see SKYNETHERPLAN). Every other field is optional and, when present, replaces
 * the base theme's corresponding field for that island. First matching override wins.
 */
public record BiomeOverride(
        List<String> biomes,
        Optional<Integer> minY,
        Optional<Integer> maxY,
        Optional<ResourceLocation> surface,
        Optional<ResourceLocation> fill,
        Optional<ResourceLocation> core,
        Optional<Integer> fillDepth,
        Optional<List<GroundEntry>> surfaceScatter,
        Optional<Shape> shape,
        Optional<List<OreEntry>> ores,
        Optional<List<Variant>> variants,
        Optional<Pond> pond,
        Optional<Integer> waterfalls,
        Optional<List<MobEntry>> mobs,
        Optional<String> dimension,
        Optional<List<ResourceLocation>> fillBands) {

    public static final Codec<BiomeOverride> CODEC = RecordCodecBuilder.create(i -> i.group(
            Codec.STRING.listOf().optionalFieldOf("biomes", List.of()).forGetter(BiomeOverride::biomes),
            Codec.INT.optionalFieldOf("min_y").forGetter(BiomeOverride::minY),
            Codec.INT.optionalFieldOf("max_y").forGetter(BiomeOverride::maxY),
            ResourceLocation.CODEC.optionalFieldOf("surface").forGetter(BiomeOverride::surface),
            ResourceLocation.CODEC.optionalFieldOf("fill").forGetter(BiomeOverride::fill),
            ResourceLocation.CODEC.optionalFieldOf("core").forGetter(BiomeOverride::core),
            Codec.INT.optionalFieldOf("fill_depth").forGetter(BiomeOverride::fillDepth),
            GroundEntry.CODEC.listOf().optionalFieldOf("surface_scatter").forGetter(BiomeOverride::surfaceScatter),
            Shape.CODEC.optionalFieldOf("shape").forGetter(BiomeOverride::shape),
            OreEntry.CODEC.listOf().optionalFieldOf("ores").forGetter(BiomeOverride::ores),
            Variant.CODEC.listOf().optionalFieldOf("variants").forGetter(BiomeOverride::variants),
            Pond.CODEC.optionalFieldOf("pond").forGetter(BiomeOverride::pond),
            Codec.INT.optionalFieldOf("waterfalls").forGetter(BiomeOverride::waterfalls),
            MobEntry.CODEC.listOf().optionalFieldOf("mobs").forGetter(BiomeOverride::mobs),
            Codec.STRING.optionalFieldOf("dimension").forGetter(BiomeOverride::dimension),
            ResourceLocation.CODEC.listOf().optionalFieldOf("fill_bands").forGetter(BiomeOverride::fillBands)
    ).apply(i, BiomeOverride::new));

    /** True if {@code dim} (when dimension is set), {@code biome} (when biomes is set) and {@code y} (when a range is set) all match. */
    public boolean matches(Holder<Biome> biome, int y, ResourceLocation dim) {
        if (dimension.isPresent() && (dim == null || !dimension.get().equals(dim.toString()))) {
            return false;
        }
        if (!biomes.isEmpty() && !matchesBiome(biome)) {
            return false;
        }
        if (minY.isPresent() && y < minY.get()) {
            return false;
        }
        return maxY.isEmpty() || y <= maxY.get();
    }

    private boolean matchesBiome(Holder<Biome> biome) {
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
