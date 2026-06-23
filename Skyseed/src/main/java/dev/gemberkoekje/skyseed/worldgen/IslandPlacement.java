package dev.gemberkoekje.skyseed.worldgen;

import net.minecraft.core.BlockPos;
import net.minecraft.world.phys.Vec3;

import java.util.List;

/**
 * Decides whether a planned island can germinate at a spot without growing into an existing island or burying a
 * player. It is a <em>distance check</em> between island footprints, not a scan of world blocks: every island that
 * germinates is recorded (centre + half-extents) in {@link SkyseedWorldData}, and a new island is rejected if it
 * comes within a clearance of any recorded one.
 *
 * <p>The keep-out is an <em>asymmetric, oblate</em> ellipsoid — more room is kept side-to-side than top-to-bottom
 * (islands are far wider than tall, with a shallow dome above and a deep teardrop below), and the required gap is the
 * sum of <em>both</em> islands' radii plus a margin, so a big island reserves a bigger berth than a small one. That
 * fixes the old "reject if &gt;5% of my blocks overlap solid" rule, under which a large island could swallow a small
 * one (its blocks were under 5% of the big total) and which kept no gap at all.
 *
 * <p>Pure geometry: the caller supplies the recorded islands and the player positions, so this is unit-testable
 * without a world (see the gametest).
 */
public final class IslandPlacement {
    private IslandPlacement() {}

    /** Horizontal room kept clear between two islands (blocks). Wider than the vertical gap — islands are wide, not tall. */
    private static final int CLEARANCE_H = 8;
    /** Vertical room kept clear between two islands (blocks). */
    private static final int CLEARANCE_V = 3;
    /** Keep a player at least this far outside a new island's body, so a throw can't bury you. */
    private static final int PLAYER_CLEARANCE = 3;

    /** A placed island's footprint: centre, plus half-extents — wide {@code rh}, a shallow dome {@code up}, a deep teardrop {@code down}. */
    public record Island(int x, int y, int z, int rh, int up, int down) {}

    /** Measure an island's footprint from its plan (for the registry and the distance checks). */
    public static Island footprint(IslandPlan plan, BlockPos center) {
        int rx = 0, rz = 0, up = 1, down = 1;
        for (IslandPlan.BlockPlacement bp : plan.blocks()) {
            rx = Math.max(rx, Math.abs(bp.pos().getX() - center.getX()));
            rz = Math.max(rz, Math.abs(bp.pos().getZ() - center.getZ()));
            final int dy = bp.pos().getY() - center.getY();
            if (dy >= 0) {
                up = Math.max(up, dy);
            } else {
                down = Math.max(down, -dy);
            }
        }
        final int rh = Math.max(rx, rz);
        // A lone tall tree shouldn't make the keep-out taller than the island is wide.
        return new Island(center.getX(), center.getY(), center.getZ(), rh, Math.min(up, rh), down);
    }

    /** @return true if {@code candidate} can't grow — it would crowd an {@code existing} island, or bury a {@code player}. */
    public static boolean tooCrowded(Island candidate, List<Island> existing, Iterable<Vec3> players) {
        // Don't bury a player: reject if anyone is within the candidate's body plus a small margin.
        for (Vec3 p : players) {
            if (inside(p.x - candidate.x(), p.y - candidate.y(), p.z - candidate.z(),
                    candidate.rh() + PLAYER_CLEARANCE, candidate.up() + PLAYER_CLEARANCE, candidate.down() + PLAYER_CLEARANCE)) {
                return true;
            }
        }
        // Keep a gap from every recorded island: required separation is the sum of both radii plus a margin, larger
        // horizontally than vertically — an oval keep-out, not a sphere.
        for (Island e : existing) {
            final double dx = candidate.x() - e.x();
            final double dy = candidate.y() - e.y();
            final double dz = candidate.z() - e.z();
            final double rhSum = e.rh() + candidate.rh() + CLEARANCE_H;
            // Facing extents: if the candidate sits above e, e's dome meets the candidate's teardrop, and vice-versa.
            final double vSum = (dy >= 0 ? e.up() + candidate.down() : e.down() + candidate.up()) + CLEARANCE_V;
            if ((dx * dx + dz * dz) / (rhSum * rhSum) + (dy * dy) / (vSum * vSum) <= 1.0) {
                return true;
            }
        }
        return false;
    }

    /** Inside an asymmetric ellipsoid: horizontal radius {@code rh}, {@code up} above the centre, {@code down} below. */
    private static boolean inside(double dx, double dy, double dz, double rh, double up, double down) {
        final double v = dy >= 0 ? dy / up : dy / down;
        return (dx * dx + dz * dz) / (rh * rh) + v * v <= 1.0;
    }
}
