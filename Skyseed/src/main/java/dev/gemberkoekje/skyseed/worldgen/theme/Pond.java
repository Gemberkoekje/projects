package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import dev.gemberkoekje.skyseed.compat.Ids;
import net.minecraft.resources.ResourceLocation;

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
 * never leaves a floating slab of water at the rim.
 */
public record Pond(ResourceLocation block, int radius, int depth, List<GroundEntry> plants,
                   List<GroundEntry> bank, List<MobEntry> waterMobs, String style) {
    public static final Codec<Pond> CODEC = RecordCodecBuilder.create(i -> i.group(
            ResourceLocation.CODEC.optionalFieldOf("block", Ids.mc("water")).forGetter(Pond::block),
            Codec.INT.optionalFieldOf("radius", 3).forGetter(Pond::radius),
            Codec.INT.optionalFieldOf("depth", 2).forGetter(Pond::depth),
            GroundEntry.CODEC.listOf().optionalFieldOf("plants", List.of()).forGetter(Pond::plants),
            GroundEntry.CODEC.listOf().optionalFieldOf("bank", List.of()).forGetter(Pond::bank),
            MobEntry.CODEC.listOf().optionalFieldOf("water_mobs", List.of()).forGetter(Pond::waterMobs),
            Codec.STRING.optionalFieldOf("style", "pond").forGetter(Pond::style)
    ).apply(i, Pond::new));

    public boolean isRiver() {
        return "river".equalsIgnoreCase(style);
    }
}
