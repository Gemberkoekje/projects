package dev.gemberkoekje.skyseed.worldgen;

import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.Registries;
import net.minecraft.data.worldgen.features.TreeFeatures;
import net.minecraft.resources.ResourceKey;
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
import java.util.Set;

/**
 * Places island blocks into the world. Deliberately a near-pure function of
 * {@code (ServerLevel, BlockPos center, RandomSource)} — independent of items/entities so it stays
 * reusable (the curated start island can share it) and testable (plan §6).
 *
 * <p>Implements plan §5: irregular teardrop silhouette + layered fill (steps 1–2), clustered ore
 * veins (step 3), and scattered decoration (step 4). Palette, ore table, and decoration are
 * hardcoded to a forest-ish theme for now; they become theme JSON in milestone 6. Tick-budgeted
 * placement is milestone 9.
 */
public final class IslandGenerator {
    private static final int BLOCK_FLAGS = Block.UPDATE_CLIENTS; // show blocks, no neighbour cascades

    private IslandGenerator() {}

    public static void generateIsland(ServerLevel level, BlockPos center, RandomSource random) {
        // --- shape parameters (theme params, later from JSON) ---
        final int rMin = 7, rMax = 10;
        final int baseRadius = rMin + random.nextInt(rMax - rMin + 1);
        final double rimNoise = 0.40;
        final double maxDepth = baseRadius * 1.05;
        final int topDome = 1 + random.nextInt(2);
        final int baseFill = 3;

        // --- palette ---
        final BlockState surface = Blocks.GRASS_BLOCK.defaultBlockState();
        final BlockState fill = Blocks.DIRT.defaultBlockState();
        final BlockState core = Blocks.STONE.defaultBlockState();

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
        final List<BlockPos> coreList = new ArrayList<>();   // stone positions, for ore seeding
        final List<BlockPos> surfaceList = new ArrayList<>(); // grass tops, for decoration

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
                final int fillThickness = baseFill + random.nextInt(3) - 1; // 2..4

                for (int y = bottomY; y <= surfaceY; y++) {
                    if (y == surfaceY) {
                        level.setBlock(pos.set(wx, y, wz), surface, BLOCK_FLAGS);
                        surfaceList.add(new BlockPos(wx, y, wz));
                    } else if (y >= surfaceY - fillThickness) {
                        level.setBlock(pos.set(wx, y, wz), fill, BLOCK_FLAGS);
                    } else {
                        level.setBlock(pos.set(wx, y, wz), core, BLOCK_FLAGS);
                        coreList.add(new BlockPos(wx, y, wz));
                    }
                }
            }
        }

        // --- pass 2: ores (clustered veins in the core) ---
        if (!coreList.isEmpty()) {
            final Set<Long> coreSet = new HashSet<>(coreList.size() * 2);
            for (BlockPos p : coreList) {
                coreSet.add(p.asLong());
            }
            placeOre(level, coreList, coreSet, random, Blocks.COAL_ORE, 0.85f, 2, 4, 3, 6);
            placeOre(level, coreList, coreSet, random, Blocks.IRON_ORE, 0.50f, 1, 3, 2, 4);
        }

        // --- pass 3: decoration (trees, then ground cover) ---
        decorate(level, surfaceList, random);
    }

    /** Rolls presence (chance), then scatters {@code count} small clustered veins of the ore. */
    private static void placeOre(ServerLevel level, List<BlockPos> coreList, Set<Long> coreSet,
                                 RandomSource random, Block ore, float chance,
                                 int countMin, int countMax, int sizeMin, int sizeMax) {
        if (random.nextFloat() >= chance) {
            return;
        }
        final BlockState state = ore.defaultBlockState();
        final int veins = countMin + random.nextInt(countMax - countMin + 1);
        for (int v = 0; v < veins; v++) {
            BlockPos seed = null;
            for (int tries = 0; tries < 12; tries++) {
                BlockPos c = coreList.get(random.nextInt(coreList.size()));
                if (coreSet.contains(c.asLong())) {
                    seed = c;
                    break;
                }
            }
            if (seed == null) {
                continue;
            }
            growVein(level, seed, state, sizeMin + random.nextInt(sizeMax - sizeMin + 1), coreSet, random);
        }
    }

    /** Grows a blobby vein by converting adjacent still-stone core blocks. */
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

    /** A few trees (configured features) plus scattered grass and flowers on the surface. */
    private static void decorate(ServerLevel level, List<BlockPos> surfaceList, RandomSource random) {
        if (surfaceList.isEmpty()) {
            return;
        }
        final ChunkGenerator generator = level.getChunkSource().getGenerator();

        // Trees first, so trunks aren't blocked by ground cover.
        final int treeCount = 2 + random.nextInt(4); // 2..5
        final List<BlockPos> treeBases = new ArrayList<>();
        for (int i = 0; i < treeCount; i++) {
            final BlockPos grass = surfaceList.get(random.nextInt(surfaceList.size()));
            boolean tooClose = false;
            for (BlockPos t : treeBases) {
                if (t.distSqr(grass) < 9) { // keep trees ~3 blocks apart
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) {
                continue;
            }
            if (placeFeature(level, generator, random, grass.above(), TreeFeatures.OAK)) {
                treeBases.add(grass);
            }
        }

        // Ground cover: short grass and the odd flower on still-grassy, open columns.
        final BlockState shortGrass = Blocks.SHORT_GRASS.defaultBlockState();
        final BlockState poppy = Blocks.POPPY.defaultBlockState();
        final BlockState dandelion = Blocks.DANDELION.defaultBlockState();
        for (BlockPos grass : surfaceList) {
            final BlockPos above = grass.above();
            if (!level.getBlockState(grass).is(Blocks.GRASS_BLOCK) || !level.getBlockState(above).isAir()) {
                continue;
            }
            final float roll = random.nextFloat();
            if (roll < 0.05f) {
                level.setBlock(above, random.nextBoolean() ? poppy : dandelion, BLOCK_FLAGS);
            } else if (roll < 0.35f) {
                level.setBlock(above, shortGrass, BLOCK_FLAGS);
            }
        }
    }

    private static boolean placeFeature(ServerLevel level, ChunkGenerator generator, RandomSource random,
                                        BlockPos pos, ResourceKey<ConfiguredFeature<?, ?>> key) {
        final ConfiguredFeature<?, ?> feature =
                level.registryAccess().registryOrThrow(Registries.CONFIGURED_FEATURE).getOrThrow(key);
        return feature.place(level, generator, random, pos);
    }
}
