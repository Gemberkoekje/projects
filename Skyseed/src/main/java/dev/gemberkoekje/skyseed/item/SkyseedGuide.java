package dev.gemberkoekje.skyseed.item;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.compat.PatchouliCompat;
import net.minecraft.core.component.DataComponents;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.network.Filterable;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.component.WrittenBookContent;
import net.neoforged.fml.ModList;

import java.util.List;

/**
 * The Skyfarer's Almanac, made Patchouli-optional. With Patchouli installed it's the rich illustrated book
 * ({@code patchouli:guide_book}); without it, a plain vanilla written book carrying the text below (kept
 * deliberately short — the Patchouli edition is the full guide). Both the first-join grant and the craft
 * recipe ({@link dev.gemberkoekje.skyseed.recipe.GuideRecipe}) call {@link #book()}, so they always agree.
 */
public final class SkyseedGuide {
    private SkyseedGuide() {}

    /** The Patchouli book id (also the datapack book folder under {@code patchouli_books/guide}). */
    public static final ResourceLocation BOOK_ID = ResourceLocation.fromNamespaceAndPath(Skyseed.MODID, "guide");

    /** The Patchouli book if that mod is present, otherwise the plain written-book fallback. */
    public static ItemStack book() {
        if (ModList.get().isLoaded("patchouli")) {
            ItemStack patchouli = PatchouliCompat.bookStack(BOOK_ID);
            if (!patchouli.isEmpty()) {
                return patchouli;
            }
        }
        return writtenBook();
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
                        + "full illustrated guide — a page for every island — install the Patchouli mod.§r"));

        final ItemStack stack = new ItemStack(Items.WRITTEN_BOOK);
        stack.set(DataComponents.WRITTEN_BOOK_CONTENT, new WrittenBookContent(
                Filterable.passThrough("Skyfarer's Almanac"), "Skyseed", 0,
                pages.stream().map(Filterable::passThrough).toList(), true));
        return stack;
    }
}
