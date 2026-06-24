package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;

/**
 * Optional "way down" punched through an island's centre: a ladder shaft dropping {@code depth} blocks below the
 * island to a small cobblestone landing, so you can reach mining level without bridging out into the void. Applied
 * in every dimension the seed grows in (it's a structural feature, not biome content). See {@code ShaftPlanner}.
 *
 * <ul>
 *   <li>{@code depth} — how many blocks the cobblestone-backed shaft hangs below the island before the landing.</li>
 *   <li>{@code landing_radius} — half-width of the square cobblestone landing ({@code 2} → a 5×5).</li>
 *   <li>{@code waterfall_chance} — chance the shaft comes up as a water column instead of ladders (a fun easter
 *       egg), with the landing's centre left open as a drain.</li>
 * </ul>
 */
public record LadderShaft(int depth, int landingRadius, float waterfallChance) {

    public static final Codec<LadderShaft> CODEC = RecordCodecBuilder.create(i -> i.group(
            Codec.INT.optionalFieldOf("depth", 20).forGetter(LadderShaft::depth),
            Codec.INT.optionalFieldOf("landing_radius", 2).forGetter(LadderShaft::landingRadius),
            Codec.FLOAT.optionalFieldOf("waterfall_chance", 0.05f).forGetter(LadderShaft::waterfallChance)
    ).apply(i, LadderShaft::new));
}
