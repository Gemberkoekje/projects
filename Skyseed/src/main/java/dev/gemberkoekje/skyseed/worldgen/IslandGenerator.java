package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.compat.Ids;
import dev.gemberkoekje.skyseed.compat.Lookup;
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
import dev.gemberkoekje.skyseed.worldgen.theme.Underside;
import dev.gemberkoekje.skyseed.worldgen.theme.Variant;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.Level;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.biome.Biome;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.function.Function;
import java.util.function.Supplier;

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
    /** Fallback shape for a dimension override that omits one — small, so it never inherits the overworld silhouette. */
    private static final Shape NEUTRAL_SHAPE = new Shape(new IntRange(3, 3), 0.2f, Underside.TEARDROP,
            new IntRange(1, 1), java.util.Optional.empty(), java.util.List.of());

    public static IslandPlan planIsland(ServerLevel level, BlockPos center, IslandTheme theme,
                                        Holder<Biome> biome, RandomSource random) {
        // Pass 0: resolve the per-island config (palette/shape/variant/snow…) from the theme + matching biome override.
        final Resolved cfg = resolveConfig(level, center, theme, biome, random);
        final boolean useBase = cfg.useBase(); // false off the theme's home dimension — gates the home-only lava pass
        final List<OreEntry> ores = cfg.ores();
        final float snow = cfg.snow();

        // --- pass 1: solid island terrain. The buffers (block map + per-column lists the later passes read) are shared
        // across a cluster's stamps; blockMap/coreList are aliased for the ore + ladder + assembly passes still inline. ---
        final TerrainBuffers buffers = TerrainBuffers.create();
        final Map<BlockPos, BlockState> blockMap = buffers.blockMap();
        final List<BlockPos> coreList = buffers.coreList();
        final ShapeBuilder.Result sh = buildTerrain(center, cfg, random, buffers);
        final int baseRadius = sh.baseRadius();
        final int topDome = sh.topDome();

        // Lava content (orthogonal to the override bands): a vein appended to the ore pass, plus the Y-banded
        // lava lakes rolled below before the normal pond.
        // The theme's lava veins/lakes are tuned for its home (overworld) dimension and would swamp a tiny adapted
        // island; an adaptation supplies its own lava (e.g. Aquatic's lava pond), so the field is home-dimension only.
        final Lava lava = useBase ? theme.lava().orElse(null) : null;
        final List<OreEntry> oreList = (lava != null && lava.veinChance() > 0f) ? withLavaVein(ores, lava) : ores;
        if (!coreList.isEmpty()) {
            OrePlanner.planOres(blockMap, oreList, coreList, sh.minCoreY(), sh.maxCoreY(), random);
        }

        // Rare structures: at most one germinates in place of the usual island. Rolled here, before the pond, so a
        // flooded ruin can suppress the pond it stands in for.
        final RareStructure rare = rollRare(theme, biome, cfg, random);

        // Water: a Y-banded lava lake (rolled first; a hit suppresses the pond) or the theme/override pond/river.
        final Water water = planWater(buffers, cfg, theme, center, sh, lava, rare, random);

        // Curated structure: a jigsaw building/cluster on a levelled pad (assembled later by GenerationJob), or a rare
        // structure's jigsaw + animal packs in its place.
        final StructurePlan structure = planStructure(level, buffers, cfg, theme, rare, center, topDome, random);
        final List<IslandPlan.JigsawSite> jigsaws = structure.jigsaws();
        final List<IslandPlan.AnimalSpawn> animals = structure.animals();

        // Decoration: the variant's trees + ground cover, then any rim waterfalls.
        final Decor decor = planDecoration(level, buffers, cfg, center, baseRadius, random);

        // Mobs: theme/override + variant sprinkles, plus pond/river mobs for a carved pool.
        final List<IslandPlan.MobSpawn> mobs = planMobs(cfg, theme, buffers, center, water, random);

        // Ladder shaft: punch a climbable way down through the centre to a landing far below — a "home-grown" route
        // to mining level. Applied in every dimension the seed grows in (it's structure, not biome content), and
        // carved last so it cuts cleanly through the finished terrain.
        final List<BlockPos> fluidTicks = new ArrayList<>();
        theme.ladderShaft().ifPresent(shaft -> ShaftPlanner.carve(blockMap, center, shaft, random, fluidTicks));

        final List<BlockPlacement> blocks = sortedBlocks(blockMap);
        final List<BlockPos> hives = beeNests(blockMap);

        // Cross-dimension twin (Ruined Portal): a rolled rare structure's twin wins, else the theme's own.
        final Optional<ResourceLocation> twinTheme =
                (rare != null && rare.twin().isPresent()) ? rare.twin() : theme.twin();
        return new IslandPlan(blocks, decor.trees(), mobs, hives, jigsaws, animals, random, twinTheme, fluidTicks,
                decor.scatterPositions(), snow);
    }

    /** The per-island config resolved from the theme + the matching biome override (see {@link #resolveConfig}). */
    private record Resolved(BiomeOverride ov, boolean useBase, ResourceLocation dim, Shape shape, List<OreEntry> ores,
                            Variant variant, BlockState surface, BlockState fill, BlockState core, float snow,
                            List<Scatter> scatter, List<BlockState> bands, int bandThickness, int baseFill) {}

    /**
     * Pass 0: resolve every per-island field from the theme and the matching biome override (each via {@link #eff}),
     * roll the variant, and turn the block ids into states. {@code useBase} is false off the theme's home dimension, so
     * an unset field falls to a neutral default rather than leaking overworld content across the portal.
     */
    private static Resolved resolveConfig(ServerLevel level, BlockPos center, IslandTheme theme, Holder<Biome> biome,
                                          RandomSource random) {
        final ResourceLocation dim = level.dimension().location();
        final boolean useBase = theme.baseValidIn(dim);
        final BiomeOverride ov = matchOverride(theme.biomeOverrides(), biome, center.getY(), dim, useBase);
        final ResourceLocation neutralBlock = Ids.mc(dim.getPath().equals("the_end") ? "end_stone" : "netherrack");
        final Palette pal = theme.palette();
        final Shape shape = eff(ov, BiomeOverride::shape, useBase, theme::shape, NEUTRAL_SHAPE);
        final List<OreEntry> ores = eff(ov, BiomeOverride::ores, useBase, theme::ores, List.<OreEntry>of());
        final List<Variant> variants = eff(ov, BiomeOverride::variants, useBase, theme::variants, List.<Variant>of());
        final ResourceLocation fillId = eff(ov, BiomeOverride::fill, useBase, pal::fill, neutralBlock);
        final ResourceLocation coreId = eff(ov, BiomeOverride::core, useBase, pal::core, neutralBlock);
        final int baseFill = eff(ov, BiomeOverride::fillDepth, useBase, pal::fillDepth, 2);
        final List<GroundEntry> scatterCfg = eff(ov, BiomeOverride::surfaceScatter, useBase, pal::surfaceScatter, List.<GroundEntry>of());

        final Variant variant = pickVariant(variants, random);
        ResourceLocation surfaceId = eff(ov, BiomeOverride::surface, useBase, pal::surface, neutralBlock);
        if (variant != null && variant.surfaceOverride().isPresent()) {
            surfaceId = variant.surfaceOverride().get();
        }
        final BlockState surface = resolveBlock(surfaceId, Blocks.GRASS_BLOCK).defaultBlockState();
        final BlockState fill = resolveBlock(fillId, Blocks.DIRT).defaultBlockState();
        final BlockState core = resolveBlock(coreId, Blocks.STONE).defaultBlockState();
        final float snow = variant != null && variant.snow().isPresent() ? variant.snow().get()
                : eff(ov, BiomeOverride::snow, useBase, pal::snow, 0f);
        final List<Scatter> scatter = resolveScatter(scatterCfg);
        final List<BlockState> bands = resolveBands(
                eff(ov, BiomeOverride::fillBands, useBase, pal::fillBands, List.<ResourceLocation>of()));
        final int bandThickness = Math.max(1, pal.bandThickness());
        return new Resolved(ov, useBase, dim, shape, ores, variant, surface, fill, core, snow, scatter, bands,
                bandThickness, baseFill);
    }

    /**
     * Pass 1: stamp the island body into {@code buffers}. A ring cluster ({@code cluster_offsets} set) stamps the shape
     * at each offset and leaves the centre void; a normal island stamps once at the centre. The first stamp gives the
     * shape {@link ShapeBuilder.Result} (rolled radius/dome + core Y-range) the later passes reuse.
     */
    private static ShapeBuilder.Result buildTerrain(BlockPos center, Resolved cfg, RandomSource random,
                                                    TerrainBuffers buffers) {
        final List<BlockPos> clusterOffsets = cfg.shape().clusterOffsets();
        final BlockPos firstCentre = clusterOffsets.isEmpty() ? center
                : center.offset(clusterOffsets.get(0).getX(), 0, clusterOffsets.get(0).getZ());
        final ShapeBuilder.Result sh = ShapeBuilder.build(firstCentre, cfg.shape(), cfg.surface(), cfg.fill(),
                cfg.core(), cfg.scatter(), cfg.bands(), cfg.bandThickness(), cfg.baseFill(), random, buffers);
        for (int k = 1; k < clusterOffsets.size(); k++) {
            final BlockPos off = clusterOffsets.get(k);
            ShapeBuilder.build(center.offset(off.getX(), 0, off.getZ()), cfg.shape(), cfg.surface(), cfg.fill(),
                    cfg.core(), cfg.scatter(), cfg.bands(), cfg.bandThickness(), cfg.baseFill(), random, buffers);
        }
        return sh;
    }

    /**
     * Roll for a rare structure (the first whose chance hits) that germinates in place of the usual island. Gated to the
     * theme's home dimension unless the structure names its own. Consumes one RNG roll per candidate up to the hit.
     */
    private static RareStructure rollRare(IslandTheme theme, Holder<Biome> biome, Resolved cfg, RandomSource random) {
        for (final RareStructure rs : theme.rareStructures()) {
            if (rs.rollsIn(cfg.dim(), cfg.useBase()) && rs.matchesBiome(biome) && random.nextFloat() < rs.chance()) {
                return rs;
            }
        }
        return null;
    }

    /** Materialise the block map into the plan's placement list, sorted bottom-up so the grow-in animation rises. */
    private static List<BlockPlacement> sortedBlocks(Map<BlockPos, BlockState> blockMap) {
        final List<BlockPlacement> blocks = new ArrayList<>(blockMap.size());
        for (final Map.Entry<BlockPos, BlockState> e : blockMap.entrySet()) {
            blocks.add(new BlockPlacement(e.getKey(), e.getValue()));
        }
        blocks.sort(Comparator.comparingInt(bp -> bp.pos().getY()));
        return blocks;
    }

    /** The positions of every bee nest/hive in the block map (populated with bees once placed in the world). */
    private static List<BlockPos> beeNests(Map<BlockPos, BlockState> blockMap) {
        final List<BlockPos> hives = new ArrayList<>();
        for (final Map.Entry<BlockPos, BlockState> e : blockMap.entrySet()) {
            if (e.getValue().is(Blocks.BEE_NEST) || e.getValue().is(Blocks.BEEHIVE)) {
                hives.add(e.getKey());
            }
        }
        return hives;
    }

    /** The carved water the mob pass needs: the resolved pond config + its carved columns + the water surface Y. */
    private record Water(Optional<Pond> pondCfg, Set<Long> pondColumns, int pondSurfaceY) {}

    /**
     * Carve the island's water. A Y-banded lava lake (home dimension only) rolls first and, on a hit, carves a lava pool
     * and suppresses the water pond — so a sub-zero Aquatic comes up with a lava lake, not a pond. Otherwise the
     * theme/override pond (or river) is carved, contained and dressed. A rare structure that suppresses the pond skips
     * both. Returns what the later mob pass needs to seed pond/river mobs.
     */
    private static Water planWater(TerrainBuffers buffers, Resolved cfg, IslandTheme theme, BlockPos center,
                                   ShapeBuilder.Result sh, Lava lava, RareStructure rare, RandomSource random) {
        final Map<BlockPos, BlockState> blockMap = buffers.blockMap();
        final List<BlockPos> surfaceList = buffers.surfaceList();
        final int topDome = sh.topDome();
        final int baseRadius = sh.baseRadius();
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
                        PondCarver.containPond(blockMap, surfaceList, center, lakeY, lakeBottom, cfg.surface(), cfg.fill(), carved, random);
                        lavaLake = true;
                    }
                    break; // only the first matching height band rolls
                }
            }
        }

        final Optional<Pond> pondCfg = (cfg.ov() != null && cfg.ov().pond().isPresent()) ? cfg.ov().pond()
                : (cfg.useBase() ? theme.pond() : Optional.<Pond>empty());
        final Set<Long> pondColumns = new HashSet<>();
        int pondSurfaceY = center.getY();
        if (!lavaLake && pondCfg.isPresent() && (rare == null || !rare.suppressPond())) {
            final Pond pond = pondCfg.get();
            // Ponds sit flush with the surface; rivers cut a channel down through it.
            final int waterY = pond.isRiver() ? center.getY() : PondCarver.pondWaterY(center, topDome, baseRadius, pond);
            pondSurfaceY = waterY;
            final int bottomY = waterY - Math.max(0, pond.depth() - 1);
            final BlockState pondWater = resolveBlock(pond.block(), Blocks.WATER).defaultBlockState();
            final Set<Long> carved = PondCarver.carvePond(blockMap, surfaceList, center, topDome, waterY, baseRadius, pond, pondWater, random);
            // Wall up any open edge to the water surface (a containing ring) and dress the bed/shore with
            // sand/clay/gravel — before decorations, so cane and lily pads sit on contained ground.
            PondCarver.containPond(blockMap, surfaceList, center, waterY, bottomY, cfg.surface(), cfg.fill(), carved, random);
            // Soften the banks (steep / sloped / mixed per island) so a sheer channel can become a gentle shore.
            PondCarver.terraceBanks(blockMap, surfaceList, center, waterY, carved, cfg.surface(), random);
            PondCarver.placePondPlants(blockMap, center, waterY, pond, carved, random);
            PondCarver.placePondBanks(blockMap, surfaceList, center, pond, carved, random);
            pondColumns.addAll(carved);
        }
        return new Water(pondCfg, pondColumns, pondSurfaceY);
    }

    /** The curated structure pass's output: the jigsaw site(s) to assemble and any guaranteed animal spawns. */
    private record StructurePlan(List<IslandPlan.JigsawSite> jigsaws, List<IslandPlan.AnimalSpawn> animals) {}

    /**
     * Curated structure: level a pad and reserve a jigsaw building (or cluster) centred on the island, assembled later
     * by {@link GenerationJob}. A rolled rare structure replaces the theme's jigsaw + animal packs; otherwise a matching
     * biome override may swap the jigsaw build. The shop/lot cap is rolled here from the island RNG so it's reproducible.
     */
    private static StructurePlan planStructure(ServerLevel level, TerrainBuffers buffers, Resolved cfg,
                                               IslandTheme theme, RareStructure rare, BlockPos center, int topDome,
                                               RandomSource random) {
        final List<IslandPlan.JigsawSite> jigsaws = new ArrayList<>();
        final List<IslandPlan.AnimalSpawn> animals = new ArrayList<>();
        final JigsawConfig jcBase = (cfg.ov() != null && cfg.ov().jigsaw().isPresent()) ? cfg.ov().jigsaw().get()
                : theme.jigsaw().orElse(null);
        final JigsawConfig jcRaw = rare != null ? rare.jigsaw() : jcBase;
        final JigsawConfig jc = jcRaw != null ? dimensionVariant(level, jcRaw) : null;
        final List<AnimalPack> animalPacks = rare != null ? rare.mobs() : theme.animals();
        if (jc != null) {
            final int gy = center.getY() + topDome;
            levelStructurePad(buffers.blockMap(), buffers.surfaceList(), center, gy, jc.pad(), cfg.surface(), cfg.fill());
            // JigsawPlacement lands the start piece's anchor block at origin.y - 1, so pass gy + 1 to seat the floor flush
            // on the pad; `sink` buries it further. The cap: a fixed cap_count, or — when cap_min is set below it — a
            // target rolled in [cap_min, cap_count] now, so a trade post lands a reproducible-but-varied 2–4 shops.
            final int cap = jc.capMin() > 0 && jc.capMin() < jc.capCount()
                    ? jc.capMin() + random.nextInt(jc.capCount() - jc.capMin() + 1)
                    : jc.capCount();
            jigsaws.add(new IslandPlan.JigsawSite(jc.pool(), jc.target(), jc.depth(), jc.pad(), jc.ironGolems(),
                    new BlockPos(center.getX(), gy + 1 - jc.sink(), center.getZ()), jc.reach(),
                    jc.capPrefix(), cap, jc.capFiller(), jc.centerpiece()));
            // An Animal Island (or a rare structure's mobs): roll one weighted pack onto the pad, a block above its floor.
            if (!animalPacks.isEmpty()) {
                MobPlanner.rollAnimals(animalPacks, new BlockPos(center.getX(), gy, center.getZ()), animals, random);
            }
        }
        return new StructurePlan(jigsaws, animals);
    }

    /** The decoration pass's output: the planned trees and the surface-scatter positions reserved from later passes. */
    private record Decor(List<TreeSite> trees, Set<BlockPos> scatterPositions) {}

    /**
     * Decoration: the rolled variant's trees + ground cover (placed before water-side passes have run their course), then
     * any rim waterfalls. Both are no-ops when the variant/override doesn't ask for them.
     */
    private static Decor planDecoration(ServerLevel level, TerrainBuffers buffers, Resolved cfg, BlockPos center,
                                        int baseRadius, RandomSource random) {
        final List<TreeSite> trees = new ArrayList<>();
        final Set<BlockPos> scatterPositions = new HashSet<>();
        if (cfg.variant() != null) {
            DecorationPlanner.planDecoration(level, buffers.blockMap(), trees, buffers.surfaceList(),
                    buffers.bottomList(), cfg.variant().decoration(), scatterPositions, random);
        }
        // Waterfalls: short static cascades off the rim (block placements, no flow physics).
        final int waterfalls = (cfg.ov() != null && cfg.ov().waterfalls().isPresent()) ? cfg.ov().waterfalls().get() : 0;
        if (waterfalls > 0) {
            DecorationPlanner.placeWaterfalls(buffers.blockMap(), buffers.surfaceList(), center, baseRadius, waterfalls, random);
        }
        return new Decor(trees, scatterPositions);
    }

    /**
     * Mobs: the theme/override sprinkles plus any variant-specific ones, then pond/river mobs for a carved pool. Spawned
     * (by {@link GenerationJob}) once the island lands.
     */
    private static List<IslandPlan.MobSpawn> planMobs(Resolved cfg, IslandTheme theme, TerrainBuffers buffers,
                                                      BlockPos center, Water water, RandomSource random) {
        final List<MobEntry> mobCfg = new ArrayList<>(
                eff(cfg.ov(), BiomeOverride::mobs, cfg.useBase(), theme::mobs, List.<MobEntry>of()));
        if (cfg.variant() != null) {
            mobCfg.addAll(cfg.variant().decoration().mobs());
        }
        final List<IslandPlan.MobSpawn> mobs = new ArrayList<>(MobPlanner.planMobs(mobCfg, buffers.surfaceList(), random));
        if (water.pondCfg().isPresent() && !water.pondColumns().isEmpty()) {
            mobs.addAll(MobPlanner.planPondMobs(center, water.pondSurfaceY(), water.pondCfg().get(),
                    water.pondColumns(), random));
        }
        return mobs;
    }

    /**
     * The effective value of one per-island field: the matching biome override's value when it sets that field, else
     * the base theme's value when the base config is valid in this dimension ({@code useBase}), else a dimension-neutral
     * default — so a foreign-dimension override is a complete spec and never inherits overworld content. The single
     * place planIsland's per-field "override over base over neutral" resolution lives (one call per overridable field).
     */
    private static <T> T eff(BiomeOverride ov, Function<BiomeOverride, Optional<T>> field,
                             boolean useBase, Supplier<T> base, T neutral) {
        final Optional<T> overridden = ov != null ? field.apply(ov) : Optional.empty();
        return overridden.orElseGet(() -> useBase ? base.get() : neutral);
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
        out.add(new OreEntry(Ids.mc("lava"), lava.veinChance(),
                new IntRange(1, 1), lava.veinSize(), OreDepth.CORE));
        return out;
    }

    private static BiomeOverride matchOverride(List<BiomeOverride> overrides, Holder<Biome> biome, int y,
                                               ResourceLocation dim, boolean baseValidHere) {
        for (BiomeOverride o : overrides) {
            // A non-dimensioned override belongs to the base config's home dimension(s); it must not leak into a
            // foreign dimension (e.g. an overworld biome tweak applying to a seed thrown in the Nether). Only
            // dimension-keyed overrides for the current dimension are eligible there.
            if (o.dimension().isEmpty() && !baseValidHere) {
                continue;
            }
            if (o.matches(biome, y, dim)) {
                return o;
            }
        }
        return null;
    }

    /**
     * Whether {@code theme} can grow an island in dimension {@code dim} at {@code biome}/{@code y}: true if the base
     * config is declared for {@code dim}, or a dimension-keyed override for {@code dim} matches here. False means the
     * seed must <em>fizzle</em> — it has no implementation for this dimension and must not fall back to the foreign
     * base form (e.g. an overworld seed thrown in the Nether). See SKYNETHERPLAN and {@code IslandSeedEntity}.
     */
    public static boolean formValidFor(IslandTheme theme, Holder<Biome> biome, int y, ResourceLocation dim) {
        if (theme.fizzlesIn(biome)) {
            return false; // a hard biome exclusion (e.g. bastions never form in the basalt deltas)
        }
        if (theme.baseValidIn(dim)) {
            return true;
        }
        return matchOverride(theme.biomeOverrides(), biome, y, dim, false) != null;
    }

    /**
     * In the Nether, prefer a {@code <pool>_nether} variant of a theme's jigsaw pool if one is registered — so e.g.
     * the Ruined Portal places its no-goodies Nether frame ({@code skyseed:ruined_portal/portal_nether}) instead of
     * the Overworld treasure version. A general convention: any structure can ship a Nether variant by providing the
     * suffixed template pool. See SKYNETHERPLAN (Ruined Portal twins).
     */
    private static JigsawConfig dimensionVariant(ServerLevel level, JigsawConfig jc) {
        if (level.dimension() != Level.NETHER) {
            return jc;
        }
        final ResourceLocation netherPool = Ids.of(
                jc.pool().getNamespace(), jc.pool().getPath() + "_nether");
        if (Lookup.hasTemplatePool(level.registryAccess(), netherPool)) {
            return new JigsawConfig(netherPool, jc.target(), jc.depth(), jc.pad(), jc.ironGolems(), jc.sink(), jc.reach(),
                    jc.capPrefix(), jc.capCount(), jc.capMin(), jc.capFiller(), jc.centerpiece());
        }
        return jc;
    }

    private static List<Scatter> resolveScatter(List<GroundEntry> cfg) {
        if (cfg.isEmpty()) {
            return List.of();
        }
        List<Scatter> out = new ArrayList<>(cfg.size());
        for (GroundEntry g : cfg) {
            if (Lookup.hasBlock(g.block())) {
                out.add(new Scatter(Lookup.blockState(g.block()), g.chance()));
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
            if (Lookup.hasBlock(id)) {
                out.add(Lookup.blockState(id));
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
        if (id != null && Lookup.hasBlock(id)) {
            return Lookup.block(id);
        }
        Skyseed.LOGGER.warn("[skyseed] theme references unknown block '{}' — using {}",
                id, Lookup.blockId(fallback));
        return fallback;
    }
}
