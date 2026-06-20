package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import net.minecraft.util.RandomSource;

/** An inclusive {@code {min,max}} integer range with a sampler. */
public record IntRange(int min, int max) {
    public static final Codec<IntRange> CODEC = RecordCodecBuilder.create(i -> i.group(
            Codec.INT.fieldOf("min").forGetter(IntRange::min),
            Codec.INT.fieldOf("max").forGetter(IntRange::max)
    ).apply(i, IntRange::new));

    public int sample(RandomSource random) {
        return max <= min ? min : min + random.nextInt(max - min + 1);
    }
}
