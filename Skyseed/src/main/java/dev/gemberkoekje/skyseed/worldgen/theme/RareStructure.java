package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import dev.gemberkoekje.skyseed.compat.Id;
import dev.gemberkoekje.skyseed.compat.Lookup;
import net.minecraft.core.Holder;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.world.level.biome.Biome;

import java.util.List;
import java.util.Optional;

/**
 * A rare structure that occasionally germinates on an otherwise ordinary island — the surprise igloo on a
 * Frozen isle, the haunted cottage on a Hamlet, the flooded ruin on an Aquatic isle, the buried temple on a
 * Large Desert. When its {@code chance} rolls true, the rolled structure's {@code jigsaw} replaces the theme's
 * normal jigsaw (or becomes the only one, if the theme has none) and its {@code mobs} replace the theme's
 * animal packs. At most one rare structure is chosen per island (the first that rolls). {@code suppress_pond}
 * skips the theme's pond carve so a self-contained flooded ruin can stand in its place. {@code biomes} (empty =
 * any) gates the roll to matching germination biomes — same id/{@code #tag} syntax as {@link BiomeOverride},
 * e.g. a jungle temple only on a Large Forest grown in {@code #minecraft:is_jungle}. See
 * {@code SKYSTRUCTURESPLAN.md}.
 */
public record RareStructure(float chance, JigsawConfig jigsaw, List<AnimalPack> mobs, boolean suppressPond,
                            List<String> biomes, Optional<Id> twin, Optional<String> dimension) {
    public static final Codec<RareStructure> CODEC = RecordCodecBuilder.create(i -> i.group(
            Codec.FLOAT.fieldOf("chance").forGetter(RareStructure::chance),
            JigsawConfig.CODEC.fieldOf("jigsaw").forGetter(RareStructure::jigsaw),
            AnimalPack.CODEC.listOf().optionalFieldOf("mobs", List.of()).forGetter(RareStructure::mobs),
            Codec.BOOL.optionalFieldOf("suppress_pond", false).forGetter(RareStructure::suppressPond),
            Codec.STRING.listOf().optionalFieldOf("biomes", List.of()).forGetter(RareStructure::biomes),
            // If present, a roll of this rare structure also grows the named theme at the dimension-linked
            // coordinate in the other dimension — so a ruined portal rolled on a big island gets its twin too.
            Id.CODEC.optionalFieldOf("twin").forGetter(RareStructure::twin),
            // A roll gate by dimension: when set, this rare structure rolls only in that dimension — so a Nether-native
            // seed can carry an overworld easter egg (the Trading Post growing the abandoned cottage thrown topside).
            // Unset, it follows the theme's home dimension. See rollsIn.
            Codec.STRING.optionalFieldOf("dimension").forGetter(RareStructure::dimension)
    ).apply(i, RareStructure::new));

    /** Whether this rare structure may roll in {@code dim}: only its own {@code dimension} when set, else the theme's
     * home dimension ({@code baseValidHere}). */
    public boolean rollsIn(ResourceLocation dim, boolean baseValidHere) {
        return dimension.isPresent() ? dimension.get().equals(dim.toString()) : baseValidHere;
    }

    /** True if this rare structure may roll in {@code biome} (no {@code biomes} filter set = any biome). */
    public boolean matchesBiome(Holder<Biome> biome) {
        if (biomes.isEmpty()) {
            return true;
        }
        for (final String entry : biomes) {
            if (Lookup.biomeMatches(biome, entry)) {
                return true;
            }
        }
        return false;
    }
}
