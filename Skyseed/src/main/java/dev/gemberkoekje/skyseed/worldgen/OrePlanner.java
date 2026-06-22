package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.worldgen.theme.OreDepth;
import dev.gemberkoekje.skyseed.worldgen.theme.OreEntry;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.state.BlockState;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Plants ore veins through an island's core columns (README → ore algorithm): each {@link OreEntry} rolls a number
 * of veins, each grown by a short random walk from a depth-appropriate seed block, replacing core blocks in place.
 */
final class OrePlanner {
    private OrePlanner() {}

    static void planOres(Map<BlockPos, BlockState> blockMap, List<OreEntry> ores, List<BlockPos> coreList,
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
}
