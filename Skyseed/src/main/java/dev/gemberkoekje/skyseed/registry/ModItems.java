package dev.gemberkoekje.skyseed.registry;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.world.item.Item;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.neoforge.registries.DeferredItem;
import net.neoforged.neoforge.registries.DeferredRegister;

/**
 * Mod items. There is a single Skyseed item ({@link #ISLAND_SEED}); themes are differentiated by the
 * {@link ModDataComponents#THEME} component, not by separate items — see plan §3.
 */
public final class ModItems {
    public static final DeferredRegister.Items ITEMS = DeferredRegister.createItems(Skyseed.MODID);

    public static final DeferredItem<Item> ISLAND_SEED = ITEMS.registerSimpleItem("island_seed");

    private ModItems() {}

    public static void register(IEventBus modEventBus) {
        ITEMS.register(modEventBus);
    }
}
