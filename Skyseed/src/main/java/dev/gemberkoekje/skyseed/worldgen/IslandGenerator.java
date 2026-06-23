package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan.BlockPlacement;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan.TreeSite;
import dev.gemberkoekje.skyseed.worldgen.theme.AnimalPack;
import dev.gemberkoekje.skyseed.worldgen.theme.BiomeOverride;
import dev.gemberkoekje.skyseed.worldgen.theme.GroundEntry;
import dev.gemberkoekje.skyseed.worldgen.theme.IntRange;
import dev.gemberkoekje.skyseed.worldgen.theme.IslandTheme;
import dev.gemberkoekje.skyseed.worldgen.theme.Lava;
import dev.gemberkoekje.skyseed.worldgen.theme.MobEntry;
import dev.gemberkoekje.skyseed.worldgen.theme.OreDepth;
import dev.gemberkoekje.skyseed.worldgen.theme.OreEntry;
import dev.gemberkoekje.skyseed.worldgen.theme.Palette;
import dev.gemberkoekje.skyseed.worldgen.theme.Pond;
import dev.gemberkoekje.skyseed.worldgen.theme.JigsawConfig;
import dev.gemberkoekje.skyseed.worldgen.theme.RareStructure;
import dev.gemberkoekje.skyseed.worldgen.theme.Shape;
import dev.gemberkoekje.skyseed.worldgen.theme.Variant;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.biome.Biome;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;

/**
 * Computes an island (README → Generation algorithm) from an {@link IslandTheme}, with the theme's base config overlaid
 * by the first matching {@link BiomeOverride} for the biome the seed landed in. Near-pure: returns an
 * {@link IslandPlan} without writing the world (a {@link GenerationJob} drains it over ticks).
 *
 * <p>This class is the orchestrator: it resolves the effective config, then drives the generation passes — terrain
 * ({@link ShapeBuilder}), ores ({@link OrePlanner}), ponds/rivers ({@link PondCarver}), curated structures + animal
 * packs, decoration ({@link DecorationPlanner}, with {@link CustomTrees}), and mobs ({@link MobPlanner}) — threading a
 * single {@link RandomSource} through them in a fixed order so a given seed always yields the same island.
 */
public final class IslandGenerator {
    private IslandGenerator() {}

    /** Blocks of headroom cleared above a structure pad, so an assembled building isn't clipped by island terrain. */
    private static final int PAD_CLEAR_HEIGHT = 10;

    public static IslandPlan planIsland(ServerLevel level, BlockPos center, IslandTheme theme,
                                        Holder<Biome> biome, RandomSource random) {
        final BiomeOverride ov = matchOverride(theme.biomeOverrides(), biome, center.getY(), level.dimension().location());

        // --- effective config: base theme, overlaid by the matching biome override ---
        final Shape shape = (ov != null && ov.shape().isPresent()) ? ov.shape().get() : theme.shape();
        final List<OreEntry> ores = (ov != null && ov.ores().isPresent()) ? ov.ores().get() : theme.ores();
        final List<Variant> variants = (ov != null && ov.variants().isPresent()) ? ov.variants().get() : theme.variants();
        final Palette pal = theme.palette();
        final ResourceLocation fillId = (ov != null && ov.fill().isPresent()) ? ov.fill().get() : pal.fill();
        final ResourceLocation coreId = (ov != null && ov.core().isPresent()) ? ov.core().get() : pal.core();
        final int baseFill = (ov != null && ov.fillDepth().isPresent()) ? ov.fillDepth().get() : pal.fillDepth();
        final List<GroundEntry> scatterCfg =
                (ov != null && ov.surfaceScatter().isPresent()) ? ov.surfaceScatter().get() : pal.surfaceScatter();

        final Variant variant = pickVariant(variants, random);
        ResourceLocation surfaceId = (ov != null && ov.surface().isPresent()) ? ov.surface().get() : pal.surface();
        if (variant != null && variant.surfaceOverride().isPresent()) {
            surfaceId = variant.surfaceOverride().get();
        }
        final BlockState surface = resolveBlock(surfaceId, Blocks.GRASS_BLOCK).defaultBlockState();
        final BlockState fill = resolveBlock(fillId, Blocks.DIRT).defaultBlockState();
        final BlockState core = resolveBlock(coreId, Blocks.STONE).defaultBlockState();
        final List<Scatter> scatter = resolveScatter(scatterCfg);
        // Optional banded body (badlands-style strata): a Y-cycled palette replacing fill + core.
        final List<BlockState> bands = resolveBands(pal.fillBands());
        final int bandThickness = Math.max(1, pal.bandThickness());

        // --- pass 1: solid island terrain ---
        final Map<BlockPos, BlockState> blockMap = new LinkedHashMap<>();
        final List<BlockPos> coreList = new ArrayList<>();
        final List<BlockPos> surfaceList = new ArrayList<>();
        final List<BlockPos> bottomList = new ArrayList<>(); // lowest block of each column, for underside hangs
        final ShapeBuilder.Result sh = ShapeBuilder.build(center, shape, surface, fill, core, scatter, bands,
                bandThickness, baseFill, random, blockMap, coreList, surfaceList, bottomList);
        final int baseRadius = sh.baseRadius();
        final int topDome = sh.topDome();

        // Lava content (orthogonal to the override bands): a vein appended to the ore pass, plus the Y-banded
        // lava lakes rolled below before the normal pond.
        final Lava lava = theme.lava().orElse(null);
        final List<OreEntry> oreList = (lava != null && lava.veinChance() > 0f) ? withLavaVein(ores, lava) : ores;
        if (!coreList.isEmpty()) {
            OrePlanner.planOres(blockMap, oreList, coreList, sh.minCoreY(), sh.maxCoreY(), random);
        }

        // Rare structures: at most one germinates in place of the usual island (the first whose chance rolls).
        // Rolled here, before the pond, so a flooded ruin can suppress the pond it stands in for.
        RareStructure rolledRare = null;
        for (final RareStructure rs : theme.rareStructures()) {
            if (rs.matchesBiome(biome) && random.nextFloat() < rs.chance()) {
                rolledRare = rs;
                break;
            }
        }
        final RareStructure rare = rolledRare;

        // Lava lake (Y-banded, rolled before the normal pond): the first height band that matches rolls, and a
        // hit carves a lava pool and suppresses the water pond — so e.g. a sub-zero Aquatic comes up as a stone
        // island with a lava lake instead of a water one.
        boolean lavaLake = false;
        if (lava != null && (rare == null || !rare.suppressPond())) {
            for (final Lava.Lake lk : lava.lakes()) {
                if (lk.matches(center.getY())) {
                    if (random.nextFloat() < lk.chance()) {
                        final Pond pool = lk.toPond();
                        final int lakeY = PondCarver.pondWaterY(center, topDome, baseRadius, pool);
                        final int lakeBottom = lakeY - Math.max(0, pool.depth() - 1);
                        final BlockState lavaState = resolveBlock(pool.block(), Blocks.LAVA).defaultBlockState();
                        final Set<Long> carved = PondCarver.carvePond(blockMap, surfaceList, center, topDome, lakeY, baseRadius, pool, lavaState, random);
                        PondCarver.containPond(blockMap, surfaceList, center, lakeY, lakeBottom, surface, fill, carved, random);
                        lavaLake = true;
                    }
                    break; // only the first matching height band rolls
                }
            }
        }

        // Pond: carve a contained pool into the top centre (placed before trees so mangroves see water).
        final Optional<Pond> pondCfg = (ov != null && ov.pond().isPresent()) ? ov.pond() : theme.pond();
        final Set<Long> pondColumns = new HashSet<>();
        int pondSurfaceTmp = center.getY();
        if (!lavaLake && pondCfg.isPresent() && (rare == null || !rare.suppressPond())) {
            final Pond pond = pondCfg.get();
            // Ponds sit flush with the surface; rivers cut a channel down through it.
            final int waterY = pond.isRiver() ? center.getY() : PondCarver.pondWaterY(center, topDome, baseRadius, pond);
            pondSurfaceTmp = waterY;
            final int bottomY = waterY - Math.max(0, pond.depth() - 1);
            final BlockState pondWater = resolveBlock(pond.block(), Blocks.WATER).defaultBlockState();
            final Set<Long> carved = PondCarver.carvePond(blockMap, surfaceList, center, topDome, waterY, baseRadius, pond, pondWater, random);
            // Wall up any open edge to the water surface (a containing ring) and dress the bed/shore with
            // sand/clay/gravel — before decorations, so cane and lily pads sit on contained ground.
            PondCarver.containPond(blockMap, surfaceList, center, waterY, bottomY, surface, fill, carved, random);
            // Soften the banks (steep / sloped / mixed per island) so a sheer channel can become a gentle shore.
            PondCarver.terraceBanks(blockMap, surfaceList, center, waterY, carved, surface, random);
            PondCarver.placePondPlants(blockMap, center, waterY, pond, carved, random);
            PondCarver.placePondBanks(blockMap, surfaceList, center, pond, carved, random);
            pondColumns.addAll(carved);
        }
        final int pondSurfaceY = pondSurfaceTmp;

        // Curated structure: level a pad and reserve the footprint now (so trees/ground skip it); a jigsaw
        // building (or cluster) is assembled centred on the island after the terrain lands (GenerationJob),
        // and a villager is spawned at every bed in it.
        final List<IslandPlan.JigsawSite> jigsaws = new ArrayList<>();
        final List<IslandPlan.AnimalSpawn> animals = new ArrayList<>();
        // A rolled rare structure replaces the theme's normal jigsaw + animal packs for this island.
        final JigsawConfig jc = rare != null ? rare.jigsaw() : theme.jigsaw().orElse(null);
        final List<AnimalPack> animalPacks = rare != null ? rare.mobs() : theme.animals();
        if (jc != null) {
            final int gy = center.getY() + topDome;
            levelStructurePad(blockMap, surfaceList, center, gy, jc.pad(), surface, fill);
            // JigsawPlacement lands the start piece's anchor block at origin.y - 1, so pass gy + 1 to seat the
            // structure's floor (the anchor layer) flush on the pad at gy — otherwise it sinks a block into it.
            // `sink` buries it further: each block lowers the whole piece so the island's own surface covers it.
            jigsaws.add(new IslandPlan.JigsawSite(jc.pool(), jc.target(), jc.depth(), jc.pad(), jc.ironGolems(),
                    new BlockPos(center.getX(), gy + 1 - jc.sink(), center.getZ())));
            // Dedicated Animal Islands (and rare-structure mobs): roll one weighted pack onto the pad (gy),
            // spawned a block above, so the mob lands on the structure floor that now sits at gy.
            if (!animalPacks.isEmpty()) {
                MobPlanner.rollAnimals(animalPacks, new BlockPos(center.getX(), gy, center.getZ()), animals, random);
            }
        }

        final List<TreeSite> trees = new ArrayList<>();
        if (variant != null) {
            DecorationPlanner.planDecoration(level, blockMap, trees, surfaceList, bottomList, variant.decoration(), random);
        }

        // Waterfalls: short static cascades off the rim (block placements, no flow physics).
        final int waterfalls = (ov != null && ov.waterfalls().isPresent()) ? ov.waterfalls().get() : 0;
        if (waterfalls > 0) {
            DecorationPlanner.placeWaterfalls(blockMap, surfaceList, center, baseRadius, waterfalls, random);
        }

        // Mobs: theme/override sprinkles plus any variant-specific ones, spawned after the island lands.
        final List<MobEntry> baseMobs = (ov != null && ov.mobs().isPresent()) ? ov.mobs().get() : theme.mobs();
        final List<MobEntry> mobCfg = new ArrayList<>(baseMobs);
        if (variant != null) {
            mobCfg.addAll(variant.decoration().mobs());
        }
        final List<IslandPlan.MobSpawn> mobs = new ArrayList<>(MobPlanner.planMobs(mobCfg, surfaceList, random));
        if (pondCfg.isPresent() && !pondColumns.isEmpty()) {
            mobs.addAll(MobPlanner.planPondMobs(center, pondSurfaceY, pondCfg.get(), pondColumns, random));
        }

        final List<BlockPlacement> blocks = new ArrayList<>(blockMap.size());
        for (Map.Entry<BlockPos, BlockState> e : blockMap.entrySet()) {
            blocks.add(new BlockPlacement(e.getKey(), e.getValue()));
        }
        blocks.sort(Comparator.comparingInt(bp -> bp.pos().getY())); // bottom-up grow-in

        // Bee nests are populated with bees once placed (they need a real block entity in the world).
        final List<BlockPos> hives = new ArrayList<>();
        for (Map.Entry<BlockPos, BlockState> e : blockMap.entrySet()) {
            if (e.getValue().is(Blocks.BEE_NEST) || e.getValue().is(Blocks.BEEHIVE)) {
                hives.add(e.getKey());
            }
        }

        return new IslandPlan(blocks, trees, mobs, hives, jigsaws, animals, random);
    }

    /** Flatten a {@code pad}-radius disc to {@code gy} for a building: clear above, solid below, no decoration. */
    private static void levelStructurePad(Map<BlockPos, BlockState> blockMap, List<BlockPos> surfaceList,
                                          BlockPos center, int gy, int pad, BlockState surface, BlockState fill) {
        final int pad2 = pad * pad;
        // A disc, not a square: the village footprints are plus-shaped (corners empty), so a round pad
        // covers them while staying inside a round island's rim — square corners would float past the edge.
        for (int dx = -pad; dx <= pad; dx++) {
            for (int dz = -pad; dz <= pad; dz++) {
                if (dx * dx + dz * dz > pad2) {
                    continue;
                }
                final int wx = center.getX() + dx;
                final int wz = center.getZ() + dz;
                for (int y = gy + 1; y <= gy + PAD_CLEAR_HEIGHT; y++) {
                    blockMap.remove(new BlockPos(wx, y, wz));
                }
                blockMap.put(new BlockPos(wx, gy, wz), surface);
                blockMap.put(new BlockPos(wx, gy - 1, wz), fill);
                blockMap.put(new BlockPos(wx, gy - 2, wz), fill);
            }
        }
        surfaceList.removeIf(p -> {
            final int dx = p.getX() - center.getX();
            final int dz = p.getZ() - center.getZ();
            return dx * dx + dz * dz <= pad2;
        });
    }

    /** @return the first override matching {@code biome}/{@code y}, or {@code null} if none match (use the base theme). */
    /** The ore list with a one-off lava vein appended (rolled last, so it doesn't shift the real ores' RNG). */
    private static List<OreEntry> withLavaVein(List<OreEntry> ores, Lava lava) {
        final List<OreEntry> out = new ArrayList<>(ores);
        out.add(new OreEntry(ResourceLocation.withDefaultNamespace("lava"), lava.veinChance(),
                new IntRange(1, 1), lava.veinSize(), OreDepth.CORE));
        return out;
    }

    private static BiomeOverride matchOverride(List<BiomeOverride> overrides, Holder<Biome> biome, int y,
                                               ResourceLocation dim) {
        for (BiomeOverride o : overrides) {
            if (o.matches(biome, y, dim)) {
                return o;
            }
        }
        return null;
    }

    private static List<Scatter> resolveScatter(List<GroundEntry> cfg) {
        if (cfg.isEmpty()) {
            return List.of();
        }
        List<Scatter> out = new ArrayList<>(cfg.size());
        for (GroundEntry g : cfg) {
            if (BuiltInRegistries.BLOCK.containsKey(g.block())) {
                out.add(new Scatter(BuiltInRegistries.BLOCK.get(g.block()).defaultBlockState(), g.chance()));
            } else {
                Skyseed.LOGGER.warn("[skyseed] theme surface_scatter references unknown block '{}' — skipping", g.block());
            }
        }
        return out;
    }

    private static List<BlockState> resolveBands(List<ResourceLocation> ids) {
        if (ids.isEmpty()) {
            return List.of();
        }
        List<BlockState> out = new ArrayList<>(ids.size());
        for (ResourceLocation id : ids) {
            if (BuiltInRegistries.BLOCK.containsKey(id)) {
                out.add(BuiltInRegistries.BLOCK.get(id).defaultBlockState());
            } else {
                Skyseed.LOGGER.warn("[skyseed] theme fill_bands references unknown block '{}' — skipping", id);
            }
        }
        return out;
    }

    /** @return a weighted-random variant, or {@code null} if the theme has no variants. */
    private static Variant pickVariant(List<Variant> variants, RandomSource random) {
        if (variants.isEmpty()) {
            return null;
        }
        int total = 0;
        for (Variant v : variants) {
            total += Math.max(0, v.weight());
        }
        if (total <= 0) {
            return variants.get(random.nextInt(variants.size()));
        }
        int roll = random.nextInt(total);
        for (Variant v : variants) {
            roll -= Math.max(0, v.weight());
            if (roll < 0) {
                return v;
            }
        }
        return variants.get(variants.size() - 1);
    }

    private static Block resolveBlock(ResourceLocation id, Block fallback) {
        if (id != null && BuiltInRegistries.BLOCK.containsKey(id)) {
            return BuiltInRegistries.BLOCK.get(id);
        }
        Skyseed.LOGGER.warn("[skyseed] theme references unknown block '{}' — using {}",
                id, BuiltInRegistries.BLOCK.getKey(fallback));
        return fallback;
    }
}
