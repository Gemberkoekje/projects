package dev.gemberkoekje.skyseed.compat;

import net.minecraft.resources.ResourceLocation;
import net.minecraft.world.item.ItemStack;
import vazkii.patchouli.api.PatchouliAPI;

/**
 * Patchouli integration, isolated so the rest of the mod never touches Patchouli classes directly.
 * Only reference this class behind a {@code ModList.get().isLoaded("patchouli")} check — the JVM then
 * loads it (and the Patchouli API it imports) only when Patchouli is actually present, so the mod runs
 * fine without it. See {@link dev.gemberkoekje.skyseed.item.SkyseedGuide}.
 */
public final class PatchouliCompat {
    private PatchouliCompat() {}

    /** The Patchouli book item stack for the given book id (the rich, illustrated Almanac). */
    public static ItemStack bookStack(ResourceLocation book) {
        return PatchouliAPI.get().getBookStack(book);
    }
}
