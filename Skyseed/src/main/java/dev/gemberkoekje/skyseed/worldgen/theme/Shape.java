package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import net.minecraft.core.BlockPos;

import java.util.List;
import java.util.Optional;

/**
 * Silhouette + vertical profile parameters (README → Generation algorithm). {@code maxUnderDepth}, when set, caps how
 * far the teardrop underside hangs below the centre — normally it scales with the radius (~radius deep), so a very wide
 * island would dangle a very deep point; the cap keeps a huge island a wide plateau instead of a bottomless cone.
 * {@code clusterOffsets}, when set, stamps the SAME shape again at each (x,z) offset from the centre — turning one
 * island into a little archipelago of smaller islands (the y of each offset is ignored). A jigsaw placed at the centre
 * then spans them on its own bridges/piers.
 */
public record Shape(IntRange radius, float rimNoise, Underside underside, IntRange topDome,
                    Optional<Integer> maxUnderDepth, List<BlockPos> clusterOffsets) {
    public static final Codec<Shape> CODEC = RecordCodecBuilder.create(i -> i.group(
            IntRange.CODEC.fieldOf("radius").forGetter(Shape::radius),
            Codec.FLOAT.optionalFieldOf("rim_noise", 0.40f).forGetter(Shape::rimNoise),
            Underside.CODEC.optionalFieldOf("underside", Underside.TEARDROP).forGetter(Shape::underside),
            IntRange.CODEC.optionalFieldOf("top_dome", new IntRange(1, 2)).forGetter(Shape::topDome),
            Codec.INT.optionalFieldOf("max_under_depth").forGetter(Shape::maxUnderDepth),
            BlockPos.CODEC.listOf().optionalFieldOf("cluster_offsets", List.of()).forGetter(Shape::clusterOffsets)
    ).apply(i, Shape::new));
}
