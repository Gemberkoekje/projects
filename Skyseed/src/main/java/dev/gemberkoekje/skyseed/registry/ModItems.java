package dev.gemberkoekje.skyseed.registry;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.item.GuideItem;
import dev.gemberkoekje.skyseed.item.IslandSeedItem;
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

    public static final DeferredItem<IslandSeedItem> ISLAND_SEED =
            ITEMS.registerItem("island_seed", IslandSeedItem::new, new Item.Properties().stacksTo(16));

    /** The Skyfarer's Almanac guide book. */
    public static final DeferredItem<GuideItem> GUIDE =
            ITEMS.registerItem("guide", GuideItem::new, new Item.Properties().stacksTo(1));

    private ModItems() {}

    public static void register(IEventBus modEventBus) {
        ITEMS.register(modEventBus);
    }
}
