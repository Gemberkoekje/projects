package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import dev.gemberkoekje.skyseed.compat.Ids;
import net.minecraft.core.Holder;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.tags.TagKey;
import net.minecraft.world.level.biome.Biome;

import java.util.List;
import java.util.Optional;

/**
 * A hard biome exclusion: a seed that fizzles when thrown into one of these {@code biomes} <em>even in a dimension it
 * otherwise implements</em> — for the vanilla rule that bastions never generate in the basalt deltas. {@code biomes}
 * uses the same id / {@code #tag} syntax as {@link BiomeOverride}; {@code message} is an optional translation key
 * shown to the thrower (action bar) so the fizzle reads as deliberate rather than broken. See {@code IslandGenerator
 * #formValidFor}.
 */
public record FizzleRule(List<String> biomes, Optional<String> message) {

    public static final Codec<FizzleRule> CODEC = RecordCodecBuilder.create(i -> i.group(
            Codec.STRING.listOf().fieldOf("biomes").forGetter(FizzleRule::biomes),
            Codec.STRING.optionalFieldOf("message").forGetter(FizzleRule::message)
    ).apply(i, FizzleRule::new));

    /** True if {@code biome} is one of the excluded biomes (so the seed should fizzle there). */
    public boolean matches(Holder<Biome> biome) {
        for (final String entry : biomes) {
            if (entry.startsWith("#")) {
                final ResourceLocation tagId = Ids.parse(entry.substring(1));
                if (tagId != null && biome.is(TagKey.create(Registries.BIOME, tagId))) {
                    return true;
                }
            } else {
                final ResourceLocation id = Ids.parse(entry);
                if (id != null && biome.is(id)) {
                    return true;
                }
            }
        }
        return false;
    }
}
