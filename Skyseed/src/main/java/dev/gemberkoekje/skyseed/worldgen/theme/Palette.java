package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import net.minecraft.resources.ResourceLocation;

import java.util.List;

/**
 * The three layered blocks plus the dirt-band thickness (README → Generation algorithm). {@code surface_scatter}
 * optionally mixes other blocks into the surface per column (e.g. some sand among dirt). Block ids
 * resolved at gen time.
 */
public record Palette(ResourceLocation surface, ResourceLocation fill, ResourceLocation core, int fillDepth,
                      List<GroundEntry> surfaceScatter) {
    public static final Codec<Palette> CODEC = RecordCodecBuilder.create(i -> i.group(
            ResourceLocation.CODEC.fieldOf("surface").forGetter(Palette::surface),
            ResourceLocation.CODEC.fieldOf("fill").forGetter(Palette::fill),
            ResourceLocation.CODEC.fieldOf("core").forGetter(Palette::core),
            Codec.INT.optionalFieldOf("fill_depth", 3).forGetter(Palette::fillDepth),
            GroundEntry.CODEC.listOf().optionalFieldOf("surface_scatter", List.of()).forGetter(Palette::surfaceScatter)
    ).apply(i, Palette::new));
}
