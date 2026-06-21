package dev.gemberkoekje.skyseed.registry;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.Registries;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.world.item.CreativeModeTab;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.component.CustomModelData;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.neoforge.registries.DeferredHolder;
import net.neoforged.neoforge.registries.DeferredRegister;

/** Creative tab holding the Skyseed item, plus a pre-themed example so themes are testable without crafting. */
public final class ModCreativeTabs {
    public static final DeferredRegister<CreativeModeTab> CREATIVE_MODE_TABS =
            DeferredRegister.create(Registries.CREATIVE_MODE_TAB, Skyseed.MODID);

    public static final DeferredHolder<CreativeModeTab, CreativeModeTab> SKYSEED_TAB =
            CREATIVE_MODE_TABS.register("skyseed", () -> CreativeModeTab.builder()
                    .title(Component.translatable("itemGroup.skyseed"))
                    .icon(() -> ModItems.ISLAND_SEED.get().getDefaultInstance())
                    .displayItems((parameters, output) -> {
                        // The guide book.
                        output.accept(ModItems.GUIDE.get());
                        // The generic, theme-less seed.
                        output.accept(ModItems.ISLAND_SEED.get());
                        // Pre-themed examples (handy for testing germination without crafting).
                        output.accept(themedSeed("forest", "Forest Skyseed", 0));
                        output.accept(themedSeed("forest_large", "Large Forest Skyseed", 2));
                        output.accept(themedSeed("rocky", "Rocky Skyseed", 1));
                        output.accept(themedSeed("rocky_large", "Large Rocky Skyseed", 12));
                        output.accept(themedSeed("desert", "Desert Skyseed", 3));
                        output.accept(themedSeed("desert_large", "Large Desert Skyseed", 13));
                        output.accept(themedSeed("mushroom", "Mushroom Skyseed", 4));
                        output.accept(themedSeed("mushroom_large", "Large Mushroom Skyseed", 14));
                        output.accept(themedSeed("frozen", "Frozen Skyseed", 5));
                        output.accept(themedSeed("frozen_large", "Large Frozen Skyseed", 15));
                        output.accept(themedSeed("meadow", "Meadow Skyseed", 6));
                        output.accept(themedSeed("meadow_large", "Large Meadow Skyseed", 16));
                        output.accept(themedSeed("badlands", "Badlands Skyseed", 7));
                        output.accept(themedSeed("badlands_large", "Large Badlands Skyseed", 17));
                        output.accept(themedSeed("ancient", "Ancient Skyseed", 8));
                        output.accept(themedSeed("ancient_large", "Large Ancient Skyseed", 18));
                        output.accept(themedSeed("lush", "Lush Skyseed", 9));
                        output.accept(themedSeed("lush_large", "Large Lush Skyseed", 11));
                        output.accept(themedSeed("aquatic", "Aquatic Skyseed", 10));
                        output.accept(themedSeed("aquatic_large", "Large Aquatic Skyseed", 19));
                        output.accept(themedSeed("hamlet", "Hamlet Skyseed", 20));
                    })
                    .build());

    private static ItemStack themedSeed(String themePath, String displayName, int customModelData) {
        ItemStack stack = new ItemStack(ModItems.ISLAND_SEED.get());
        stack.set(ModDataComponents.THEME.get(), ResourceLocation.fromNamespaceAndPath(Skyseed.MODID, themePath));
        stack.set(DataComponents.ITEM_NAME, Component.literal(displayName));
        if (customModelData > 0) {
            stack.set(DataComponents.CUSTOM_MODEL_DATA, new CustomModelData(customModelData));
        }
        return stack;
    }

    private ModCreativeTabs() {}

    public static void register(IEventBus modEventBus) {
        CREATIVE_MODE_TABS.register(modEventBus);
    }
}
