package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import net.minecraft.resources.ResourceLocation;

/** The three layered blocks plus the dirt-band thickness (plan §5 step 2). Block ids resolved at gen time. */
public record Palette(ResourceLocation surface, ResourceLocation fill, ResourceLocation core, int fillDepth) {
    public static final Codec<Palette> CODEC = RecordCodecBuilder.create(i -> i.group(
            ResourceLocation.CODEC.fieldOf("surface").forGetter(Palette::surface),
            ResourceLocation.CODEC.fieldOf("fill").forGetter(Palette::fill),
            ResourceLocation.CODEC.fieldOf("core").forGetter(Palette::core),
            Codec.INT.optionalFieldOf("fill_depth", 3).forGetter(Palette::fillDepth)
    ).apply(i, Palette::new));
}
