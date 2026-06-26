package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import dev.gemberkoekje.skyseed.compat.Ids;
import net.minecraft.resources.ResourceLocation;

import java.util.Optional;

/**
 * A theme's jigsaw building config: the generator levels a pad and assembles a structure from the
 * {@code pool} (a vanilla {@code worldgen/template_pool}) at the island centre, exactly like a vanilla
 * village. {@code target} is the start piece's anchor jigsaw name, {@code depth} the jigsaw recursion
 * limit (1 = just the start piece), {@code pad} the half-width of the levelled foundation,
 * {@code ironGolems} how many golems to spawn at the centre once assembled (the Village Center's guard), and
 * {@code sink} how many blocks below the levelled surface to seat the structure (0 = flush on the pad; 1 buries
 * it one block under the island's own surface, so e.g. a temple roof hides under the sand and only its hole
 * shows), and {@code reach} the horizontal half-extent the post-assembly passes scan around the origin — the
 * connection-link pass and, when {@code reach > 0}, the path/bridge marker surfacing (SKYJIGSAWPLAN §3a). It is
 * the size of the assembled structure, so a deep, sprawling jigsaw (a village, a fortress) must declare a reach
 * wide enough to cover where its pieces land; {@code 0} (the default) means a solid structure that lays no path
 * markers and links within the normal radius. Finally {@code capPrefix} / {@code capCount} bound a family of
 * elements: after the jigsaw assembles, any piece whose element name contains {@code capPrefix} beyond the cap
 * nearest the centre is dropped before stamping, so a trade post can run long streets full of fields yet keep its
 * shops to a handful (vanilla has no native per-element limit). The cap is {@code capCount}, unless {@code capMin}
 * is set in {@code [1, capCount)}, in which case the generator rolls a target in {@code [capMin, capCount]} from the
 * island RNG up front (so e.g. a trade post lands a reproducible-but-varied 2–4 shops). To <em>guarantee</em> the
 * count rather than only trim a surplus, make the lot pool entirely the capped element and set {@code capFiller} to
 * a pool of replacements (fields/gardens): the surplus lots beyond the cap are re-stamped from that pool, so the
 * planned number of shops always lands when that many lots placed. {@code capCount = 0} (the default) disables it,
 * and an empty {@code capFiller} drops the surplus instead. Finally {@code centerpiece} is an optional block stamped
 * at the very centre of the assembled structure (at the origin — the centre of the start piece's floor, where its
 * lantern sits), with any centre guard golem posted a couple of blocks aside: the Village Center's anvil capstone.
 * See {@code SKYVILLAGESPLAN.md} / {@code SKYJIGSAWPLAN.md}.
 */
public record JigsawConfig(ResourceLocation pool, ResourceLocation target, int depth, int pad, int ironGolems,
                           int sink, int reach, String capPrefix, int capCount, int capMin, String capFiller,
                           Optional<ResourceLocation> centerpiece) {
    public static final Codec<JigsawConfig> CODEC = RecordCodecBuilder.create(i -> i.group(
            ResourceLocation.CODEC.fieldOf("pool").forGetter(JigsawConfig::pool),
            ResourceLocation.CODEC.optionalFieldOf("target", Ids.mc("bottom")).forGetter(JigsawConfig::target),
            Codec.INT.optionalFieldOf("depth", 1).forGetter(JigsawConfig::depth),
            Codec.INT.optionalFieldOf("pad", 6).forGetter(JigsawConfig::pad),
            Codec.INT.optionalFieldOf("iron_golems", 0).forGetter(JigsawConfig::ironGolems),
            Codec.INT.optionalFieldOf("sink", 0).forGetter(JigsawConfig::sink),
            Codec.INT.optionalFieldOf("reach", 0).forGetter(JigsawConfig::reach),
            Codec.STRING.optionalFieldOf("cap_prefix", "").forGetter(JigsawConfig::capPrefix),
            Codec.INT.optionalFieldOf("cap_count", 0).forGetter(JigsawConfig::capCount),
            Codec.INT.optionalFieldOf("cap_min", 0).forGetter(JigsawConfig::capMin),
            Codec.STRING.optionalFieldOf("cap_filler", "").forGetter(JigsawConfig::capFiller),
            ResourceLocation.CODEC.optionalFieldOf("centerpiece").forGetter(JigsawConfig::centerpiece)
    ).apply(i, JigsawConfig::new));
}
