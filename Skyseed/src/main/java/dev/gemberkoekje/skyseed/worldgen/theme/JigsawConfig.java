package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import dev.gemberkoekje.skyseed.compat.Ids;
import net.minecraft.resources.ResourceLocation;

/**
 * A theme's jigsaw building config: the generator levels a pad and assembles a structure from the
 * {@code pool} (a vanilla {@code worldgen/template_pool}) at the island centre, exactly like a vanilla
 * village. {@code target} is the start piece's anchor jigsaw name, {@code depth} the jigsaw recursion
 * limit (1 = just the start piece), {@code pad} the half-width of the levelled foundation,
 * {@code ironGolems} how many golems to spawn at the centre once assembled (the Village Center's guard), and
 * {@code sink} how many blocks below the levelled surface to seat the structure (0 = flush on the pad; 1 buries
 * it one block under the island's own surface, so e.g. a temple roof hides under the sand and only its hole
 * shows). See {@code SKYVILLAGESPLAN.md} / {@code SKYSTRUCTURESPLAN.md}.
 */
public record JigsawConfig(ResourceLocation pool, ResourceLocation target, int depth, int pad, int ironGolems, int sink) {
    public static final Codec<JigsawConfig> CODEC = RecordCodecBuilder.create(i -> i.group(
            ResourceLocation.CODEC.fieldOf("pool").forGetter(JigsawConfig::pool),
            ResourceLocation.CODEC.optionalFieldOf("target", Ids.mc("bottom")).forGetter(JigsawConfig::target),
            Codec.INT.optionalFieldOf("depth", 1).forGetter(JigsawConfig::depth),
            Codec.INT.optionalFieldOf("pad", 6).forGetter(JigsawConfig::pad),
            Codec.INT.optionalFieldOf("iron_golems", 0).forGetter(JigsawConfig::ironGolems),
            Codec.INT.optionalFieldOf("sink", 0).forGetter(JigsawConfig::sink)
    ).apply(i, JigsawConfig::new));
}
