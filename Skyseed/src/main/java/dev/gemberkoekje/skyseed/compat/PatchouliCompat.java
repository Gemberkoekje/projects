package dev.gemberkoekje.skyseed.compat;

import net.minecraft.world.item.ItemStack;
//? if <26.1.2 {
import vazkii.patchouli.api.PatchouliAPI;
//?}

/**
 * Patchouli integration, isolated so the rest of the mod never touches Patchouli classes directly.
 * Only reference this class behind a {@code ModList.get().isLoaded("patchouli")} check — the JVM then
 * loads it (and the Patchouli API it imports) only when Patchouli is actually present, so the mod runs
 * fine without it. On versions where Patchouli isn't built yet (26.1.2) the whole API dependency is
 * compiled out by a {@code //?} directive and {@link #bookStack} returns an empty stack (the
 * {@code isLoaded} guard means it is never actually called there). See
 * {@link dev.gemberkoekje.skyseed.item.SkyseedGuide}.
 */
public final class PatchouliCompat {
    private PatchouliCompat() {}

    /** The Patchouli book item stack for the given book id (the rich, illustrated Almanac). */
    public static ItemStack bookStack(Id book) {
        //? if >=26.1.2 {
        /*return ItemStack.EMPTY;*/
        //?} else {
        return PatchouliAPI.get().getBookStack(Ids.parse(book.value()));
        //?}
    }
}
