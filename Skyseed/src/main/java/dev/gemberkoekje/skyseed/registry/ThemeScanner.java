package dev.gemberkoekje.skyseed.registry;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.resources.ResourceLocation;
import net.neoforged.fml.ModList;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.stream.Stream;

/**
 * Scans the shipped {@code skyseed:theme} JSON at mod-construction time and derives the full set of debug seeds so we
 * never hand-maintain them: one per theme {@code biome_overrides} entry (germinate that theme forced to a representative
 * biome) and one per {@code rare_structures} entry (germinate that theme forcing the otherwise chance-gated structure).
 * Add an override or a rare structure to any theme and its debug seed appears next launch — no list, model, or lang
 * edits. {@link ModItems} registers a {@link dev.gemberkoekje.skyseed.item.IslandSeedItem} per spec; the names are
 * composed at runtime and the icons reuse the base theme's seed texture (a client model hook), so no per-seed files.
 */
public final class ThemeScanner {
    private ThemeScanner() {}

    /**
     * One auto debug seed. {@code id} is the registered name (no {@code _skyseed} suffix); {@code baseTheme} is the
     * theme it germinates; exactly one of {@code forcedBiome} (force this biome) or {@code forcedRare} &ge; 0 (force the
     * rare structure at that index into the theme's {@code rare_structures}) drives what it shows.
     */
    public record DebugSeedSpec(String id, String baseTheme, String label, ResourceLocation forcedBiome, int forcedRare) {}

    public static List<DebugSeedSpec> scan() {
        final List<DebugSeedSpec> out = new ArrayList<>();
        final Set<String> ids = new HashSet<>();
        try {
            final Path dir = ModList.get().getModFileById(Skyseed.MODID).getFile()
                    .findResource("data", "skyseed", "skyseed", "theme");
            if (dir == null || !Files.isDirectory(dir)) {
                return out;
            }
            try (Stream<Path> files = Files.walk(dir)) {
                files.filter(p -> p.getFileName().toString().endsWith(".json")).sorted()
                        .forEach(p -> scanTheme(dir, p, out, ids));
            }
        } catch (Exception e) {
            Skyseed.LOGGER.warn("[skyseed] debug-seed theme scan skipped: {}", e.toString());
        }
        Skyseed.LOGGER.info("[skyseed] auto debug seeds: {}", out.size());
        return out;
    }

    private static void scanTheme(Path dir, Path file, List<DebugSeedSpec> out, Set<String> ids) {
        final String theme = relName(dir, file);
        try {
            final JsonObject root = JsonParser.parseString(Files.readString(file)).getAsJsonObject();
            if (root.has("biome_overrides")) {
                for (JsonElement el : root.getAsJsonArray("biome_overrides")) {
                    final JsonObject ov = el.getAsJsonObject();
                    if (!ov.has("biomes")) {
                        continue;
                    }
                    final ResourceLocation biome = firstConcreteBiome(ov.getAsJsonArray("biomes"));
                    if (biome == null) {
                        continue;
                    }
                    final String id = unique("debug_" + theme + "_" + biome.getPath(), ids);
                    out.add(new DebugSeedSpec(id, theme, theme + " [" + biome.getPath() + "]", biome, -1));
                }
            }
            if (root.has("rare_structures")) {
                final JsonArray arr = root.getAsJsonArray("rare_structures");
                for (int i = 0; i < arr.size(); i++) {
                    final String tag = rareTag(arr.get(i).getAsJsonObject(), i);
                    final String id = unique("debug_" + theme + "_" + tag, ids);
                    out.add(new DebugSeedSpec(id, theme, theme + " (" + tag + ")", null, i));
                }
            }
        } catch (Exception e) {
            Skyseed.LOGGER.warn("[skyseed] debug-seed scan failed for theme {}: {}", theme, e.toString());
        }
    }

    /** Theme id from the file path: the path under {@code dir} with the {@code .json} suffix dropped. */
    private static String relName(Path dir, Path file) {
        final String rel = dir.relativize(file).toString().replace('\\', '/');
        return rel.substring(0, rel.length() - ".json".length());
    }

    /**
     * Resolve the first usable biome in an override's {@code biomes} list to a concrete biome id: a plain id verbatim,
     * or a {@code #ns:is_X} tag mapped to {@code ns:X} (every override tag follows that shape, and {@code X} is a real
     * biome). An odd tag we can't map to a single biome is skipped.
     */
    private static ResourceLocation firstConcreteBiome(JsonArray biomes) {
        for (JsonElement b : biomes) {
            String s = b.getAsString();
            if (s.startsWith("#")) {
                s = s.substring(1);
                final int colon = s.indexOf(':');
                final String ns = colon < 0 ? "minecraft" : s.substring(0, colon);
                final String path = colon < 0 ? s : s.substring(colon + 1);
                if (!path.startsWith("is_")) {
                    continue;
                }
                s = ns + ":" + path.substring("is_".length());
            }
            final ResourceLocation id = ResourceLocation.tryParse(s);
            if (id != null) {
                return id;
            }
        }
        return null;
    }

    /** A short tag for a rare structure: its jigsaw pool's first path segment ({@code ancient_city/plaza} →
     *  {@code ancient_city}), else {@code rare<index>}. */
    private static String rareTag(JsonObject rare, int index) {
        if (rare.has("jigsaw")) {
            final JsonObject jig = rare.getAsJsonObject("jigsaw");
            if (jig.has("pool")) {
                final ResourceLocation pool = ResourceLocation.tryParse(jig.get("pool").getAsString());
                if (pool != null) {
                    final String path = pool.getPath();
                    final int slash = path.indexOf('/');
                    return slash < 0 ? path : path.substring(0, slash);
                }
            }
        }
        return "rare" + index;
    }

    /** A registry-safe id, made unique against {@code ids} by suffixing a counter on collision. */
    private static String unique(String base, Set<String> ids) {
        final String safe = base.replaceAll("[^a-z0-9_]", "_");
        String id = safe;
        int n = 2;
        while (!ids.add(id)) {
            id = safe + "_" + (n++);
        }
        return id;
    }
}
