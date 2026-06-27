package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import dev.gemberkoekje.skyseed.compat.Ids;

import java.util.List;
import java.util.Optional;

/**
 * Optional lava content for a theme, orthogonal to the biome-override palette bands so it doesn't have to
 * duplicate them. Two parts:
 * <ul>
 *   <li>a {@code vein} — a small cluster of lava grown into the core like an ore ({@code vein_chance} to roll,
 *       {@code vein_size} blocks), so it shows up at every throw height regardless of the band's ore table;</li>
 *   <li>{@code lakes} — a list of throw-height-banded lava-lake chances. The first band whose [{@code min_y},
 *       {@code max_y}] contains the throw height is rolled; a hit carves a lava pool (like a pond) and
 *       <em>suppresses</em> the theme's normal water pond (so e.g. a sub-zero Aquatic comes up as a stone island
 *       with a lava lake instead of a water one).</li>
 * </ul>
 */
public record Lava(float veinChance, IntRange veinSize, List<Lake> lakes) {
    public static final Codec<Lava> CODEC = RecordCodecBuilder.create(i -> i.group(
            Codec.FLOAT.optionalFieldOf("vein_chance", 0.0f).forGetter(Lava::veinChance),
            IntRange.CODEC.optionalFieldOf("vein_size", new IntRange(4, 8)).forGetter(Lava::veinSize),
            Lake.CODEC.listOf().optionalFieldOf("lakes", List.of()).forGetter(Lava::lakes)
    ).apply(i, Lava::new));

    /** A lava-lake chance for one throw-height band: matches Y in [{@code minY}, {@code maxY}] (either bound optional). */
    public record Lake(Optional<Integer> minY, Optional<Integer> maxY, float chance, int radius, int depth) {
        public static final Codec<Lake> CODEC = RecordCodecBuilder.create(i -> i.group(
                Codec.INT.optionalFieldOf("min_y").forGetter(Lake::minY),
                Codec.INT.optionalFieldOf("max_y").forGetter(Lake::maxY),
                Codec.FLOAT.fieldOf("chance").forGetter(Lake::chance),
                Codec.INT.optionalFieldOf("radius", 4).forGetter(Lake::radius),
                Codec.INT.optionalFieldOf("depth", 2).forGetter(Lake::depth)
        ).apply(i, Lake::new));

        public boolean matches(int y) {
            return (minY.isEmpty() || y >= minY.get()) && (maxY.isEmpty() || y <= maxY.get());
        }

        /** This band as a {@link Pond} of lava (no plants/banks/mobs) for the shared pond-carving code. */
        public Pond toPond() {
            return new Pond(Ids.mc("lava"), radius, depth,
                    List.of(), List.of(), List.of(), "pond", 0.5f, false);
        }
    }
}
