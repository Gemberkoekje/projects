package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import net.minecraft.resources.ResourceLocation;

import java.util.List;

/**
 * A small contained pool carved into the island's top centre (a lake/pond). {@code block} is the
 * fluid (default water), resolved at gen time. The pool is walled by the island's domed rim, and is
 * placed without block updates so it stays still and never spills into the void. {@code plants} are
 * per-column water plants scattered through it (lily pads on the surface, kelp/seagrass/coral on the floor);
 * {@code bank} plants grow on the shore ring just outside the pool (e.g. sugar cane), stacked 1-3 tall.
 */
public record Pond(ResourceLocation block, int radius, int depth, List<GroundEntry> plants, List<GroundEntry> bank) {
    public static final Codec<Pond> CODEC = RecordCodecBuilder.create(i -> i.group(
            ResourceLocation.CODEC.optionalFieldOf("block", ResourceLocation.withDefaultNamespace("water")).forGetter(Pond::block),
            Codec.INT.optionalFieldOf("radius", 3).forGetter(Pond::radius),
            Codec.INT.optionalFieldOf("depth", 2).forGetter(Pond::depth),
            GroundEntry.CODEC.listOf().optionalFieldOf("plants", List.of()).forGetter(Pond::plants),
            GroundEntry.CODEC.listOf().optionalFieldOf("bank", List.of()).forGetter(Pond::bank)
    ).apply(i, Pond::new));
}
