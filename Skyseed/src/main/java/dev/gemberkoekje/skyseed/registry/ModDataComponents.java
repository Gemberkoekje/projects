package dev.gemberkoekje.skyseed.registry;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.component.DataComponentType;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.ResourceLocation;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.neoforge.registries.DeferredHolder;
import net.neoforged.neoforge.registries.DeferredRegister;

/**
 * Custom data components. The keystone is {@link #THEME}: a single id carried on every Skyseed item,
 * set by whichever recipe crafted it, and read later when the seed germinates. It keys off the same
 * theme-id namespace as recipes and the theme datapack registry — see README → The Skyseed item / Configuration.
 */
public final class ModDataComponents {
    public static final DeferredRegister.DataComponents DATA_COMPONENTS =
            DeferredRegister.createDataComponents(Registries.DATA_COMPONENT_TYPE, Skyseed.MODID);

    /** The theme id of a Skyseed, e.g. {@code skyseed:forest}. */
    public static final DeferredHolder<DataComponentType<?>, DataComponentType<ResourceLocation>> THEME =
            DATA_COMPONENTS.registerComponentType("theme", builder -> builder
                    .persistent(ResourceLocation.CODEC)
                    .networkSynchronized(ResourceLocation.STREAM_CODEC));

    private ModDataComponents() {}

    public static void register(IEventBus modEventBus) {
        DATA_COMPONENTS.register(modEventBus);
    }
}
