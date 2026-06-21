package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import net.minecraft.resources.ResourceLocation;

/**
 * A theme's jigsaw building config: the generator levels a pad and assembles a structure from the
 * {@code pool} (a vanilla {@code worldgen/template_pool}) at the island centre, exactly like a vanilla
 * village. {@code target} is the start piece's anchor jigsaw name, {@code depth} the jigsaw recursion
 * limit (1 = just the start piece), {@code pad} the half-width of the levelled foundation. See
 * {@code SKYVILLAGESPLAN.md}.
 */
public record JigsawConfig(ResourceLocation pool, ResourceLocation target, int depth, int pad) {
    public static final Codec<JigsawConfig> CODEC = RecordCodecBuilder.create(i -> i.group(
            ResourceLocation.CODEC.fieldOf("pool").forGetter(JigsawConfig::pool),
            ResourceLocation.CODEC.optionalFieldOf("target", ResourceLocation.withDefaultNamespace("bottom")).forGetter(JigsawConfig::target),
            Codec.INT.optionalFieldOf("depth", 1).forGetter(JigsawConfig::depth),
            Codec.INT.optionalFieldOf("pad", 6).forGetter(JigsawConfig::pad)
    ).apply(i, JigsawConfig::new));
}
