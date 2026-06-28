package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import dev.gemberkoekje.skyseed.compat.Id;

import java.util.List;

/**
 * One weighted option for an Animal Island's enclosure: when this pack is rolled, every {@link Entry} spawns
 * its {@code adults} + {@code babies} inside the pen. A theme's {@code animals} list holds several packs and
 * exactly one is chosen (e.g. a Pasture rolls cows OR sheep OR pigs). See {@code SKYANIMALSPLAN.md}.
 */
public record AnimalPack(int weight, List<Entry> entries) {
    public static final Codec<AnimalPack> CODEC = RecordCodecBuilder.create(i -> i.group(
            Codec.INT.optionalFieldOf("weight", 1).forGetter(AnimalPack::weight),
            Entry.CODEC.listOf().fieldOf("entries").forGetter(AnimalPack::entries)
    ).apply(i, AnimalPack::new));

    public record Entry(Id entity, int adults, int babies) {
        public static final Codec<Entry> CODEC = RecordCodecBuilder.create(i -> i.group(
                Id.CODEC.fieldOf("entity").forGetter(Entry::entity),
                Codec.INT.optionalFieldOf("adults", 1).forGetter(Entry::adults),
                Codec.INT.optionalFieldOf("babies", 0).forGetter(Entry::babies)
        ).apply(i, Entry::new));
    }
}
