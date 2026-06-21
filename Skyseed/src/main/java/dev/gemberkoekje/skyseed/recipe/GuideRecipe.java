package dev.gemberkoekje.skyseed.recipe;

import dev.gemberkoekje.skyseed.item.SkyseedGuide;
import dev.gemberkoekje.skyseed.registry.ModItems;
import dev.gemberkoekje.skyseed.registry.ModRecipes;
import net.minecraft.core.HolderLookup;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.crafting.CraftingBookCategory;
import net.minecraft.world.item.crafting.CraftingInput;
import net.minecraft.world.item.crafting.CustomRecipe;
import net.minecraft.world.item.crafting.RecipeSerializer;
import net.minecraft.world.level.Level;

/**
 * Crafts any single Skyseed (the {@code #skyseed:skyseeds} tag) into the Almanac. A code recipe rather than
 * a JSON one so the output is resolved at craft time by {@link SkyseedGuide#book()} — the Patchouli book if
 * that mod is installed, otherwise the plain written book — exactly matching the first-join grant.
 */
public class GuideRecipe extends CustomRecipe {
    public GuideRecipe(CraftingBookCategory category) {
        super(category);
    }

    @Override
    public boolean matches(CraftingInput input, Level level) {
        ItemStack found = ItemStack.EMPTY;
        for (int i = 0; i < input.size(); i++) {
            final ItemStack stack = input.getItem(i);
            if (!stack.isEmpty()) {
                if (!found.isEmpty()) {
                    return false; // more than one item — not a guide craft
                }
                found = stack;
            }
        }
        return !found.isEmpty() && found.is(ModItems.SKYSEEDS);
    }

    @Override
    public ItemStack assemble(CraftingInput input, HolderLookup.Provider registries) {
        return SkyseedGuide.book();
    }

    @Override
    public ItemStack getResultItem(HolderLookup.Provider registries) {
        return SkyseedGuide.book();
    }

    @Override
    public boolean canCraftInDimensions(int width, int height) {
        return width * height >= 1;
    }

    @Override
    public RecipeSerializer<?> getSerializer() {
        return ModRecipes.GUIDE.get();
    }
}
