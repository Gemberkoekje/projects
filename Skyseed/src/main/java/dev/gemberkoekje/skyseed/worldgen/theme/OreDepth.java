package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import net.minecraft.util.StringRepresentable;

/** Where an ore may seed: anywhere in the core, or only the lower part (deep_core). */
public enum OreDepth implements StringRepresentable {
    CORE("core"),
    DEEP_CORE("deep_core");

    public static final Codec<OreDepth> CODEC = StringRepresentable.fromEnum(OreDepth::values);

    private final String name;

    OreDepth(String name) {
        this.name = name;
    }

    @Override
    public String getSerializedName() {
        return name;
    }
}
