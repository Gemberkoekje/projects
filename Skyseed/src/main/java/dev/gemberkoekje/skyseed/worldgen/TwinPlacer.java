package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.worldgen.theme.IslandTheme;
import net.minecraft.core.BlockPos;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.Mth;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.Vec3;

import java.util.ArrayList;
import java.util.List;

/**
 * Grows a cross-dimension twin island at the vanilla 8:1 portal-linked coordinate, so a Ruined Portal seed pairs an
 * overworld frame with a Nether one that links with no extra code (SKYNETHERPLAN → Ruined Portal twins). Pulled out of
 * {@link dev.gemberkoekje.skyseed.entity.IslandSeedEntity} so the thrown seed keeps a single responsibility.
 */
public final class TwinPlacer {
    private TwinPlacer() {}

    /**
     * Grow a twin island at the 8:1 dimension-linked coordinate in the paired dimension (overworld &harr; nether), so a
     * repaired-and-lit frame on both sides connects with no linking code. The twin germinates the same theme — the
     * dimension itself decides the form (goodies in the overworld, a bare frame in the Nether). Placement stays close to
     * the linked spot (small steps only) so the portals still link.
     */
    public static void spawnTwin(ServerLevel origin, BlockPos center, IslandTheme theme) {
        final ResourceKey<Level> to;
        if (origin.dimension() == Level.OVERWORLD) {
            to = Level.NETHER;
        } else if (origin.dimension() == Level.NETHER) {
            to = Level.OVERWORLD;
        } else {
            return; // twins only pair the overworld and the Nether
        }
        final ServerLevel other = origin.getServer().getLevel(to);
        if (other == null) {
            return;
        }
        final BlockPos linked = linkedPortalPos(center, to, other);
        if (!IslandGenerator.formValidFor(theme, other.getBiome(linked), linked.getY(), other.dimension().location().toString())) {
            return; // the theme doesn't implement the other dimension — no twin
        }
        final IslandPlan twin = placeTwinNear(other, theme, linked);
        if (twin != null) {
            IslandGrowth.enqueue(new GenerationJob(other, twin));
        }
    }

    /** The vanilla 8:1 cross-dimension coordinate (overworld/8 &harr; nether*8), Y kept and clamped to {@code to}. */
    public static BlockPos linkedPortalPos(BlockPos c, ResourceKey<Level> to, ServerLevel toLevel) {
        final int x;
        final int z;
        if (to == Level.NETHER) {
            x = Math.floorDiv(c.getX(), 8);
            z = Math.floorDiv(c.getZ(), 8);
        } else {
            x = c.getX() * 8;
            z = c.getZ() * 8;
        }
        int y = c.getY();
        if (to == Level.NETHER) {
            y = Mth.clamp(y, 16, 110); // above the lava sea, below the ceiling
        } else {
            y = Mth.clamp(y, toLevel.getMinBuildHeight() + 8, toLevel.getMaxBuildHeight() - 16);
        }
        return new BlockPos(x, y, z);
    }

    /** Plan the twin as close to {@code linked} as possible — small steps only, so the portal stays in linking range. */
    private static IslandPlan placeTwinNear(ServerLevel level, IslandTheme theme, BlockPos linked) {
        final List<Vec3> players = level.players().stream().map(p -> p.position()).toList();
        final BlockPos.MutableBlockPos probe = new BlockPos.MutableBlockPos();
        final IslandPlacement.Occupancy occupied = (x, y, z) -> {
            final BlockState s = level.getBlockState(probe.set(x, y, z));
            return !s.isAir() && !s.canBeReplaced();
        };
        for (BlockPos c : twinSearchSpots(linked)) {
            final IslandPlan candidate = planTwinAt(level, theme, c);
            if (IslandPlacement.check(candidate, players, occupied).ok()) {
                return candidate;
            }
        }
        // No clear spot close by — grow it at the linked coordinate anyway; sitting on the link is the whole point.
        return planTwinAt(level, theme, linked);
    }

    /** The linked spot first, then a tight ring (small horizontal steps), then a couple of small vertical lifts. */
    private static List<BlockPos> twinSearchSpots(BlockPos linked) {
        final List<BlockPos> spots = new ArrayList<>();
        spots.add(linked);
        for (int d = 3; d <= 9; d += 3) {
            spots.add(linked.offset(d, 0, 0));
            spots.add(linked.offset(-d, 0, 0));
            spots.add(linked.offset(0, 0, d));
            spots.add(linked.offset(0, 0, -d));
            spots.add(linked.offset(d, 0, d));
            spots.add(linked.offset(-d, 0, -d));
        }
        for (int lift : new int[] { 6, -6, 12, -12 }) {
            spots.add(linked.above(lift));
        }
        return spots;
    }

    /** Plan the twin at {@code c} with the linked dimension's own biome, RNG keyed by the centre (deterministic). */
    private static IslandPlan planTwinAt(ServerLevel level, IslandTheme theme, BlockPos c) {
        final RandomSource random = RandomSource.create(level.getSeed() ^ c.asLong());
        return IslandGenerator.planIsland(level, c, theme, level.getBiome(c), random);
    }
}
