package dev.gemberkoekje.skyseed.item;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.compat.Id;
import dev.gemberkoekje.skyseed.compat.Ids;
import dev.gemberkoekje.skyseed.compat.ModonomiconCompat;
import dev.gemberkoekje.skyseed.compat.PatchouliCompat;
import net.minecraft.core.component.DataComponents;
import net.minecraft.network.chat.Component;
import net.minecraft.server.network.Filterable;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.component.WrittenBookContent;
import net.neoforged.fml.ModList;

import java.util.List;

/**
 * The Skyfarer's Almanac, made optional across two guide-book mods. The rich illustrated edition comes from
 * <b>Modonomicon</b> (the preferred backend on every version) or, as a legacy fallback, <b>Patchouli</b>; with neither
 * installed it's a plain vanilla written book carrying the short text below (the rich editions are the full guide).
 * Both the first-join grant and the craft recipe ({@link dev.gemberkoekje.skyseed.recipe.GuideRecipe}) call
 * {@link #book()}, so they always agree on the one book to hand out. See MODONOMICONPLAN.md.
 */
public final class SkyseedGuide {
    private SkyseedGuide() {}

    /** The guide book id (also the datapack book folder: {@code patchouli_books/guide} / {@code modonomicon/books/guide}). */
    public static final Id BOOK_ID = Id.of(Ids.mod("guide").toString());

    private static boolean loggedBackends = false;

    /**
     * The one book to hand out: the first installed backend that yields a real (non-empty) book — Modonomicon
     * (preferred), then Patchouli (legacy fallback) — otherwise the vanilla written book. A backend returns empty when
     * it's loaded but has no Skyseed book yet, so we degrade gracefully (and so two installed book mods never both
     * hand out a book — exactly one wins, deterministically).
     */
    public static ItemStack book() {
        logBackendsOnce();
        if (ModList.get().isLoaded("modonomicon")) {
            ItemStack modonomicon = ModonomiconCompat.bookStack(BOOK_ID);
            if (!modonomicon.isEmpty()) {
                return modonomicon;
            }
        }
        if (ModList.get().isLoaded("patchouli")) {
            ItemStack patchouli = PatchouliCompat.bookStack(BOOK_ID);
            if (!patchouli.isEmpty()) {
                return patchouli;
            }
        }
        return writtenBook();
    }

    /** If more than one guide-book mod is installed, note once which one Skyseed prefers (Modonomicon). */
    private static void logBackendsOnce() {
        if (loggedBackends) {
            return;
        }
        loggedBackends = true;
        if (ModList.get().isLoaded("modonomicon") && ModList.get().isLoaded("patchouli")) {
            Skyseed.LOGGER.info("[skyseed] Both Modonomicon and Patchouli are installed; the Skyfarer's Almanac uses "
                    + "the Modonomicon edition (preferred). Patchouli's copy still appears in its own book list.");
        }
    }

    /** The vanilla written-book edition of the Almanac (no Patchouli required). Update the pages here by hand. */
    public static ItemStack writtenBook() {
        final List<Component> pages = List.of(
                Component.literal("The Skyfarer's Almanac\n\nCraft a Skyseed, hold right-click to wind up, "
                        + "and release to throw it. Where it lands, an island grows from nothing."),
                Component.literal("Throwing (toggle with V):\n\n§lPrecise§r (default) — places the island "
                        + "along your line of sight.\n\n§lClassic§r — a charged arc; the island grows where "
                        + "the seed comes to rest."),
                Component.literal("Every Skyseed grows its own kind of island — forests, deserts, mushroom isles, "
                        + "frozen peaks, villages, animal pens and more.\n\nHow high you throw sets the island's "
                        + "altitude, and so its ores."),
                Component.literal("Craft any Skyseed back into this Almanac at a crafting table.\n\n§oFor the "
                        + "full illustrated guide — a page for every island — install the Modonomicon (or Patchouli) mod.§r"));

        final ItemStack stack = new ItemStack(Items.WRITTEN_BOOK);
        stack.set(DataComponents.WRITTEN_BOOK_CONTENT, new WrittenBookContent(
                Filterable.passThrough("Skyfarer's Almanac"), "Skyseed", 0,
                pages.stream().map(Filterable::passThrough).toList(), true));
        return stack;
    }
}
