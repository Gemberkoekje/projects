package dev.gemberkoekje.skyseed.compat;

import dev.gemberkoekje.skyseed.Skyseed;
//? if >=26.1.2 {
/*import net.minecraft.resources.Identifier;*/
//?} else {
import net.minecraft.resources.ResourceLocation;
//?}
import org.jetbrains.annotations.Nullable;

/**
 * Version-volatile namespaced-id construction, isolated behind stable signatures. The Minecraft id type was renamed
 * {@code net.minecraft.resources.ResourceLocation} → {@code net.minecraft.resources.Identifier} in 26.1.2 — a pure
 * rename: same package, same factory methods ({@code fromNamespaceAndPath}/{@code withDefaultNamespace}/
 * {@code tryParse}). So every id the mod builds goes through here and the per-version swap lives in this file (and in
 * {@link Lookup} / {@link Jigsaw}), never in the algorithm. See {@code REFACTORPLAN.md} §2.6.
 */
public final class Ids {

    private Ids() {
    }

    //? if >=26.1.2 {
    /*public static Identifier of(String namespace, String path) {
        return Identifier.fromNamespaceAndPath(namespace, path);
    }

    public static Identifier mod(String path) {
        return of(Skyseed.MODID, path);
    }

    public static Identifier mc(String path) {
        return Identifier.withDefaultNamespace(path);
    }

    @Nullable
    public static Identifier parse(String s) {
        return Identifier.tryParse(s);
    }
    *///?} else {
    /** An id in an explicit namespace. */
    public static ResourceLocation of(String namespace, String path) {
        return ResourceLocation.fromNamespaceAndPath(namespace, path);
    }

    /** An id in the Skyseed namespace ({@code skyseed:<path>}). */
    public static ResourceLocation mod(String path) {
        return of(Skyseed.MODID, path);
    }

    /** An id in the vanilla namespace ({@code minecraft:<path>}). */
    public static ResourceLocation mc(String path) {
        return ResourceLocation.withDefaultNamespace(path);
    }

    /** Parse a namespaced id from a string; {@code null} if it is malformed. */
    @Nullable
    public static ResourceLocation parse(String s) {
        return ResourceLocation.tryParse(s);
    }
    //?}
}
