package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan.BlockPlacement;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan.TreeSite;
import dev.gemberkoekje.skyseed.worldgen.theme.BiomeOverride;
import dev.gemberkoekje.skyseed.worldgen.theme.Decoration;
import dev.gemberkoekje.skyseed.worldgen.theme.GroundEntry;
import dev.gemberkoekje.skyseed.worldgen.theme.IslandTheme;
import dev.gemberkoekje.skyseed.worldgen.theme.OreDepth;
import dev.gemberkoekje.skyseed.worldgen.theme.OreEntry;
import dev.gemberkoekje.skyseed.worldgen.theme.Palette;
import dev.gemberkoekje.skyseed.worldgen.theme.Pond;
import dev.gemberkoekje.skyseed.worldgen.theme.Shape;
import dev.gemberkoekje.skyseed.worldgen.theme.TreeEntry;
import dev.gemberkoekje.skyseed.worldgen.theme.Variant;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.biome.Biome;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.LeavesBlock;
import net.minecraft.world.level.block.LiquidBlock;
import net.minecraft.world.level.block.state.BlockState;
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
 * Computes an island (plan §5) from an {@link IslandTheme}, with the theme's base config overlaid by
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

        if (!coreList.isEmpty()) {
            planOres(blockMap, ores, coreList, minCoreY, maxCoreY, random);
        }

        // Pond: carve a contained pool into the top centre (placed before trees so mangroves see water).
        final Optional<Pond> pondCfg = (ov != null && ov.pond().isPresent()) ? ov.pond() : theme.pond();
        pondCfg.ifPresent(pond -> carvePond(blockMap, surfaceList, center, topDome, pond));

        final List<TreeSite> trees = new ArrayList<>();
        if (variant != null) {
            planDecoration(level, blockMap, trees, surfaceList, variant.decoration(), random);
        }

        // Waterfalls: short static cascades off the rim (block placements, no flow physics).
        final int waterfalls = (ov != null && ov.waterfalls().isPresent()) ? ov.waterfalls().get() : 0;
        if (waterfalls > 0) {
            placeWaterfalls(blockMap, surfaceList, center, baseRadius, waterfalls, random);
        }

        final List<BlockPlacement> blocks = new ArrayList<>(blockMap.size());
        for (Map.Entry<BlockPos, BlockState> e : blockMap.entrySet()) {
            blocks.add(new BlockPlacement(e.getKey(), e.getValue()));
        }
        blocks.sort(Comparator.comparingInt(bp -> bp.pos().getY())); // bottom-up grow-in

        return new IslandPlan(blocks, trees, random);
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

        final List<BlockPos> treeBases = new ArrayList<>();
        for (TreeEntry tree : deco.trees()) {
            // skyseed:* "features" are built-in hand-built trees (vanilla features that won't place
            // dry, like mangroves); anything else is a vanilla configured feature placed afterwards.
            final boolean custom = tree.feature().getNamespace().equals(Skyseed.MODID);
            ConfiguredFeature<?, ?> feature = null;
            if (custom) {
                if (!tree.feature().getPath().equals("mangrove")) {
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
                    buildMangrove(blockMap, base, random); // hand-built into the streamed block list
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
                            blockMap.put(above, BuiltInRegistries.BLOCK.get(g.block()).defaultBlockState());
                        }
                        break;
                    }
                }
            }
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

    /** Carve a contained pool into the island's top centre and keep decoration off those columns. */
    private static void carvePond(Map<BlockPos, BlockState> blockMap, List<BlockPos> surfaceList,
                                  BlockPos center, int topDome, Pond pond) {
        final BlockState water = resolveBlock(pond.block(), Blocks.WATER).defaultBlockState();
        final int r = Math.max(1, pond.radius());
        final int r2 = r * r;
        final int waterY = center.getY();
        final int bottomY = waterY - Math.max(0, pond.depth() - 1);
        for (int dx = -r; dx <= r; dx++) {
            for (int dz = -r; dz <= r; dz++) {
                if (dx * dx + dz * dz > r2) {
                    continue;
                }
                final int wx = center.getX() + dx;
                final int wz = center.getZ() + dz;
                // open the dome above the water surface
                for (int y = waterY + 1; y <= center.getY() + topDome + 1; y++) {
                    blockMap.remove(new BlockPos(wx, y, wz));
                }
                // fill the pool; blocks below the floor stay as the island body
                for (int y = bottomY; y <= waterY; y++) {
                    blockMap.put(new BlockPos(wx, y, wz), water);
                }
            }
        }
        surfaceList.removeIf(p -> {
            int dx = p.getX() - center.getX();
            int dz = p.getZ() - center.getZ();
            return dx * dx + dz * dz <= r2;
        });
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
