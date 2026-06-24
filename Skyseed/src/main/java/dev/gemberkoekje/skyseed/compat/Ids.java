package dev.gemberkoekje.skyseed.compat;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.resources.ResourceLocation;
import org.jetbrains.annotations.Nullable;

/**
 * Version-volatile {@link ResourceLocation} construction, isolated behind stable signatures.
 *
 * <p>The {@code ResourceLocation} constructors moved in 1.21 ({@code new ResourceLocation(..)} →
 * {@code fromNamespaceAndPath} / {@code withDefaultNamespace}) and the API keeps shifting, so every id the mod
 * builds goes through here. When a future Minecraft/NeoForge version renames these, this is the only file that
 * changes (a Stonecutter directive lives here, never in the algorithm). See {@code REFACTORPLAN.md}.
 */
public final class Ids {

    private Ids() {
    }

    /** A {@link ResourceLocation} in an explicit namespace. */
    public static ResourceLocation of(String namespace, String path) {
        return ResourceLocation.fromNamespaceAndPath(namespace, path);
    }

    /** A {@link ResourceLocation} in the Skyseed namespace ({@code skyseed:<path>}). */
    public static ResourceLocation mod(String path) {
        return of(Skyseed.MODID, path);
    }

    /** A {@link ResourceLocation} in the vanilla namespace ({@code minecraft:<path>}). */
    public static ResourceLocation mc(String path) {
        return ResourceLocation.withDefaultNamespace(path);
    }

    /** Parse a namespaced id from a string; {@code null} if it is malformed. */
    @Nullable
    public static ResourceLocation parse(String s) {
        return ResourceLocation.tryParse(s);
    }
}
