package dev.gemberkoekje.skyseed.worldgen;

import net.minecraft.core.BlockPos;
import net.minecraft.util.Mth;
import net.minecraft.world.phys.Vec3;

/**
 * Tests whether a planned island can grow at its centre without interpenetrating existing blocks or burying a player.
 * It is a <em>block-level overlap</em> check on the island's own blocks (not a clearance volume), so two islands may
 * sit flush against each other — touching is fine, only real interpenetration is rejected. When it doesn't fit, the
 * result carries the centroid of the blocks that are in the way, so the caller can nudge the island <em>off</em> them
 * (horizontally first) rather than blindly upward.
 *
 * <p>Pure geometry: the caller supplies world occupancy and player positions, so this is unit-testable without a
 * world (see the gametest).
 */
public final class IslandPlacement {
    private IslandPlacement() {}

    /** A few overlapping blocks (touching rims, a stray leaf) are fine; more than this is a real collision. */
    private static final int GRAZE_TOLERANCE = 8;

    /** Tests whether a world position is solid enough to block an island (non-air, non-replaceable). */
    @FunctionalInterface
    public interface Occupancy {
        boolean solid(int x, int y, int z);
    }

    /** Result of a fit test: whether the island can grow as-is, and (if not) the centroid of what's in the way. */
    public record Fit(boolean ok, double blockedX, double blockedY, double blockedZ) {}

    /**
     * @return whether {@code plan} can grow as planned — no meaningful overlap with existing solids, and no block
     *         landing on a player's body. Adjacency/touching is allowed. On a failure the {@link Fit} carries the
     *         centroid of the blocked blocks for the caller to push away from.
     */
    public static Fit check(IslandPlan plan, Iterable<Vec3> players, Occupancy occupied) {
        int overlap = 0;
        int blocked = 0;
        long sx = 0, sy = 0, sz = 0;
        boolean buries = false;
        for (IslandPlan.BlockPlacement bp : plan.blocks()) {
            final BlockPos p = bp.pos();
            boolean inTheWay = false;
            if (occupied.solid(p.getX(), p.getY(), p.getZ())) {
                overlap++;
                inTheWay = true;
            }
            for (Vec3 player : players) {
                if (occupies(p, player)) {
                    buries = true;
                    inTheWay = true;
                }
            }
            if (inTheWay) {
                blocked++;
                sx += p.getX();
                sy += p.getY();
                sz += p.getZ();
            }
        }
        if (!buries && overlap <= GRAZE_TOLERANCE) {
            return new Fit(true, 0, 0, 0);
        }
        return new Fit(false, (double) sx / blocked, (double) sy / blocked, (double) sz / blocked);
    }

    /** True if island block {@code p} sits where a player's body (the feet or head block) is. */
    private static boolean occupies(BlockPos p, Vec3 player) {
        final int fx = Mth.floor(player.x);
        final int fy = Mth.floor(player.y);
        final int fz = Mth.floor(player.z);
        return p.getX() == fx && p.getZ() == fz && (p.getY() == fy || p.getY() == fy + 1);
    }
}
