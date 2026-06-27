package dev.gemberkoekje.skyseed.registry;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.compat.Ids;
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
            "huge_forest", "huge_aquatic",
            "ladder_small", "ladder_large",
            "nether_rocky", "nether_rocky_large", "nether_lava", "nether_lava_large", "nether_forest", "nether_forest_large",
            "nether_soul", "nether_soul_large", "nether_basalt", "nether_basalt_large", "nether_fortress", "bastion",
            "piglin_trading_post", "wither_arena",
            "hamlet", "trade_post", "village_center",
            "pasture", "poultry", "wool_farm", "stable", "aquarium",
            "dungeon", "ruined_portal", "desert_temple", "jungle_temple", "witch_hut", "outpost", "trial_chamber",
            "woodland_mansion", "ocean_monument", "end_portal", "return_portal", "chorus_forest", "end_city",
            "dragon_trophy");

    /**
     * Hidden debug seeds — one per rare structure that otherwise <em>only</em> appears by chance (igloo,
     * abandoned cottage, ocean ruin, evoker cell, vault cell, trail ruins). Each germinates that structure as a
     * dedicated island, so it can be spawned on demand instead of throwing 30 seeds to roll it. These are
     * <b>creative-only</b>: registered (and shown in the separate "Skyseed Debug" tab) but deliberately given
     * <b>no recipe</b>, kept out of the {@code #skyseed:skyseeds} tag, and given no guide entry.
     */
    public static final List<String> DEBUG_SEED_THEMES = List.of(
            "debug_igloo", "debug_abandoned_cottage", "debug_ocean_ruin",
            "debug_evoker_cell", "debug_vault_cell", "debug_trail_ruins", "debug_blaze_spawner",
            "debug_bastion_remnant", "debug_streets", "debug_small_waterfall", "debug_large_waterfall");

    /**
     * Hidden debug seeds that germinate an <em>existing</em> theme but force the island to read as a specific biome,
     * regardless of where they're thrown — so a biome-adaptive island (the trade post's desert style, and any theme
     * with biome overrides) can be inspected on demand without flying to that biome. Same creative-only treatment as
     * {@link #DEBUG_SEED_THEMES}: registered into {@link #DEBUG_SEEDS}, shown in the debug tab, but no recipe/tag/guide.
     */
    public record BiomeDebugSeed(String name, String theme, String biome) {}

    public static final List<BiomeDebugSeed> BIOME_DEBUG_SEEDS = List.of(
            new BiomeDebugSeed("debug_trade_post_plains", "trade_post", "minecraft:plains"),
            new BiomeDebugSeed("debug_trade_post_desert", "trade_post", "minecraft:desert"),
            new BiomeDebugSeed("debug_trade_post_savanna", "trade_post", "minecraft:savanna"),
            new BiomeDebugSeed("debug_trade_post_taiga", "trade_post", "minecraft:taiga"),
            new BiomeDebugSeed("debug_trade_post_snowy", "trade_post", "minecraft:snowy_plains"),
            new BiomeDebugSeed("debug_village_center", "village_center", "minecraft:plains"),
            new BiomeDebugSeed("debug_village_center_desert", "village_center", "minecraft:desert"),
            new BiomeDebugSeed("debug_village_center_savanna", "village_center", "minecraft:savanna"),
            new BiomeDebugSeed("debug_village_center_snowy", "village_center", "minecraft:snowy_plains"),
            new BiomeDebugSeed("debug_village_center_taiga", "village_center", "minecraft:taiga"),
            new BiomeDebugSeed("debug_forest_taiga", "forest", "minecraft:taiga"),
            new BiomeDebugSeed("debug_forest_dark", "forest", "minecraft:dark_forest"),
            new BiomeDebugSeed("debug_desert_badlands", "desert", "minecraft:badlands"),
            new BiomeDebugSeed("debug_rocky_snowy", "rocky", "minecraft:snowy_plains"),
            new BiomeDebugSeed("debug_aquatic_ocean", "aquatic", "minecraft:ocean"),
            new BiomeDebugSeed("debug_aquatic_swamp", "aquatic", "minecraft:mangrove_swamp"),
            new BiomeDebugSeed("debug_frozen_ice_spikes", "frozen_large", "minecraft:ice_spikes"),
            new BiomeDebugSeed("debug_ladder_desert", "ladder_small", "minecraft:desert"),
            // Huge tier (SKYHUGEPLAN) — spawn the huge islands free for testing, in the biomes they adapt to (huge
            // Forest/Aquatic carry the regular seeds' biome forms). huge_rocky/desert/ancient are debug-only draft
            // themes (no real seed yet; the Phase-4 rollout promotes them).
            new BiomeDebugSeed("debug_huge_forest", "huge_forest", "minecraft:plains"),
            new BiomeDebugSeed("debug_huge_forest_taiga", "huge_forest", "minecraft:taiga"),
            new BiomeDebugSeed("debug_huge_forest_dark", "huge_forest", "minecraft:dark_forest"),
            new BiomeDebugSeed("debug_huge_forest_cherry", "huge_forest", "minecraft:cherry_grove"),
            new BiomeDebugSeed("debug_huge_forest_jungle", "huge_forest", "minecraft:jungle"),
            new BiomeDebugSeed("debug_huge_forest_swamp", "huge_forest", "minecraft:swamp"),
            new BiomeDebugSeed("debug_huge_forest_snowy", "huge_forest", "minecraft:snowy_plains"),
            new BiomeDebugSeed("debug_huge_aquatic", "huge_aquatic", "minecraft:plains"),
            new BiomeDebugSeed("debug_huge_aquatic_ocean", "huge_aquatic", "minecraft:ocean"),
            new BiomeDebugSeed("debug_huge_aquatic_swamp", "huge_aquatic", "minecraft:swamp"),
            new BiomeDebugSeed("debug_huge_rocky", "huge_rocky", "minecraft:windswept_hills"),
            new BiomeDebugSeed("debug_huge_desert", "huge_desert", "minecraft:desert"),
            new BiomeDebugSeed("debug_huge_ancient", "huge_ancient", "minecraft:plains"));

    /** theme id → its seed item, in {@link #SEED_THEMES} order. */
    public static final Map<String, DeferredItem<IslandSeedItem>> SEEDS = new LinkedHashMap<>();

    /** theme id → its debug seed item, in {@link #DEBUG_SEED_THEMES} order. */
    public static final Map<String, DeferredItem<IslandSeedItem>> DEBUG_SEEDS = new LinkedHashMap<>();

    /**
     * End-chapter crafting components (the Phase-1 collect-a-thon, see SKYENDPLAN.md): the Portal Frame Shard, the
     * eight structure relics, and the four portal edges. Plain items (not seeds) that combine — shard + 2 relics → an
     * edge, four edges → the End Portal Seed. No {@code skyseeds} tag (they aren't seeds).
     */
    public static final List<String> END_PORTAL_PARTS = List.of(
            "portal_frame_shard",
            "mansion_relic", "monument_relic", "desert_relic", "jungle_relic",
            "trial_relic", "outpost_relic", "fortress_relic", "bastion_relic",
            "grand_edge", "temple_edge", "camp_edge", "nether_edge");

    /** part id → its item, in {@link #END_PORTAL_PARTS} order. */
    public static final Map<String, DeferredItem<Item>> PARTS = new LinkedHashMap<>();

    static {
        registerSeeds(SEED_THEMES, SEEDS);
        registerSeeds(DEBUG_SEED_THEMES, DEBUG_SEEDS);
        for (final String part : END_PORTAL_PARTS) {
            PARTS.put(part, ITEMS.registerItem(part, Item::new, new Item.Properties()));
        }
        for (BiomeDebugSeed s : BIOME_DEBUG_SEEDS) {
            final ResourceLocation themeId = Ids.mod(s.theme());
            final ResourceLocation biomeId = Ids.parse(s.biome());
            DEBUG_SEEDS.put(s.name(), ITEMS.registerItem(s.name() + "_skyseed",
                    props -> new IslandSeedItem(props, themeId, biomeId), new Item.Properties().stacksTo(16)));
        }
    }

    private static void registerSeeds(List<String> themes, Map<String, DeferredItem<IslandSeedItem>> into) {
        for (String theme : themes) {
            final ResourceLocation themeId = Ids.mod(theme);
            into.put(theme, ITEMS.registerItem(theme + "_skyseed",
                    props -> new IslandSeedItem(props, themeId), new Item.Properties().stacksTo(16)));
        }
    }

    /** The canonical seed, used as the fallback display item for the thrown entity. */
    public static final DeferredItem<IslandSeedItem> DEFAULT_SEED = SEEDS.get("forest");

    /** Every Skyseed item — the guide recipe accepts any one of these; add-on seeds should join this tag. */
    public static final TagKey<Item> SKYSEEDS =
            TagKey.create(Registries.ITEM, Ids.mod("skyseeds"));

    private ModItems() {}

    public static void register(IEventBus modEventBus) {
        ITEMS.register(modEventBus);
    }
}
