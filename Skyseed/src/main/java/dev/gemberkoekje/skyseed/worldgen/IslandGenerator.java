package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.Skyseed;
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
import net.minecraft.world.level.chunk.ChunkGenerator;
import net.minecraft.world.level.levelgen.feature.ConfiguredFeature;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Optional;
import java.util.Set;

/**
 * Places island blocks into the world. Deliberately a near-pure function of
 * {@code (ServerLevel, BlockPos center, IslandTheme, RandomSource)} — independent of items/entities so
 * it stays reusable (the curated start island can share it) and testable (plan §6).
 *
 * <p>Implements plan §5: irregular teardrop silhouette + layered fill, clustered ore veins, and
 * scattered decoration. All content comes from the {@link IslandTheme} (datapack, §4). Block/feature
 * ids resolve here so a missing modded id just warns and is skipped/falls back, never hard-fails.
 * Tick-budgeted placement is milestone 9.
 */
public final class IslandGenerator {
    private static final int BLOCK_FLAGS = Block.UPDATE_CLIENTS; // show blocks, no neighbour cascades

    private IslandGenerator() {}

    public static void generateIsland(ServerLevel level, BlockPos center, IslandTheme theme, RandomSource random) {
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
        final BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
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
                    if (y == surfaceY) {
                        level.setBlock(pos.set(wx, y, wz), surface, BLOCK_FLAGS);
                        surfaceList.add(new BlockPos(wx, y, wz));
                    } else if (y >= surfaceY - fillThickness) {
                        level.setBlock(pos.set(wx, y, wz), fill, BLOCK_FLAGS);
                    } else {
                        level.setBlock(pos.set(wx, y, wz), core, BLOCK_FLAGS);
                        coreList.add(new BlockPos(wx, y, wz));
                        minCoreY = Math.min(minCoreY, y);
                        maxCoreY = Math.max(maxCoreY, y);
                    }
                }
            }
        }

        // --- pass 2: ores ---
        if (!coreList.isEmpty()) {
            placeOres(level, theme.ores(), coreList, minCoreY, maxCoreY, random);
        }

        // --- pass 3: decoration ---
        if (variant != null) {
            decorate(level, surfaceList, variant.decoration(), random);
        }
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

    private static void placeOres(ServerLevel level, List<OreEntry> ores, List<BlockPos> coreList,
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
                    growVein(level, seed, state, ore.veinSize().sample(random), coreSet, random);
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

    private static void growVein(ServerLevel level, BlockPos seed, BlockState ore, int size,
                                 Set<Long> coreSet, RandomSource random) {
        final List<BlockPos> placed = new ArrayList<>();
        level.setBlock(seed, ore, BLOCK_FLAGS);
        coreSet.remove(seed.asLong());
        placed.add(seed);

        int attempts = 0;
        while (placed.size() < size && attempts < size * 8) {
            attempts++;
            final BlockPos from = placed.get(random.nextInt(placed.size()));
            final BlockPos nb = from.offset(random.nextInt(3) - 1, random.nextInt(3) - 1, random.nextInt(3) - 1);
            final long key = nb.asLong();
            if (coreSet.contains(key)) {
                level.setBlock(nb, ore, BLOCK_FLAGS);
                coreSet.remove(key);
                placed.add(nb);
            }
        }
    }

    private static void decorate(ServerLevel level, List<BlockPos> surfaceList, Decoration deco, RandomSource random) {
        if (surfaceList.isEmpty()) {
            return;
        }
        final ChunkGenerator generator = level.getChunkSource().getGenerator();
        final Registry<ConfiguredFeature<?, ?>> features =
                level.registryAccess().registryOrThrow(Registries.CONFIGURED_FEATURE);

        // Trees first, so trunks aren't blocked by ground cover.
        final List<BlockPos> placedTrees = new ArrayList<>();
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
                for (BlockPos t : placedTrees) {
                    if (t.distSqr(grass) < spacingSq) {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) {
                    continue;
                }
                if (feature.get().place(level, generator, random, grass.above())) {
                    placedTrees.add(grass);
                }
            }
        }

        // Ground cover: each open column rolls once; cumulative chances pick at most one plant.
        if (!deco.ground().isEmpty()) {
            for (BlockPos grass : surfaceList) {
                final BlockPos above = grass.above();
                if (!level.getBlockState(above).isAir()) {
                    continue;
                }
                float roll = random.nextFloat();
                for (GroundEntry g : deco.ground()) {
                    roll -= g.chance();
                    if (roll < 0) {
                        if (BuiltInRegistries.BLOCK.containsKey(g.block())) {
                            level.setBlock(above, BuiltInRegistries.BLOCK.get(g.block()).defaultBlockState(), BLOCK_FLAGS);
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
