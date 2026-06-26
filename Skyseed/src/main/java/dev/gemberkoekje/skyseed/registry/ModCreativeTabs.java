package dev.gemberkoekje.skyseed.registry;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.item.SkyseedGuide;
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
                        output.accept(SkyseedGuide.book());
                        ModItems.SEED_THEMES.forEach(theme -> output.accept(ModItems.SEEDS.get(theme).get()));
                        ModItems.PARTS.values().forEach(part -> output.accept(part.get()));
                    })
                    .build());

    /** A separate tab for the hidden debug seeds (one per chance-only rare structure) — creative testing only. */
    public static final DeferredHolder<CreativeModeTab, CreativeModeTab> SKYSEED_DEBUG_TAB =
            CREATIVE_MODE_TABS.register("skyseed_debug", () -> CreativeModeTab.builder()
                    .title(Component.translatable("itemGroup.skyseed.debug"))
                    .icon(() -> ModItems.DEBUG_SEEDS.get("debug_igloo").get().getDefaultInstance())
                    .withTabsBefore(SKYSEED_TAB.getId())
                    .displayItems((parameters, output) ->
                            ModItems.DEBUG_SEEDS.values().forEach(seed -> output.accept(seed.get().getDefaultInstance())))
                    .build());

    private ModCreativeTabs() {}

    public static void register(IEventBus modEventBus) {
        CREATIVE_MODE_TABS.register(modEventBus);
    }
}
