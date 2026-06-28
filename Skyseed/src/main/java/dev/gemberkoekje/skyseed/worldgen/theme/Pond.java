package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import dev.gemberkoekje.skyseed.compat.Id;

import java.util.List;

/**
 * A small contained pool carved into the island's top centre (a lake/pond). {@code block} is the
 * fluid (default water), resolved at gen time. The pool is walled by the island's domed rim, and is
 * placed without block updates so it stays still and never spills into the void. {@code plants} are
 * per-column water plants scattered through it (lily pads on the surface, kelp/seagrass/coral on the floor);
 * {@code bank} plants grow on the shore ring just outside the pool (e.g. sugar cane), stacked 1-3 tall;
 * {@code water_mobs} are spawned inside the water (squid, axolotls, fish).
 * {@code style} is {@code "pond"} (an irregular blob carved into the centre, default) or
 * {@code "river"} (a meandering channel cut across the island); for a river, {@code radius} is the
 * channel's half-width. Either way the carve is clipped to where the island body can hold it, so it
 * never leaves a floating slab of water at the rim. {@code extent} caps the pond's radius as a fraction
 * of the island radius (default 0.5 — well inside the rim); a higher value (e.g. 0.72) makes a near
 * island-filling lake/ocean with only a thin land rim, for huge water islands. {@code slope}, when set,
 * shapes the floor as a basin — full {@code depth} in a flat centre, shallowing to the shore — so the edge
 * water is shallow (rests on the rim's body instead of spilling off a sheer deep edge) and the shore eases in.
 */
public record Pond(Id block, int radius, int depth, List<GroundEntry> plants,
                   List<GroundEntry> bank, List<MobEntry> waterMobs, String style, float extent, boolean slope) {
    public static final Codec<Pond> CODEC = RecordCodecBuilder.create(i -> i.group(
            Id.CODEC.optionalFieldOf("block", Id.of("minecraft:water")).forGetter(Pond::block),
            Codec.INT.optionalFieldOf("radius", 3).forGetter(Pond::radius),
            Codec.INT.optionalFieldOf("depth", 2).forGetter(Pond::depth),
            GroundEntry.CODEC.listOf().optionalFieldOf("plants", List.of()).forGetter(Pond::plants),
            GroundEntry.CODEC.listOf().optionalFieldOf("bank", List.of()).forGetter(Pond::bank),
            MobEntry.CODEC.listOf().optionalFieldOf("water_mobs", List.of()).forGetter(Pond::waterMobs),
            Codec.STRING.optionalFieldOf("style", "pond").forGetter(Pond::style),
            Codec.FLOAT.optionalFieldOf("extent", 0.5f).forGetter(Pond::extent),
            Codec.BOOL.optionalFieldOf("slope", false).forGetter(Pond::slope)
    ).apply(i, Pond::new));

    public boolean isRiver() {
        return "river".equalsIgnoreCase(style);
    }
}
