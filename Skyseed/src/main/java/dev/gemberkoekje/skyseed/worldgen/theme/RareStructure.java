package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;

import java.util.List;

/**
 * A rare structure that occasionally germinates on an otherwise ordinary island — the surprise igloo on a
 * Frozen isle, the haunted cottage on a Hamlet, the flooded ruin on an Aquatic isle. When its {@code chance}
 * rolls true, the rolled structure's {@code jigsaw} replaces the theme's normal jigsaw (or becomes the only
 * one, if the theme has none) and its {@code mobs} replace the theme's animal packs. At most one rare
 * structure is chosen per island (the first that rolls). {@code suppress_pond} skips the theme's pond carve
 * so a self-contained flooded ruin can stand in its place. See {@code SKYSTRUCTURESPLAN.md}.
 */
public record RareStructure(float chance, JigsawConfig jigsaw, List<AnimalPack> mobs, boolean suppressPond) {
    public static final Codec<RareStructure> CODEC = RecordCodecBuilder.create(i -> i.group(
            Codec.FLOAT.fieldOf("chance").forGetter(RareStructure::chance),
            JigsawConfig.CODEC.fieldOf("jigsaw").forGetter(RareStructure::jigsaw),
            AnimalPack.CODEC.listOf().optionalFieldOf("mobs", List.of()).forGetter(RareStructure::mobs),
            Codec.BOOL.optionalFieldOf("suppress_pond", false).forGetter(RareStructure::suppressPond)
    ).apply(i, RareStructure::new));
}
