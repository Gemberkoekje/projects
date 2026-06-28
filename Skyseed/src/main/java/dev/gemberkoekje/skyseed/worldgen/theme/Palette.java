package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import dev.gemberkoekje.skyseed.compat.Id;

import java.util.List;

/**
 * The three layered blocks plus the dirt-band thickness (README → Generation algorithm). {@code surface_scatter}
 * optionally mixes other blocks into the surface per column (e.g. some sand among dirt). {@code fill_bands},
 * when set, replaces the body (fill + core) with a Y-cycled list of blocks {@code band_thickness} tall each —
 * horizontal strata like a badlands cliff; the core still seeds ores normally. {@code snow} (0–1) is the per-column
 * probability the generator drapes a snow layer over the highest block of every column once the whole island is built
 * — ground, building roofs and tree tops alike (a cold-biome island): {@code 1.0} caps every column, a lower value
 * leaves icy patches showing. {@code 0} (the default) is off. Block ids resolved at gen time.
 */
public record Palette(Id surface, Id fill, Id core, int fillDepth,
                      List<GroundEntry> surfaceScatter, List<Id> fillBands, int bandThickness,
                      float snow) {
    public static final Codec<Palette> CODEC = RecordCodecBuilder.create(i -> i.group(
            Id.CODEC.fieldOf("surface").forGetter(Palette::surface),
            Id.CODEC.fieldOf("fill").forGetter(Palette::fill),
            Id.CODEC.fieldOf("core").forGetter(Palette::core),
            Codec.INT.optionalFieldOf("fill_depth", 3).forGetter(Palette::fillDepth),
            GroundEntry.CODEC.listOf().optionalFieldOf("surface_scatter", List.of()).forGetter(Palette::surfaceScatter),
            Id.CODEC.listOf().optionalFieldOf("fill_bands", List.of()).forGetter(Palette::fillBands),
            Codec.INT.optionalFieldOf("band_thickness", 2).forGetter(Palette::bandThickness),
            Codec.FLOAT.optionalFieldOf("snow", 0.0f).forGetter(Palette::snow)
    ).apply(i, Palette::new));
}
