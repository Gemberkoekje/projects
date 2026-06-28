package dev.gemberkoekje.skyseed.compat;

import com.klikli_dev.modonomicon.data.BookDataManager;
import com.klikli_dev.modonomicon.registry.DataComponentRegistry;
import com.klikli_dev.modonomicon.registry.ItemRegistry;
import net.minecraft.world.item.ItemStack;

/**
 * Modonomicon integration, isolated so the rest of the mod never touches Modonomicon classes directly. Only reference
 * this class behind a {@code ModList.get().isLoaded("modonomicon")} check — the JVM then loads it (and the Modonomicon
 * classes it imports) only when Modonomicon is actually present, so the mod runs fine without it.
 *
 * <p>Modonomicon publishes a build for every node we target (1.21.1 + 26.1.2), so — unlike {@link PatchouliCompat},
 * whose API is absent on 26.1.2 — no {@code //?} directive is needed here; the only version-volatile thing is the MC
 * id type, which {@link Ids#parse} returns per node (so we keep it behind {@code var} and never name it). Modonomicon
 * is the PRIMARY guide backend (preferred over Patchouli on every version) — see
 * {@link dev.gemberkoekje.skyseed.item.SkyseedGuide} and MODONOMICONPLAN.md.
 */
public final class ModonomiconCompat {
    private ModonomiconCompat() {}

    /**
     * The Modonomicon book item stack for {@code book}, or {@link ItemStack#EMPTY} if no such book is currently loaded
     * (e.g. the Skyseed Modonomicon content hasn't been authored yet) — which lets
     * {@link dev.gemberkoekje.skyseed.item.SkyseedGuide} fall through to the next backend. The stack is the generic
     * {@code modonomicon:book} item carrying the book id in its {@code BOOK_ID} data component.
     */
    public static ItemStack bookStack(Id book) {
        var id = Ids.parse(book.value()); // ResourceLocation (1.21.1) / Identifier (26.1.2)
        if (BookDataManager.get().getBook(id) == null) {
            return ItemStack.EMPTY; // no such book loaded -> caller tries the next backend
        }
        var stack = new ItemStack(ItemRegistry.MODONOMICON.get());
        stack.set(DataComponentRegistry.BOOK_ID.get(), id);
        return stack;
    }
}
