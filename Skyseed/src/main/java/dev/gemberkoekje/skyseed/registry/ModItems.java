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
            "aquatic", "aquatic_large",
            "nether_rocky", "nether_rocky_large", "nether_lava", "nether_lava_large", "nether_forest", "nether_forest_large",
            "nether_soul", "nether_soul_large", "nether_basalt", "nether_basalt_large", "nether_fortress",
            "hamlet", "trade_post", "village_center",
            "pasture", "poultry", "wool_farm", "stable", "aquarium",
            "dungeon", "ruined_portal", "desert_temple", "jungle_temple", "witch_hut", "outpost", "trial_chamber",
            "woodland_mansion", "ocean_monument");

    /**
     * Hidden debug seeds — one per rare structure that otherwise <em>only</em> appears by chance (igloo,
     * abandoned cottage, ocean ruin, evoker cell, vault cell, trail ruins). Each germinates that structure as a
     * dedicated island, so it can be spawned on demand instead of throwing 30 seeds to roll it. These are
     * <b>creative-only</b>: registered (and shown in the separate "Skyseed Debug" tab) but deliberately given
     * <b>no recipe</b>, kept out of the {@code #skyseed:skyseeds} tag, and given no guide entry.
     */
    public static final List<String> DEBUG_SEED_THEMES = List.of(
            "debug_igloo", "debug_abandoned_cottage", "debug_ocean_ruin",
            "debug_evoker_cell", "debug_vault_cell", "debug_trail_ruins", "debug_blaze_spawner");

    /** theme id → its seed item, in {@link #SEED_THEMES} order. */
    public static final Map<String, DeferredItem<IslandSeedItem>> SEEDS = new LinkedHashMap<>();

    /** theme id → its debug seed item, in {@link #DEBUG_SEED_THEMES} order. */
    public static final Map<String, DeferredItem<IslandSeedItem>> DEBUG_SEEDS = new LinkedHashMap<>();

    static {
        registerSeeds(SEED_THEMES, SEEDS);
        registerSeeds(DEBUG_SEED_THEMES, DEBUG_SEEDS);
    }

    private static void registerSeeds(List<String> themes, Map<String, DeferredItem<IslandSeedItem>> into) {
        for (String theme : themes) {
            final ResourceLocation themeId = ResourceLocation.fromNamespaceAndPath(Skyseed.MODID, theme);
            into.put(theme, ITEMS.registerItem(theme + "_skyseed",
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
