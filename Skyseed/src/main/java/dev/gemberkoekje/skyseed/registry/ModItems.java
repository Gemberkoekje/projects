package dev.gemberkoekje.skyseed.registry;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.item.IslandSeedItem;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.tags.TagKey;
import net.minecraft.world.item.Item;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.neoforge.registries.DeferredItem;
import net.neoforged.neoforge.registries.DeferredRegister;

import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Mod items. Each island theme is its own Skyseed item ({@code <theme>_skyseed}), registered from
 * {@link #SEED_THEMES} — distinct items so they show up individually in JEI/REI and so add-on mods can
 * register their own. The thrown seed's island is fixed by {@link IslandSeedItem#theme()}. All seeds
 * share the {@code skyseed:skyseeds} item tag (used by the guide recipe). See README → The Skyseed item.
 */
public final class ModItems {
    public static final DeferredRegister.Items ITEMS = DeferredRegister.createItems(Skyseed.MODID);

    /** Theme ids (in the catalogue/creative-tab order), each registered as a distinct {@code <id>_skyseed} item. */
    public static final List<String> SEED_THEMES = List.of(
            "forest", "forest_large", "rocky", "rocky_large", "desert", "desert_large",
            "mushroom", "mushroom_large", "frozen", "frozen_large", "meadow", "meadow_large",
            "badlands", "badlands_large", "ancient", "ancient_large", "lush", "lush_large",
            "aquatic", "aquatic_large", "hamlet", "trade_post", "village_center",
            "pasture", "poultry", "wool_farm", "stable", "aquarium",
            "dungeon", "ruined_portal", "desert_temple", "jungle_temple", "witch_hut");

    /** theme id → its seed item, in {@link #SEED_THEMES} order. */
    public static final Map<String, DeferredItem<IslandSeedItem>> SEEDS = new LinkedHashMap<>();

    static {
        for (String theme : SEED_THEMES) {
            final ResourceLocation themeId = ResourceLocation.fromNamespaceAndPath(Skyseed.MODID, theme);
            SEEDS.put(theme, ITEMS.registerItem(theme + "_skyseed",
                    props -> new IslandSeedItem(props, themeId), new Item.Properties().stacksTo(16)));
        }
    }

    /** The canonical seed, used as the fallback display item for the thrown entity. */
    public static final DeferredItem<IslandSeedItem> DEFAULT_SEED = SEEDS.get("forest");

    /** Every Skyseed item — the guide recipe accepts any one of these; add-on seeds should join this tag. */
    public static final TagKey<Item> SKYSEEDS =
            TagKey.create(Registries.ITEM, ResourceLocation.fromNamespaceAndPath(Skyseed.MODID, "skyseeds"));

    private ModItems() {}

    public static void register(IEventBus modEventBus) {
        ITEMS.register(modEventBus);
    }
}
