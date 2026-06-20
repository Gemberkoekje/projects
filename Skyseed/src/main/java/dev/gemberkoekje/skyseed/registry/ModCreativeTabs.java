package dev.gemberkoekje.skyseed.registry;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.Registries;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.world.item.CreativeModeTab;
import net.minecraft.world.item.ItemStack;
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
                        // The generic, theme-less seed.
                        output.accept(ModItems.ISLAND_SEED.get());
                        // The forest variant the basic recipe produces (handy for testing future germination).
                        output.accept(themedSeed("forest", "Forest Skyseed"));
                    })
                    .build());

    private static ItemStack themedSeed(String themePath, String displayName) {
        ItemStack stack = new ItemStack(ModItems.ISLAND_SEED.get());
        stack.set(ModDataComponents.THEME.get(), ResourceLocation.fromNamespaceAndPath(Skyseed.MODID, themePath));
        stack.set(DataComponents.ITEM_NAME, Component.literal(displayName));
        return stack;
    }

    private ModCreativeTabs() {}

    public static void register(IEventBus modEventBus) {
        CREATIVE_MODE_TABS.register(modEventBus);
    }
}
