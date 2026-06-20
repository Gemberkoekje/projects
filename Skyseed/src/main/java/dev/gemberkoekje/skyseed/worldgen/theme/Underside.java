package dev.gemberkoekje.skyseed.worldgen.theme;

import com.mojang.serialization.Codec;
import net.minecraft.util.StringRepresentable;

/** Vertical underside profile. Only {@code teardrop} for now; the enum leaves room for more. */
public enum Underside implements StringRepresentable {
    TEARDROP("teardrop");

    public static final Codec<Underside> CODEC = StringRepresentable.fromEnum(Underside::values);

    private final String name;

    Underside(String name) {
        this.name = name;
    }

    @Override
    public String getSerializedName() {
        return name;
    }
}
