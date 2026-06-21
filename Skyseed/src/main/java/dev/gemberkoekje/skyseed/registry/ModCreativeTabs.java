package dev.gemberkoekje.skyseed.registry;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.registries.Registries;
import net.minecraft.network.chat.Component;
import net.minecraft.world.item.CreativeModeTab;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.neoforge.registries.DeferredHolder;
import net.neoforged.neoforge.registries.DeferredRegister;

/** Creative tab: the guide book plus every Skyseed item, in catalogue order. */
public final class ModCreativeTabs {
    public static final DeferredRegister<CreativeModeTab> CREATIVE_MODE_TABS =
            DeferredRegister.create(Registries.CREATIVE_MODE_TAB, Skyseed.MODID);

    public static final DeferredHolder<CreativeModeTab, CreativeModeTab> SKYSEED_TAB =
            CREATIVE_MODE_TABS.register("skyseed", () -> CreativeModeTab.builder()
                    .title(Component.translatable("itemGroup.skyseed"))
                    .icon(() -> ModItems.DEFAULT_SEED.get().getDefaultInstance())
                    .displayItems((parameters, output) -> {
                        output.accept(ModItems.GUIDE.get());
                        ModItems.SEED_THEMES.forEach(theme -> output.accept(ModItems.SEEDS.get(theme).get()));
                    })
                    .build());

    private ModCreativeTabs() {}

    public static void register(IEventBus modEventBus) {
        CREATIVE_MODE_TABS.register(modEventBus);
    }
}
