package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import dev.gemberkoekje.skyseed.compat.Id;

/**
 * A mob to sprinkle onto the island at generation time: presence roll {@code chance}, then {@code count}
 * of them spawned on the surface. Spawned directly (not via natural spawning), so it ignores light/time.
 */
public record MobEntry(Id entity, float chance, IntRange count) {
    public static final Codec<MobEntry> CODEC = RecordCodecBuilder.create(i -> i.group(
            Id.CODEC.fieldOf("entity").forGetter(MobEntry::entity),
            Codec.FLOAT.fieldOf("chance").forGetter(MobEntry::chance),
            IntRange.CODEC.fieldOf("count").forGetter(MobEntry::count)
    ).apply(i, MobEntry::new));
}
