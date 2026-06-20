package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan.BlockPlacement;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan.TreeSite;
import dev.gemberkoekje.skyseed.worldgen.theme.Decoration;
import dev.gemberkoekje.skyseed.worldgen.theme.GroundEntry;
import dev.gemberkoekje.skyseed.worldgen.theme.IslandTheme;
import dev.gemberkoekje.skyseed.worldgen.theme.OreDepth;
import dev.gemberkoekje.skyseed.worldgen.theme.OreEntry;
import dev.gemberkoekje.skyseed.worldgen.theme.Palette;
import dev.gemberkoekje.skyseed.worldgen.theme.Shape;
import dev.gemberkoekje.skyseed.worldgen.theme.TreeEntry;
import dev.gemberkoekje.skyseed.worldgen.theme.Variant;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
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
 * Computes an island (plan §5: irregular teardrop silhouette + layered fill, clustered ore veins,
 * scattered decoration) from an {@link IslandTheme}. Deliberately a near-pure function that does NOT
 * write the world — it returns an {@link IslandPlan} of block placements + tree sites, which a
 * {@link GenerationJob} then drains over several ticks (tick-budget guard, §5). Block/feature ids
 * resolve here so a missing modded id just warns and is skipped/falls back, never hard-fails (§4).
 */
public final class IslandGenerator {
    private IslandGenerator() {}

    public static IslandPlan planIsland(ServerLevel level, BlockPos center, IslandTheme theme, RandomSource random) {
        final Shape shape = theme.shape();
        final Palette palette = theme.palette();
        final Variant variant = pickVariant(theme.variants(), random);

        // --- resolve palette (graceful fallback to vanilla defaults) ---
        ResourceLocation surfaceId = palette.surface();
        if (variant != null && variant.surfaceOverride().isPresent()) {
            surfaceId = variant.surfaceOverride().get();
        }
        final BlockState surface = resolveBlock(surfaceId, Blocks.GRASS_BLOCK).defaultBlockState();
        final BlockState fill = resolveBlock(palette.fill(), Blocks.DIRT).defaultBlockState();
        final BlockState core = resolveBlock(palette.core(), Blocks.STONE).defaultBlockState();

        // --- shape parameters ---
        final int baseRadius = Math.max(1, shape.radius().sample(random));
        final double rimNoise = shape.rimNoise();
        final int topDome = shape.topDome().sample(random);
        final int baseFill = palette.fillDepth();
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
        // insertion-ordered so ore overwrites keep their slot; one entry per position (final state)
        final Map<BlockPos, BlockState> blockMap = new LinkedHashMap<>();
        final List<BlockPos> coreList = new ArrayList<>();
        final List<BlockPos> surfaceList = new ArrayList<>();
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
                        blockMap.put(p, surface);
                        surfaceList.add(p);
                    } else if (y >= surfaceY - fillThickness) {
                        blockMap.put(p, fill);
                    } else {
                        blockMap.put(p, core);
                        coreList.add(p);
                        minCoreY = Math.min(minCoreY, y);
                        maxCoreY = Math.max(maxCoreY, y);
                    }
                }
            }
        }

        // --- pass 2: ores (clustered veins, overwriting core entries) ---
        if (!coreList.isEmpty()) {
            planOres(blockMap, theme.ores(), coreList, minCoreY, maxCoreY, random);
        }

        // --- pass 3: decoration (tree sites + ground cover) ---
        final List<TreeSite> trees = new ArrayList<>();
        if (variant != null) {
            planDecoration(level, blockMap, trees, surfaceList, variant.decoration(), random);
        }

        // --- assemble: bottom-up order so the island appears to grow upward ---
        final List<BlockPlacement> blocks = new ArrayList<>(blockMap.size());
        for (Map.Entry<BlockPos, BlockState> e : blockMap.entrySet()) {
            blocks.add(new BlockPlacement(e.getKey(), e.getValue()));
        }
        blocks.sort(Comparator.comparingInt(bp -> bp.pos().getY()));

        return new IslandPlan(blocks, trees, random);
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
        while (placed.size() < size && attempts < size * 8) {
            attempts++;
            final BlockPos from = placed.get(random.nextInt(placed.size()));
            final BlockPos nb = from.offset(random.nextInt(3) - 1, random.nextInt(3) - 1, random.nextInt(3) - 1);
            final long key = nb.asLong();
            if (coreSet.contains(key)) {
                blockMap.put(nb, ore);
                coreSet.remove(key);
                placed.add(nb);
            }
        }
    }

    private static void planDecoration(ServerLevel level, Map<BlockPos, BlockState> blockMap, List<TreeSite> trees,
                                       List<BlockPos> surfaceList, Decoration deco, RandomSource random) {
        if (surfaceList.isEmpty()) {
            return;
        }
        final Registry<ConfiguredFeature<?, ?>> features =
                level.registryAccess().registryOrThrow(Registries.CONFIGURED_FEATURE);

        // Tree sites: count-based, spaced apart. Placed (as features) after the solid blocks land.
        final List<BlockPos> treeBases = new ArrayList<>();
        for (TreeEntry tree : deco.trees()) {
            final Optional<ConfiguredFeature<?, ?>> feature = features.getOptional(tree.feature());
            if (feature.isEmpty()) {
                Skyseed.LOGGER.warn("[skyseed] theme references unknown feature '{}' — skipping", tree.feature());
                continue;
            }
            final int spacingSq = tree.spacing() * tree.spacing();
            for (int i = 0; i < tree.tries(); i++) {
                final BlockPos grass = surfaceList.get(random.nextInt(surfaceList.size()));
                boolean tooClose = false;
                for (BlockPos t : treeBases) {
                    if (t.distSqr(grass) < spacingSq) {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) {
                    continue;
                }
                treeBases.add(grass);
                trees.add(new TreeSite(feature.get(), grass.above()));
            }
        }

        // Ground cover: each column rolls once; cumulative chances pick at most one plant (placed on top).
        if (!deco.ground().isEmpty()) {
            for (BlockPos grass : surfaceList) {
                float roll = random.nextFloat();
                for (GroundEntry g : deco.ground()) {
                    roll -= g.chance();
                    if (roll < 0) {
                        if (BuiltInRegistries.BLOCK.containsKey(g.block())) {
                            blockMap.put(grass.above(), BuiltInRegistries.BLOCK.get(g.block()).defaultBlockState());
                        }
                        break;
                    }
                }
            }
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
