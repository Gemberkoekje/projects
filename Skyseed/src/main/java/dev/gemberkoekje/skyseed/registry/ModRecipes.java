package dev.gemberkoekje.skyseed.registry;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.recipe.GuideRecipe;
import net.minecraft.core.registries.Registries;
import net.minecraft.world.item.crafting.RecipeSerializer;
import net.minecraft.world.item.crafting.SimpleCraftingRecipeSerializer;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.neoforge.registries.DeferredHolder;
import net.neoforged.neoforge.registries.DeferredRegister;

/** Custom recipe serializers — just the dynamic "any Skyseed → Almanac" guide recipe. */
public final class ModRecipes {
    public static final DeferredRegister<RecipeSerializer<?>> RECIPE_SERIALIZERS =
            DeferredRegister.create(Registries.RECIPE_SERIALIZER, Skyseed.MODID);

    public static final DeferredHolder<RecipeSerializer<?>, SimpleCraftingRecipeSerializer<GuideRecipe>> GUIDE =
            RECIPE_SERIALIZERS.register("guide", () -> new SimpleCraftingRecipeSerializer<>(GuideRecipe::new));

    private ModRecipes() {}

    public static void register(IEventBus modEventBus) {
        RECIPE_SERIALIZERS.register(modEventBus);
    }
}
