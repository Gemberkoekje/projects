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
            "huge_forest", "huge_aquatic", "huge_rocky", "huge_desert", "huge_mushroom", "huge_frozen",
            "huge_meadow", "huge_badlands", "huge_ancient", "huge_lush",
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
     * Hand-made debug <em>themes</em> for the one thing the auto scan ({@link ThemeScanner}) cannot derive — a
     * standalone island that is neither a per-theme biome override, a rare structure, nor a ladder-shaft waterfall:
     * the {@code debug_streets} jigsaw spike (SKYJIGSAWPLAN). (Everything chance-based — biome overrides, rare
     * structures, and the ladder waterfall — is auto-generated from the host theme now.) Same creative-only treatment
     * as the auto debug seeds: registered into {@link #DEBUG_SEEDS} and shown in the "Skyseed Debug" tab, no recipe/tag/guide.
     */
    public static final List<String> DEBUG_SEED_THEMES = List.of("debug_streets");

    /**
     * Auto-generated debug seeds, derived at construction by {@link ThemeScanner} from the shipped theme JSON: one per
     * theme {@code biome_overrides} entry (germinate that theme forced to a representative biome) and one per
     * {@code rare_structures} entry (germinate that theme forcing the otherwise chance-gated structure). Replaces the
     * old hand-maintained list — add an override or rare structure to any theme and its debug seed appears automatically,
     * with no model/lang/list edits. Same creative-only treatment as {@link #DEBUG_SEED_THEMES}: registered into
     * {@link #DEBUG_SEEDS}, shown in the debug tab, names composed at runtime, icons reused from the base theme
     * ({@link dev.gemberkoekje.skyseed.client.SkyseedClientEvents} model hook); no recipe/tag/guide.
     */
    public static final List<ThemeScanner.DebugSeedSpec> AUTO_DEBUG_SEEDS = ThemeScanner.scan();

    /** Auto debug seed's registered item name ({@code <id>_skyseed}) → its base theme, for the client model hook. */
    public static final Map<String, String> AUTO_DEBUG_BASE = new LinkedHashMap<>();

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
        for (ThemeScanner.DebugSeedSpec s : AUTO_DEBUG_SEEDS) {
            final ResourceLocation themeId = Ids.mod(s.baseTheme());
            DEBUG_SEEDS.put(s.id(), ITEMS.registerItem(s.id() + "_skyseed",
                    props -> new IslandSeedItem(props, themeId, s.forcedBiome(), s.forcedRare(), s.forcedWaterfall(), s.label()),
                    new Item.Properties().stacksTo(16)));
            AUTO_DEBUG_BASE.put(s.id() + "_skyseed", s.baseTheme());
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
