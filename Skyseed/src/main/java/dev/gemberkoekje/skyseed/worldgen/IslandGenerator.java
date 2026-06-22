package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan.BlockPlacement;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan.TreeSite;
import dev.gemberkoekje.skyseed.worldgen.theme.AnimalPack;
import dev.gemberkoekje.skyseed.worldgen.theme.BiomeOverride;
import dev.gemberkoekje.skyseed.worldgen.theme.Decoration;
import dev.gemberkoekje.skyseed.worldgen.theme.GroundEntry;
import dev.gemberkoekje.skyseed.worldgen.theme.IslandTheme;
import dev.gemberkoekje.skyseed.worldgen.theme.MobEntry;
import dev.gemberkoekje.skyseed.worldgen.theme.OreDepth;
import dev.gemberkoekje.skyseed.worldgen.theme.OreEntry;
import dev.gemberkoekje.skyseed.worldgen.theme.Palette;
import dev.gemberkoekje.skyseed.worldgen.theme.Pond;
import dev.gemberkoekje.skyseed.worldgen.theme.JigsawConfig;
import dev.gemberkoekje.skyseed.worldgen.theme.RareStructure;
import dev.gemberkoekje.skyseed.worldgen.theme.Shape;
import dev.gemberkoekje.skyseed.worldgen.theme.TreeEntry;
import dev.gemberkoekje.skyseed.worldgen.theme.Variant;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.Holder;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.level.biome.Biome;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.DoublePlantBlock;
import net.minecraft.world.level.block.LeavesBlock;
import net.minecraft.world.level.block.LiquidBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;
import net.minecraft.world.level.block.state.properties.DoubleBlockHalf;
import net.minecraft.world.level.block.state.properties.DripstoneThickness;
import net.minecraft.world.level.chunk.ChunkGenerator;
import net.minecraft.world.level.levelgen.feature.ConfiguredFeature;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;

/**
 * Computes an island (README → Generation algorithm) from an {@link IslandTheme}, with the theme's base config overlaid by
 * the first matching {@link BiomeOverride} for the biome the seed landed in. Near-pure: returns an
 * {@link IslandPlan} without writing the world (a {@link GenerationJob} drains it over ticks).
 */
public final class IslandGenerator {
    /** A resolved surface-scatter choice. */
    private record Scatter(BlockState state, float chance) {}

    private IslandGenerator() {}

    public static IslandPlan planIsland(ServerLevel level, BlockPos center, IslandTheme theme,
                                        Holder<Biome> biome, RandomSource random) {
        final BiomeOverride ov = matchOverride(theme.biomeOverrides(), biome, center.getY());

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

        // --- shape parameters ---
        final int baseRadius = Math.max(1, shape.radius().sample(random));
        final double rimNoise = shape.rimNoise();
        final int topDome = shape.topDome().sample(random);
        final double maxDepth = baseRadius * 1.05;

        // Irregular rim from a few angular harmonics (decorrelated per island via the RandomSource).
        final int[] freq = { 2, 3, 5 };
        final double[] amp = new double[freq.length];
        final double[] phase = new double[freq.length];
        double ampSum = 0;
        for (int k = 0; k < freq.length; k++) {
            amp[k] = 0.3 + random.nextDouble();
            ampSum += amp[k];
            phase[k] = random.nextDouble() * Math.PI * 2.0;
        }
        for (int k = 0; k < freq.length; k++) {
            amp[k] = amp[k] / ampSum * rimNoise;
        }

        final int maxR = (int) Math.ceil(baseRadius * (1.0 + rimNoise)) + 1;
        final Map<BlockPos, BlockState> blockMap = new LinkedHashMap<>();
        final List<BlockPos> coreList = new ArrayList<>();
        final List<BlockPos> surfaceList = new ArrayList<>();
        final List<BlockPos> bottomList = new ArrayList<>(); // lowest block of each column, for underside hangs
        int minCoreY = Integer.MAX_VALUE;
        int maxCoreY = Integer.MIN_VALUE;

        // --- pass 1: solid island, layered fill ---
        for (int dx = -maxR; dx <= maxR; dx++) {
            for (int dz = -maxR; dz <= maxR; dz++) {
                final double dist = Math.sqrt((double) dx * dx + (double) dz * dz);
                final double angle = Math.atan2(dz, dx);

                double rim = baseRadius;
                for (int k = 0; k < freq.length; k++) {
                    rim += baseRadius * amp[k] * Math.sin(freq[k] * angle + phase[k]);
                }
                if (dist > rim) {
                    continue;
                }

                final double t = Math.min(1.0, dist / Math.max(0.001, rim));
                final double bulge = 1.0 - t * t;

                final int dome = (int) Math.round(topDome * bulge);
                int depth = (int) Math.round(maxDepth * Math.pow(bulge, 0.85));
                if (random.nextFloat() < 0.3) {
                    depth += random.nextInt(2);
                }

                final int wx = center.getX() + dx;
                final int wz = center.getZ() + dz;
                final int surfaceY = center.getY() + dome;
                final int bottomY = center.getY() - depth;
                final int fillThickness = baseFill + random.nextInt(3) - 1;

                for (int y = bottomY; y <= surfaceY; y++) {
                    final BlockPos p = new BlockPos(wx, y, wz);
                    if (y == surfaceY) {
                        blockMap.put(p, scatterSurface(surface, scatter, random));
                        surfaceList.add(p);
                    } else if (y >= surfaceY - fillThickness) {
                        blockMap.put(p, bands.isEmpty() ? fill : bandAt(bands, y, bandThickness));
                    } else {
                        blockMap.put(p, bands.isEmpty() ? core : bandAt(bands, y, bandThickness));
                        coreList.add(p);
                        minCoreY = Math.min(minCoreY, y);
                        maxCoreY = Math.max(maxCoreY, y);
                    }
                }
                bottomList.add(new BlockPos(wx, bottomY, wz));
            }
        }

        if (!coreList.isEmpty()) {
            planOres(blockMap, ores, coreList, minCoreY, maxCoreY, random);
        }

        // Rare structures: at most one germinates in place of the usual island (the first whose chance rolls).
        // Rolled here, before the pond, so a flooded ruin can suppress the pond it stands in for.
        RareStructure rolledRare = null;
        for (final RareStructure rs : theme.rareStructures()) {
            if (random.nextFloat() < rs.chance()) {
                rolledRare = rs;
                break;
            }
        }
        final RareStructure rare = rolledRare;

        // Pond: carve a contained pool into the top centre (placed before trees so mangroves see water).
        final Optional<Pond> pondCfg = (ov != null && ov.pond().isPresent()) ? ov.pond() : theme.pond();
        final Set<Long> pondColumns = new HashSet<>();
        int pondSurfaceTmp = center.getY();
        if (pondCfg.isPresent() && (rare == null || !rare.suppressPond())) {
            final Pond pond = pondCfg.get();
            // Ponds sit flush with the surface; rivers cut a channel down through it.
            final int waterY = pond.isRiver() ? center.getY() : pondWaterY(center, topDome, baseRadius, pond);
            pondSurfaceTmp = waterY;
            final int bottomY = waterY - Math.max(0, pond.depth() - 1);
            final Set<Long> carved = carvePond(blockMap, surfaceList, center, topDome, waterY, baseRadius, pond, random);
            // Wall up any open edge to the water surface (a containing ring) and dress the bed/shore with
            // sand/clay/gravel — before decorations, so cane and lily pads sit on contained ground.
            containPond(blockMap, surfaceList, center, waterY, bottomY, surface, fill, carved, random);
            placePondPlants(blockMap, center, waterY, pond, carved, random);
            placePondBanks(blockMap, surfaceList, center, pond, carved, random);
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
            jigsaws.add(new IslandPlan.JigsawSite(jc.pool(), jc.target(), jc.depth(), jc.pad(), jc.ironGolems(),
                    new BlockPos(center.getX(), gy + 1, center.getZ())));
            // Dedicated Animal Islands (and rare-structure mobs): roll one weighted pack onto the pad (gy),
            // spawned a block above, so the mob lands on the structure floor that now sits at gy.
            if (!animalPacks.isEmpty()) {
                rollAnimals(animalPacks, new BlockPos(center.getX(), gy, center.getZ()), animals, random);
            }
        }

        final List<TreeSite> trees = new ArrayList<>();
        if (variant != null) {
            planDecoration(level, blockMap, trees, surfaceList, bottomList, variant.decoration(), random);
        }

        // Waterfalls: short static cascades off the rim (block placements, no flow physics).
        final int waterfalls = (ov != null && ov.waterfalls().isPresent()) ? ov.waterfalls().get() : 0;
        if (waterfalls > 0) {
            placeWaterfalls(blockMap, surfaceList, center, baseRadius, waterfalls, random);
        }

        // Mobs: theme/override sprinkles plus any variant-specific ones, spawned after the island lands.
        final List<MobEntry> baseMobs = (ov != null && ov.mobs().isPresent()) ? ov.mobs().get() : theme.mobs();
        final List<MobEntry> mobCfg = new ArrayList<>(baseMobs);
        if (variant != null) {
            mobCfg.addAll(variant.decoration().mobs());
        }
        final List<IslandPlan.MobSpawn> mobs = new ArrayList<>(planMobs(mobCfg, surfaceList, random));
        if (pondCfg.isPresent() && !pondColumns.isEmpty()) {
            mobs.addAll(planPondMobs(center, pondSurfaceY, pondCfg.get(), pondColumns, random));
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

    /** Entity types that must be spawned submerged (Aquarium) — they die if placed on dry land. */
    private static final java.util.Set<EntityType<?>> AQUATIC = java.util.Set.of(
            EntityType.AXOLOTL, EntityType.SQUID, EntityType.GLOW_SQUID, EntityType.TURTLE,
            EntityType.TROPICAL_FISH, EntityType.COD, EntityType.SALMON, EntityType.PUFFERFISH, EntityType.DOLPHIN);

    /** Pick one weighted pack and expand it into concrete animal spawns jittered around the enclosure centre. */
    private static void rollAnimals(List<AnimalPack> packs, BlockPos center, List<IslandPlan.AnimalSpawn> out, RandomSource random) {
        final int total = packs.stream().mapToInt(AnimalPack::weight).sum();
        if (total <= 0) {
            return;
        }
        int roll = random.nextInt(total);
        AnimalPack chosen = packs.get(0);
        for (AnimalPack p : packs) {
            if (roll < p.weight()) {
                chosen = p;
                break;
            }
            roll -= p.weight();
        }
        for (AnimalPack.Entry e : chosen.entries()) {
            final EntityType<?> type = resolveEntity(e.entity());
            if (type == null) {
                continue;
            }
            final boolean water = AQUATIC.contains(type);
            for (int i = 0; i < e.adults() + e.babies(); i++) {
                final int dx = random.nextInt(3) - 1;
                final int dz = random.nextInt(3) - 1;
                // Land: stand on the pad (pos = surface, spawn goes on top). Water: a block up, inside the tank.
                final BlockPos pos = new BlockPos(center.getX() + dx, center.getY() + (water ? 1 : 0), center.getZ() + dz);
                out.add(new IslandPlan.AnimalSpawn(type, pos, i >= e.adults(), water));
            }
        }
    }

    /** Flatten a {@code pad}-radius square to {@code gy} for a building: clear above, solid below, no decoration. */
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
                for (int y = gy + 1; y <= gy + 10; y++) {
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

    private static BiomeOverride matchOverride(List<BiomeOverride> overrides, Holder<Biome> biome, int y) {
        for (BiomeOverride o : overrides) {
            if (o.matches(biome, y)) {
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

    /** Roll each configured mob and pick random surface columns to spawn them on (after generation). */
    private static List<IslandPlan.MobSpawn> planMobs(List<MobEntry> cfg, List<BlockPos> surfaceList, RandomSource random) {
        final List<IslandPlan.MobSpawn> out = new ArrayList<>();
        if (cfg.isEmpty() || surfaceList.isEmpty()) {
            return out;
        }
        for (MobEntry m : cfg) {
            if (random.nextFloat() >= m.chance()) {
                continue;
            }
            final EntityType<?> type = resolveEntity(m.entity());
            if (type == null) {
                continue;
            }
            final int n = m.count().sample(random);
            for (int i = 0; i < n; i++) {
                out.add(new IslandPlan.MobSpawn(type, surfaceList.get(random.nextInt(surfaceList.size())), false));
            }
        }
        return out;
    }

    /** Roll each pond water mob and pick random submerged positions inside the carved water columns. */
    private static List<IslandPlan.MobSpawn> planPondMobs(BlockPos center, int waterY, Pond pond, Set<Long> carved, RandomSource random) {
        final List<IslandPlan.MobSpawn> out = new ArrayList<>();
        if (pond.waterMobs().isEmpty() || carved.isEmpty()) {
            return out;
        }
        final int bottomY = waterY - Math.max(0, pond.depth() - 1);
        final List<int[]> cols = new ArrayList<>();
        for (long k : carved) {
            cols.add(new int[]{(int) (k >> 32), (int) k});
        }
        for (MobEntry m : pond.waterMobs()) {
            if (random.nextFloat() >= m.chance()) {
                continue;
            }
            final EntityType<?> type = resolveEntity(m.entity());
            if (type == null) {
                continue;
            }
            final int n = m.count().sample(random);
            for (int i = 0; i < n; i++) {
                final int[] c = cols.get(random.nextInt(cols.size()));
                // Spawn below the surface (water above them) so squid/glow squid stay submerged.
                final int y = bottomY + random.nextInt(Math.max(1, waterY - bottomY));
                out.add(new IslandPlan.MobSpawn(type, new BlockPos(center.getX() + c[0], y, center.getZ() + c[1]), true));
            }
        }
        return out;
    }

    private static EntityType<?> resolveEntity(ResourceLocation id) {
        if (BuiltInRegistries.ENTITY_TYPE.containsKey(id)) {
            return BuiltInRegistries.ENTITY_TYPE.get(id);
        }
        Skyseed.LOGGER.warn("[skyseed] theme references unknown entity '{}' — skipping", id);
        return null;
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

    /** The band block for a given world Y: strata {@code thickness} tall, cycling through the list. */
    private static BlockState bandAt(List<BlockState> bands, int y, int thickness) {
        return bands.get(Math.floorMod(Math.floorDiv(y, thickness), bands.size()));
    }

    /** The surface block for a column: the default, unless a surface-scatter entry rolls in. */
    private static BlockState scatterSurface(BlockState surface, List<Scatter> scatter, RandomSource random) {
        if (scatter.isEmpty()) {
            return surface;
        }
        float roll = random.nextFloat();
        for (Scatter s : scatter) {
            roll -= s.chance();
            if (roll < 0) {
                return s.state();
            }
        }
        return surface;
    }

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

    private static void planOres(Map<BlockPos, BlockState> blockMap, List<OreEntry> ores, List<BlockPos> coreList,
                                 int minCoreY, int maxCoreY, RandomSource random) {
        final Set<Long> coreSet = new HashSet<>(coreList.size() * 2);
        for (BlockPos p : coreList) {
            coreSet.add(p.asLong());
        }
        final int deepMaxY = minCoreY + (int) Math.round((maxCoreY - minCoreY) * 0.4); // lower 40% = deep_core

        for (OreEntry ore : ores) {
            if (!BuiltInRegistries.BLOCK.containsKey(ore.block())) {
                Skyseed.LOGGER.warn("[skyseed] theme ore references unknown block '{}' — skipping", ore.block());
                continue;
            }
            if (random.nextFloat() >= ore.chance()) {
                continue;
            }
            final BlockState state = BuiltInRegistries.BLOCK.get(ore.block()).defaultBlockState();
            final int veins = ore.count().sample(random);
            for (int v = 0; v < veins; v++) {
                final BlockPos seed = pickSeed(coreList, coreSet, ore.depth(), deepMaxY, random);
                if (seed != null) {
                    growVein(blockMap, seed, state, ore.veinSize().sample(random), coreSet, random);
                }
            }
        }
    }

    private static BlockPos pickSeed(List<BlockPos> coreList, Set<Long> coreSet, OreDepth depth,
                                     int deepMaxY, RandomSource random) {
        for (int tries = 0; tries < 16; tries++) {
            final BlockPos c = coreList.get(random.nextInt(coreList.size()));
            if (!coreSet.contains(c.asLong())) {
                continue;
            }
            if (depth == OreDepth.DEEP_CORE && c.getY() > deepMaxY) {
                continue;
            }
            return c;
        }
        return null;
    }

    private static void growVein(Map<BlockPos, BlockState> blockMap, BlockPos seed, BlockState ore, int size,
                                 Set<Long> coreSet, RandomSource random) {
        final List<BlockPos> placed = new ArrayList<>();
        blockMap.put(seed, ore);
        coreSet.remove(seed.asLong());
        placed.add(seed);

        int attempts = 0;
        while (placed.size() < size && attempts < size * 10) {
            attempts++;
            final BlockPos from = placed.get(random.nextInt(placed.size()));
            // Favour growing to a face neighbour (compact, vanilla-looking veins); allow diagonals, but rarely.
            final BlockPos nb;
            if (random.nextFloat() < 0.80f) {
                nb = from.relative(Direction.values()[random.nextInt(Direction.values().length)]);
            } else {
                int dx, dy, dz; // a real diagonal step: at least two axes off
                do {
                    dx = random.nextInt(3) - 1;
                    dy = random.nextInt(3) - 1;
                    dz = random.nextInt(3) - 1;
                } while (Math.abs(dx) + Math.abs(dy) + Math.abs(dz) < 2);
                nb = from.offset(dx, dy, dz);
            }
            final long key = nb.asLong();
            if (coreSet.contains(key)) {
                blockMap.put(nb, ore);
                coreSet.remove(key);
                placed.add(nb);
            }
        }
    }

    private static void planDecoration(ServerLevel level, Map<BlockPos, BlockState> blockMap, List<TreeSite> trees,
                                       List<BlockPos> surfaceList, List<BlockPos> bottomList, Decoration deco,
                                       RandomSource random) {
        if (surfaceList.isEmpty()) {
            return;
        }
        final Registry<ConfiguredFeature<?, ?>> features =
                level.registryAccess().registryOrThrow(Registries.CONFIGURED_FEATURE);

        final List<BlockPos> treeBases = new ArrayList<>();
        for (TreeEntry tree : deco.trees()) {
            // skyseed:* "features" are built-in hand-built trees (vanilla features that won't place
            // dry, like mangroves); anything else is a vanilla configured feature placed afterwards.
            final boolean custom = tree.feature().getNamespace().equals(Skyseed.MODID);
            ConfiguredFeature<?, ?> feature = null;
            if (custom) {
                final String path = tree.feature().getPath();
                if (!path.equals("mangrove") && !path.equals("azalea") && !path.equals("ice_spike")) {
                    Skyseed.LOGGER.warn("[skyseed] unknown built-in tree '{}' — skipping", tree.feature());
                    continue;
                }
            } else {
                Optional<ConfiguredFeature<?, ?>> resolved = features.getOptional(tree.feature());
                if (resolved.isEmpty()) {
                    Skyseed.LOGGER.warn("[skyseed] theme references unknown feature '{}' — skipping", tree.feature());
                    continue;
                }
                feature = resolved.get();
            }
            final int spacingSq = tree.spacing() * tree.spacing();
            for (int i = 0; i < tree.tries(); i++) {
                final BlockPos base = surfaceList.get(random.nextInt(surfaceList.size()));
                boolean tooClose = false;
                for (BlockPos t : treeBases) {
                    if (t.distSqr(base) < spacingSq) {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) {
                    continue;
                }
                treeBases.add(base);
                if (custom) {
                    // hand-built into the streamed block list (vanilla features that won't place here)
                    final String path = tree.feature().getPath();
                    if (path.equals("azalea")) {
                        buildAzalea(blockMap, base, random);
                    } else if (path.equals("ice_spike")) {
                        buildIceSpike(blockMap, base, random);
                    } else {
                        buildMangrove(blockMap, base, random);
                    }
                } else {
                    trees.add(new TreeSite(feature, base.above()));
                }
            }
        }

        if (!deco.ground().isEmpty()) {
            for (BlockPos grass : surfaceList) {
                final BlockPos above = grass.above();
                if (blockMap.containsKey(above)) {
                    continue; // already a trunk/leaf/etc. from a hand-built tree
                }
                float roll = random.nextFloat();
                for (GroundEntry g : deco.ground()) {
                    roll -= g.chance();
                    if (roll < 0) {
                        if (BuiltInRegistries.BLOCK.containsKey(g.block())) {
                            placeGround(blockMap, above, BuiltInRegistries.BLOCK.get(g.block()));
                        }
                        break;
                    }
                }
            }
        }

        planUnderside(blockMap, bottomList, deco.underside(), random);
    }

    /** Place a ground plant, expanding two-tall plants (dripleaves, pitcher plant, tall flowers) into both halves. */
    private static void placeGround(Map<BlockPos, BlockState> blockMap, BlockPos above, Block block) {
        if (block instanceof DoublePlantBlock) {
            blockMap.put(above, block.defaultBlockState().setValue(DoublePlantBlock.HALF, DoubleBlockHalf.LOWER));
            blockMap.put(above.above(), block.defaultBlockState().setValue(DoublePlantBlock.HALF, DoubleBlockHalf.UPPER));
        } else {
            blockMap.put(above, block.defaultBlockState());
        }
    }

    /** Hang per-column features from the island's underside: dripstone, cave vines, spore blossoms, roots. */
    private static void planUnderside(Map<BlockPos, BlockState> blockMap, List<BlockPos> bottomList,
                                      List<GroundEntry> cfg, RandomSource random) {
        if (cfg.isEmpty() || bottomList.isEmpty()) {
            return;
        }
        for (BlockPos bottom : bottomList) {
            float roll = random.nextFloat();
            for (GroundEntry g : cfg) {
                roll -= g.chance();
                if (roll < 0) {
                    if (BuiltInRegistries.BLOCK.containsKey(g.block())) {
                        hangUnder(blockMap, bottom, g.block(), random);
                    }
                    break;
                }
            }
        }
    }

    /** Build a single hanging feature under {@code bottom} (a column's lowest block). */
    private static void hangUnder(Map<BlockPos, BlockState> blockMap, BlockPos bottom, ResourceLocation id, RandomSource random) {
        final BlockPos first = bottom.below();
        if (blockMap.containsKey(first)) {
            return;
        }
        switch (id.getPath()) {
            case "pointed_dripstone" -> {
                final int len = 1 + random.nextInt(3); // 1-3 tall stalactite
                for (int i = 0; i < len; i++) {
                    final DripstoneThickness th = (len == 1 || i == len - 1) ? DripstoneThickness.TIP
                            : (i == 0) ? (len >= 3 ? DripstoneThickness.BASE : DripstoneThickness.FRUSTUM)
                            : DripstoneThickness.MIDDLE;
                    blockMap.put(bottom.below(i + 1), Blocks.POINTED_DRIPSTONE.defaultBlockState()
                            .setValue(BlockStateProperties.VERTICAL_DIRECTION, Direction.DOWN)
                            .setValue(BlockStateProperties.DRIPSTONE_THICKNESS, th));
                }
            }
            case "cave_vines", "cave_vines_plant" -> {
                final int len = 1 + random.nextInt(4); // 1-4 trailing vine, berries lit
                for (int i = 0; i < len; i++) {
                    final boolean tip = i == len - 1;
                    blockMap.put(bottom.below(i + 1), (tip ? Blocks.CAVE_VINES : Blocks.CAVE_VINES_PLANT)
                            .defaultBlockState().setValue(BlockStateProperties.BERRIES, Boolean.TRUE));
                }
            }
            default -> blockMap.put(first, BuiltInRegistries.BLOCK.get(id).defaultBlockState());
        }
    }

    /** A hand-built mangrove (logs + roots + persistent leaves) added straight into the block plan. */
    private static void buildMangrove(Map<BlockPos, BlockState> blockMap, BlockPos ground, RandomSource random) {
        final BlockState log = Blocks.MANGROVE_LOG.defaultBlockState();
        final BlockState leaves = Blocks.MANGROVE_LEAVES.defaultBlockState().setValue(LeavesBlock.PERSISTENT, Boolean.TRUE);
        final BlockState roots = Blocks.MANGROVE_ROOTS.defaultBlockState();
        final BlockState muddy = Blocks.MUDDY_MANGROVE_ROOTS.defaultBlockState();
        final int gx = ground.getX(), gy = ground.getY(), gz = ground.getZ();
        final int trunk = 4 + random.nextInt(2); // 4-5 logs

        blockMap.put(new BlockPos(gx, gy, gz), roots);
        blockMap.put(new BlockPos(gx, gy - 1, gz), muddy);
        for (int[] d : new int[][]{ {1, 0}, {-1, 0}, {0, 1}, {0, -1} }) {
            if (random.nextInt(3) != 0) {
                blockMap.put(new BlockPos(gx + d[0], gy, gz + d[1]), roots);
            }
        }
        for (int i = 1; i <= trunk; i++) {
            blockMap.put(new BlockPos(gx, gy + i, gz), log);
        }
        leafBlob(blockMap, gx, gy + trunk - 1, gz, 2, leaves);
        leafBlob(blockMap, gx, gy + trunk, gz, 1, leaves);
        blockMap.put(new BlockPos(gx, gy + trunk + 1, gz), leaves);
        for (int[] d : new int[][]{ {1, 0}, {-1, 0}, {0, 1}, {0, -1} }) {
            blockMap.put(new BlockPos(gx + d[0], gy + trunk + 1, gz + d[1]), leaves);
        }
    }

    private static void leafBlob(Map<BlockPos, BlockState> blockMap, int gx, int y, int gz, int radius, BlockState leaves) {
        for (int dx = -radius; dx <= radius; dx++) {
            for (int dz = -radius; dz <= radius; dz++) {
                if (dx == 0 && dz == 0) {
                    continue; // trunk
                }
                if (Math.abs(dx) == radius && Math.abs(dz) == radius) {
                    continue; // trim corners
                }
                blockMap.putIfAbsent(new BlockPos(gx + dx, y, gz + dz), leaves);
            }
        }
    }

    /** A hand-built azalea tree (oak trunk + persistent azalea / flowering-azalea canopy). */
    private static void buildAzalea(Map<BlockPos, BlockState> blockMap, BlockPos ground, RandomSource random) {
        final BlockState log = Blocks.OAK_LOG.defaultBlockState();
        final BlockState leaves = Blocks.AZALEA_LEAVES.defaultBlockState().setValue(LeavesBlock.PERSISTENT, Boolean.TRUE);
        final BlockState flowering = Blocks.FLOWERING_AZALEA_LEAVES.defaultBlockState().setValue(LeavesBlock.PERSISTENT, Boolean.TRUE);
        final int gx = ground.getX(), gy = ground.getY(), gz = ground.getZ();
        final int trunk = 2 + random.nextInt(2); // 2-3 logs
        for (int i = 1; i <= trunk; i++) {
            blockMap.put(new BlockPos(gx, gy + i, gz), log);
        }
        final int topY = gy + trunk;
        azaleaLayer(blockMap, gx, topY, gz, 2, true, leaves, flowering, random);
        azaleaLayer(blockMap, gx, topY + 1, gz, 1, false, leaves, flowering, random);
        blockMap.putIfAbsent(new BlockPos(gx, topY + 2, gz), random.nextInt(3) == 0 ? flowering : leaves);
    }

    private static void azaleaLayer(Map<BlockPos, BlockState> blockMap, int gx, int y, int gz, int radius,
                                    boolean skipCenter, BlockState leaves, BlockState flowering, RandomSource random) {
        for (int dx = -radius; dx <= radius; dx++) {
            for (int dz = -radius; dz <= radius; dz++) {
                if (skipCenter && dx == 0 && dz == 0) {
                    continue; // trunk
                }
                if (Math.abs(dx) == radius && Math.abs(dz) == radius) {
                    continue; // trim corners
                }
                blockMap.putIfAbsent(new BlockPos(gx + dx, y, gz + dz), random.nextInt(4) == 0 ? flowering : leaves);
            }
        }
    }

    /** A hand-built ice spike — a packed-ice spire with a flared base (vanilla's feature won't place here). */
    private static void buildIceSpike(Map<BlockPos, BlockState> blockMap, BlockPos ground, RandomSource random) {
        final BlockState ice = Blocks.PACKED_ICE.defaultBlockState();
        final int gx = ground.getX(), gy = ground.getY(), gz = ground.getZ();
        final int h = 5 + random.nextInt(5); // 5-9 tall
        for (int i = 0; i < h; i++) {
            blockMap.put(new BlockPos(gx, gy + 1 + i, gz), ice);
            if (i < 2) { // flared base
                for (int[] d : new int[][]{ {1, 0}, {-1, 0}, {0, 1}, {0, -1} }) {
                    if (random.nextInt(3) != 0) {
                        blockMap.putIfAbsent(new BlockPos(gx + d[0], gy + 1 + i, gz + d[1]), ice);
                    }
                }
            }
        }
    }

    /** Short static cascades off the rim — a spring at the lip + a falling-water column down the side. */
    private static void placeWaterfalls(Map<BlockPos, BlockState> blockMap, List<BlockPos> surfaceList,
                                        BlockPos center, int baseRadius, int count, RandomSource random) {
        final BlockState source = Blocks.WATER.defaultBlockState();
        final BlockState falling = Blocks.WATER.defaultBlockState().setValue(LiquidBlock.LEVEL, 8);
        final int minEdgeSq = (int) ((baseRadius * 0.6) * (baseRadius * 0.6));
        final List<BlockPos> edges = new ArrayList<>();
        for (BlockPos p : surfaceList) {
            int dx = p.getX() - center.getX();
            int dz = p.getZ() - center.getZ();
            if (dx * dx + dz * dz >= minEdgeSq) {
                edges.add(p);
            }
        }
        final List<BlockPos> pick = edges.isEmpty() ? surfaceList : edges;
        for (int n = 0; n < count; n++) {
            final BlockPos c = pick.get(random.nextInt(pick.size()));
            int dx = c.getX() - center.getX();
            int dz = c.getZ() - center.getZ();
            int ox = 0;
            int oz = 0;
            if (Math.abs(dx) >= Math.abs(dz)) {
                ox = Integer.signum(dx);
            } else {
                oz = Integer.signum(dz);
            }
            if (ox == 0 && oz == 0) {
                ox = 1;
            }
            final int topY = c.getY();
            blockMap.put(new BlockPos(c.getX(), topY, c.getZ()), source); // spring at the lip
            for (int k = 0; k <= 6; k++) {
                blockMap.put(new BlockPos(c.getX() + ox, topY - k, c.getZ() + oz), falling); // cascade down the face
            }
        }
    }

    /**
     * Water surface for a pond: flush with the island's top at the pond's rim, not the un-domed base
     * ({@code center.getY()}). The top is domed up by {@code topDome} at the centre, so filling to the
     * base would leave the pool recessed several blocks below the surface; this lifts it to the surface
     * and only digs {@code depth} down from there.
     */
    private static int pondWaterY(BlockPos center, int topDome, int baseRadius, Pond pond) {
        final int r = Math.max(1, pond.radius());
        final double t = Math.min(1.0, (double) r / Math.max(1, baseRadius));
        final double bulge = Math.max(0.0, 1.0 - t * t);
        return center.getY() + (int) Math.round(topDome * bulge);
    }

    private static long colKey(int dx, int dz) {
        return (((long) dx) << 32) | (dz & 0xffffffffL);
    }

    /**
     * Carve a contained water feature. A {@code pond} is an irregular blob in the centre; a
     * {@code river} is a meandering channel across the island. Each candidate column is only carved
     * where the island body reaches below the pool floor, so the water can never hang off the rim
     * (the fix for islands losing a side). Returns the carved columns (packed dx,dz) so plants, banks
     * and water mobs match the exact carved shape.
     */
    private static Set<Long> carvePond(Map<BlockPos, BlockState> blockMap, List<BlockPos> surfaceList,
                                       BlockPos center, int topDome, int waterY, int baseRadius, Pond pond, RandomSource random) {
        final BlockState water = resolveBlock(pond.block(), Blocks.WATER).defaultBlockState();
        final int bottomY = waterY - Math.max(0, pond.depth() - 1);
        final int ceil = center.getY() + topDome + 1; // strip the dome cap above the water surface
        final Set<Long> carved = new HashSet<>();

        final List<int[]> candidates = pond.isRiver()
                ? riverColumns(pond, baseRadius, random)
                : pondColumns(pond, baseRadius, random);

        for (int[] c : candidates) {
            final int wx = center.getX() + c[0];
            final int wz = center.getZ() + c[1];
            // Containment: only carve where the island body sits below the floor, so water always rests
            // on solid ground (no carving past the rim, no floating slabs).
            if (!blockMap.containsKey(new BlockPos(wx, bottomY - 1, wz))) {
                continue;
            }
            for (int y = waterY + 1; y <= ceil; y++) {
                blockMap.remove(new BlockPos(wx, y, wz));
            }
            for (int y = bottomY; y <= waterY; y++) {
                blockMap.put(new BlockPos(wx, y, wz), water);
            }
            carved.add(colKey(c[0], c[1]));
        }
        surfaceList.removeIf(p -> carved.contains(colKey(p.getX() - center.getX(), p.getZ() - center.getZ())));
        return carved;
    }

    private static final int[][] NEIGHBORS_8 = {
            {1, 0}, {-1, 0}, {0, 1}, {0, -1}, {1, 1}, {1, -1}, {-1, 1}, {-1, -1}
    };

    /**
     * Contain a carved pool and dress it with water-side materials. Every land column touching the water has
     * any gap below the surface walled up to {@code waterY} (the "ring of dirt" that keeps the pool from
     * sheeting off the edge); the bed gets sand/clay/gravel, and the shore gets sandy/gravelly patches. Runs
     * before plants and banks so decorations sit on the finished, contained edge.
     */
    private static void containPond(Map<BlockPos, BlockState> blockMap, List<BlockPos> surfaceList, BlockPos center,
                                    int waterY, int bottomY, BlockState surface, BlockState fill, Set<Long> carved, RandomSource random) {
        // 1) Pond bed: the block the water rests on, seen through the surface.
        for (long k : carved) {
            final BlockPos floor = new BlockPos(center.getX() + (int) (k >> 32), bottomY - 1, center.getZ() + (int) k);
            if (blockMap.containsKey(floor)) {
                final BlockState bed = pondBed(random);
                if (bed != null) {
                    blockMap.put(floor, bed);
                }
            }
        }
        // 2) Containing ring + shore: every land column touching the water.
        final Set<Long> handled = new HashSet<>();
        final List<BlockPos> newRim = new ArrayList<>();
        for (long k : carved) {
            final int dx = (int) (k >> 32);
            final int dz = (int) k;
            for (int[] n : NEIGHBORS_8) {
                final int ndx = dx + n[0];
                final int ndz = dz + n[1];
                final long nk = colKey(ndx, ndz);
                if (carved.contains(nk) || !handled.add(nk)) {
                    continue;
                }
                final int nx = center.getX() + ndx;
                final int nz = center.getZ() + ndz;
                // The ring needs island body beneath the floor to rest on; with none, leave it open (a small
                // waterfall is acceptable variety) rather than hang a wall in the void.
                if (!blockMap.containsKey(new BlockPos(nx, bottomY - 1, nz))) {
                    continue;
                }
                final BlockPos top = new BlockPos(nx, waterY, nz);
                if (blockMap.containsKey(top)) {
                    // A flush/higher bank — already contains the water; just give the waterline a sandy touch.
                    final BlockState shore = pondShore(random, null);
                    if (shore != null) {
                        blockMap.put(top, shore);
                    }
                    continue;
                }
                // An open, lower edge: wall it up to the water surface so the pool can't spill here.
                for (int y = bottomY; y < waterY; y++) {
                    final BlockPos p = new BlockPos(nx, y, nz);
                    if (!blockMap.containsKey(p)) {
                        blockMap.put(p, fill);
                    }
                }
                blockMap.put(top, pondShore(random, surface));
                newRim.add(top);
            }
        }
        surfaceList.addAll(newRim); // decorations may grow on the new ring; stale lower entries are skipped (now buried)
    }

    /** Pond-bed material: mostly sand, some gravel and clay, otherwise the island's own block. */
    private static BlockState pondBed(RandomSource random) {
        final float x = random.nextFloat();
        if (x < 0.30f) return Blocks.SAND.defaultBlockState();
        if (x < 0.50f) return Blocks.GRAVEL.defaultBlockState();
        if (x < 0.62f) return Blocks.CLAY.defaultBlockState();
        return null; // keep the existing island body
    }

    /** Shore material at the waterline: sandy/gravelly patches, otherwise {@code fallback} (null = leave as-is). */
    private static BlockState pondShore(RandomSource random, BlockState fallback) {
        final float x = random.nextFloat();
        if (x < 0.22f) return Blocks.SAND.defaultBlockState();
        if (x < 0.35f) return Blocks.GRAVEL.defaultBlockState();
        return fallback;
    }

    /** Candidate columns for an irregular radial pond — a few angular harmonics give it a blobby, non-round edge. */
    private static List<int[]> pondColumns(Pond pond, int baseRadius, RandomSource random) {
        final List<int[]> out = new ArrayList<>();
        // Keep the pool well inside the island (extent ≈ 0.62·baseRadius after wobble) so its rim always has
        // solid ground for the containment ring to wall against — overshooting the rim is what made it overflow.
        final int r = Math.max(1, Math.min(pond.radius(), (int) Math.round(baseRadius * 0.5)));
        final int[] freq = { 2, 3, 5 };
        final double[] amp = new double[freq.length];
        final double[] phase = new double[freq.length];
        double sum = 0;
        for (int k = 0; k < freq.length; k++) { amp[k] = 0.3 + random.nextDouble(); sum += amp[k]; phase[k] = random.nextDouble() * Math.PI * 2; }
        for (int k = 0; k < freq.length; k++) { amp[k] = amp[k] / sum * 0.28; } // wobble the edge by up to ~28%
        final int maxR = (int) Math.ceil(r * 1.25) + 1;
        for (int dx = -maxR; dx <= maxR; dx++) {
            for (int dz = -maxR; dz <= maxR; dz++) {
                final double dist = Math.sqrt((double) dx * dx + (double) dz * dz);
                final double angle = Math.atan2(dz, dx);
                double rim = r;
                for (int k = 0; k < freq.length; k++) { rim += r * amp[k] * Math.sin(freq[k] * angle + phase[k]); }
                if (dist <= rim) { out.add(new int[]{dx, dz}); }
            }
        }
        return out;
    }

    /** Candidate columns for a meandering river channel cut across the island ({@code radius} = half-width). */
    private static List<int[]> riverColumns(Pond pond, int baseRadius, RandomSource random) {
        final List<int[]> out = new ArrayList<>();
        final double half = Math.max(1.0, pond.radius());
        final double phi = random.nextDouble() * Math.PI * 2;
        final double dirX = Math.cos(phi), dirZ = Math.sin(phi);
        final double perpX = -dirZ, perpZ = dirX;
        final double mAmp = half * 1.6;
        final double mFreq = 0.16 + random.nextDouble() * 0.12;
        final double mPhase = random.nextDouble() * Math.PI * 2;
        final int maxR = (int) Math.ceil(baseRadius * 1.5) + 2;
        for (int dx = -maxR; dx <= maxR; dx++) {
            for (int dz = -maxR; dz <= maxR; dz++) {
                final double along = dx * dirX + dz * dirZ;
                final double perp = dx * perpX + dz * perpZ;
                final double centerline = mAmp * Math.sin(along * mFreq + mPhase);
                if (Math.abs(perp - centerline) <= half) { out.add(new int[]{dx, dz}); }
            }
        }
        return out;
    }

    /** Scatter water plants through the carved water columns: lily pads on the surface, kelp/seagrass/coral on the floor. */
    private static void placePondPlants(Map<BlockPos, BlockState> blockMap, BlockPos center, int waterY, Pond pond, Set<Long> carved, RandomSource random) {
        if (pond.plants().isEmpty()) {
            return;
        }
        final int bottomY = waterY - Math.max(0, pond.depth() - 1);
        for (long k : carved) {
            final int wx = center.getX() + (int) (k >> 32);
            final int wz = center.getZ() + (int) k;
            float roll = random.nextFloat();
            for (GroundEntry g : pond.plants()) {
                roll -= g.chance();
                if (roll < 0) {
                    if (BuiltInRegistries.BLOCK.containsKey(g.block())) {
                        plantInPond(blockMap, wx, wz, waterY, bottomY, g.block());
                    }
                    break;
                }
            }
        }
    }

    /** Place one water plant in a pond column; type decides placement (surface lily vs floor-rooted vs coral). */
    private static void plantInPond(Map<BlockPos, BlockState> blockMap, int wx, int wz, int waterY, int bottomY, ResourceLocation id) {
        final BlockPos floor = new BlockPos(wx, bottomY, wz); // lowest water block, resting on the island body
        switch (id.getPath()) {
            case "lily_pad" -> blockMap.put(new BlockPos(wx, waterY + 1, wz), Blocks.LILY_PAD.defaultBlockState());
            case "kelp", "kelp_plant" -> {
                for (int y = bottomY; y <= waterY; y++) {
                    blockMap.put(new BlockPos(wx, y, wz), (y == waterY ? Blocks.KELP : Blocks.KELP_PLANT).defaultBlockState());
                }
            }
            case "tall_seagrass" -> {
                blockMap.put(floor, Blocks.TALL_SEAGRASS.defaultBlockState().setValue(DoublePlantBlock.HALF, DoubleBlockHalf.LOWER));
                if (bottomY + 1 <= waterY) {
                    blockMap.put(floor.above(), Blocks.TALL_SEAGRASS.defaultBlockState().setValue(DoublePlantBlock.HALF, DoubleBlockHalf.UPPER));
                }
            }
            default -> {
                BlockState st = BuiltInRegistries.BLOCK.get(id).defaultBlockState();
                if (st.hasProperty(BlockStateProperties.WATERLOGGED)) {
                    st = st.setValue(BlockStateProperties.WATERLOGGED, Boolean.TRUE); // coral fans, sea pickle, …
                }
                blockMap.put(floor, st);
            }
        }
    }

    /** Grow shore plants (e.g. sugar cane) on the ring of land right at the carved water's edge. */
    private static void placePondBanks(Map<BlockPos, BlockState> blockMap, List<BlockPos> surfaceList,
                                       BlockPos center, Pond pond, Set<Long> carved, RandomSource random) {
        if (pond.bank().isEmpty() || carved.isEmpty()) {
            return;
        }
        for (BlockPos col : surfaceList) { // carved columns were already removed from surfaceList
            final int dx = col.getX() - center.getX();
            final int dz = col.getZ() - center.getZ();
            // Only the immediate water's-edge — a 4-neighbour must be a carved water column — so the cane
            // stays put (sugar cane needs water beside it or it pops on the first update).
            final boolean waterAdjacent = carved.contains(colKey(dx + 1, dz)) || carved.contains(colKey(dx - 1, dz))
                    || carved.contains(colKey(dx, dz + 1)) || carved.contains(colKey(dx, dz - 1));
            if (!waterAdjacent) {
                continue;
            }
            final BlockPos above = col.above();
            if (blockMap.containsKey(above)) {
                continue;
            }
            float roll = random.nextFloat();
            for (GroundEntry g : pond.bank()) {
                roll -= g.chance();
                if (roll < 0) {
                    if (BuiltInRegistries.BLOCK.containsKey(g.block())) {
                        bankPlant(blockMap, above, g.block(), random);
                    }
                    break;
                }
            }
        }
    }

    /** Place a bank plant; sugar cane stacks 1-3 tall, everything else is a single block. */
    private static void bankPlant(Map<BlockPos, BlockState> blockMap, BlockPos above, ResourceLocation id, RandomSource random) {
        final BlockState state = BuiltInRegistries.BLOCK.get(id).defaultBlockState();
        if (id.getPath().equals("sugar_cane")) {
            final int h = 1 + random.nextInt(3); // 1-3 tall
            for (int i = 0; i < h; i++) {
                blockMap.put(above.above(i), state);
            }
        } else {
            blockMap.put(above, state);
        }
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
