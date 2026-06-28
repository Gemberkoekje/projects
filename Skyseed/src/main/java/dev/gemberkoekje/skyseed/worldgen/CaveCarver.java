package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.compat.Id;
import dev.gemberkoekje.skyseed.compat.Lookup;
import dev.gemberkoekje.skyseed.worldgen.theme.Caves;
import dev.gemberkoekje.skyseed.worldgen.theme.GroundEntry;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.util.Mth;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;
import net.minecraft.world.level.block.state.properties.DripstoneThickness;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Carves an internal cave system into a (huge) island's body — SKYHUGEPLAN Phase 2. A ring of ellipsoid chambers in
 * the outer interior (clear of the centre, where ponds/structures sit), linked by tunnels, all clipped so a solid skin
 * always remains below the surface and above the underside — the island never opens to the void. Cave surfaces are
 * dressed from the variant's underside palette (stalactites, cave vines, glow lichen) plus floor stalagmites, and one
 * of three reachability styles is rolled: <b>hidden</b> (mine in), a <b>sinkhole</b> shaft, or a <b>gash</b> ravine.
 */
final class CaveCarver {
    private CaveCarver() {}

    private static final int SURFACE_MARGIN = 4; // solid blocks kept between a cave and the surface
    private static final int UNDER_MARGIN = 3;   // solid blocks kept between a cave and the underside
    private static final int TUNNEL_R = 2;

    /** Default cave dressing for themes whose variant has no underside palette: a little dripstone + glow lichen. */
    private static final List<GroundEntry> DEFAULT_DECO = List.of(
            new GroundEntry(Id.of("minecraft:pointed_dripstone"), 0.12f),
            new GroundEntry(Id.of("minecraft:glow_lichen"), 0.07f));

    private static long key(int dx, int dz) {
        return (((long) dx) << 32) | (dz & 0xffffffffL);
    }

    static void carve(Map<BlockPos, BlockState> blockMap, List<BlockPos> surfaceList, List<BlockPos> bottomList,
                      BlockPos center, int baseRadius, Caves caves, List<GroundEntry> underside, RandomSource random) {
        // Per-column surface (top) and underside (bottom) Y, so carving keeps its margins from both skins.
        final Map<Long, Integer> topY = new HashMap<>();
        final Map<Long, Integer> botY = new HashMap<>();
        for (BlockPos p : surfaceList) topY.merge(key(p.getX() - center.getX(), p.getZ() - center.getZ()), p.getY(), Math::max);
        for (BlockPos p : bottomList) botY.merge(key(p.getX() - center.getX(), p.getZ() - center.getZ()), p.getY(), Math::min);

        final Set<BlockPos> carved = new HashSet<>();
        final List<BlockPos> rooms = new ArrayList<>();
        final int count = Math.max(1, caves.rooms().sample(random));
        final double innerR = baseRadius * 0.40, outerR = baseRadius * 0.70; // a ring, clear of the centre + the rim
        int attempts = 0;
        while (rooms.size() < count && attempts++ < count * 6) {
            final double ang = random.nextDouble() * Math.PI * 2;
            final double dist = innerR + random.nextDouble() * Math.max(1.0, outerR - innerR);
            final int dx = (int) Math.round(Math.cos(ang) * dist);
            final int dz = (int) Math.round(Math.sin(ang) * dist);
            final Integer t = topY.get(key(dx, dz));
            final Integer b = botY.get(key(dx, dz));
            if (t == null || b == null) continue;
            final int lo = b + UNDER_MARGIN, hi = t - SURFACE_MARGIN;
            if (hi - lo < 4) continue; // body too thin here to hold a chamber
            final int y = lo + random.nextInt(hi - lo + 1);
            final BlockPos rc = new BlockPos(center.getX() + dx, y, center.getZ() + dz);
            carveSphere(blockMap, topY, botY, center, rc, caves.size().sample(random), carved);
            rooms.add(rc);
        }
        for (int i = 1; i < rooms.size(); i++) {
            carveTunnel(blockMap, topY, botY, center, rooms.get(i - 1), rooms.get(i), carved);
        }
        if (carved.isEmpty()) {
            return;
        }
        decorate(blockMap, carved, underside.isEmpty() ? DEFAULT_DECO : underside, random);
        breach(blockMap, surfaceList, topY, center, rooms, random);
    }

    /** Hollow a sphere of radius {@code r} around {@code c}, clipped to interior cells (margins from both skins). */
    private static void carveSphere(Map<BlockPos, BlockState> blockMap, Map<Long, Integer> topY, Map<Long, Integer> botY,
                                    BlockPos center, BlockPos c, int r, Set<BlockPos> carved) {
        final int rr = r * r;
        for (int dx = -r; dx <= r; dx++) {
            for (int dy = -r; dy <= r; dy++) {
                for (int dz = -r; dz <= r; dz++) {
                    if (dx * dx + dy * dy + dz * dz > rr) continue;
                    final BlockPos p = c.offset(dx, dy, dz);
                    if (canCarve(blockMap, topY, botY, center, p)) {
                        blockMap.remove(p);
                        carved.add(p);
                    }
                }
            }
        }
    }

    /** True if {@code p} is solid island body with a full margin below the surface and above the underside. */
    private static boolean canCarve(Map<BlockPos, BlockState> blockMap, Map<Long, Integer> topY, Map<Long, Integer> botY,
                                    BlockPos center, BlockPos p) {
        if (!blockMap.containsKey(p)) return false;
        final long k = key(p.getX() - center.getX(), p.getZ() - center.getZ());
        final Integer t = topY.get(k);
        final Integer b = botY.get(k);
        return t != null && b != null && p.getY() <= t - SURFACE_MARGIN && p.getY() >= b + UNDER_MARGIN;
    }

    private static void carveTunnel(Map<BlockPos, BlockState> blockMap, Map<Long, Integer> topY, Map<Long, Integer> botY,
                                    BlockPos center, BlockPos a, BlockPos b, Set<BlockPos> carved) {
        final int steps = Math.max(1, (int) Math.ceil(Math.sqrt(a.distSqr(b))));
        for (int s = 0; s <= steps; s++) {
            final double t = (double) s / steps;
            final BlockPos p = new BlockPos((int) Math.round(Mth.lerp(t, a.getX(), b.getX())),
                    (int) Math.round(Mth.lerp(t, a.getY(), b.getY())),
                    (int) Math.round(Mth.lerp(t, a.getZ(), b.getZ())));
            carveSphere(blockMap, topY, botY, center, p, TUNNEL_R, carved);
        }
    }

    /** Dress the carved surfaces: hang a feature from each ceiling cell (palette), and stand the odd floor stalagmite. */
    private static void decorate(Map<BlockPos, BlockState> blockMap, Set<BlockPos> carved, List<GroundEntry> palette,
                                 RandomSource random) {
        for (BlockPos p : carved) {
            if (blockMap.containsKey(p.above())) { // ceiling: solid above — hang a feature down into the cave
                float roll = random.nextFloat();
                for (GroundEntry g : palette) {
                    roll -= g.chance();
                    if (roll < 0) {
                        if (Lookup.hasBlock(g.block())) {
                            DecorationPlanner.hangUnder(blockMap, p.above(), g.block(), random);
                        }
                        break;
                    }
                }
            } else if (blockMap.containsKey(p.below()) && random.nextFloat() < 0.10f) { // floor: a stalagmite
                stalagmite(blockMap, p, random);
            }
        }
    }

    /** A 1-2 tall pointed-dripstone stalagmite standing up from a cave floor cell. */
    private static void stalagmite(Map<BlockPos, BlockState> blockMap, BlockPos floor, RandomSource random) {
        final int len = 1 + random.nextInt(2);
        for (int i = 0; i < len; i++) {
            final BlockPos at = floor.above(i);
            if (i > 0 && blockMap.containsKey(at)) break; // don't punch into the ceiling
            final DripstoneThickness th = (i == len - 1) ? DripstoneThickness.TIP
                    : (i == 0) ? (len >= 2 ? DripstoneThickness.BASE : DripstoneThickness.FRUSTUM)
                    : DripstoneThickness.MIDDLE;
            blockMap.put(at, Blocks.POINTED_DRIPSTONE.defaultBlockState()
                    .setValue(BlockStateProperties.VERTICAL_DIRECTION, Direction.UP)
                    .setValue(BlockStateProperties.DRIPSTONE_THICKNESS, th));
        }
    }

    /** Reachability: 40% hidden (mine in), 35% a sinkhole shaft, 25% a gash ravine, opened from the shallowest room. */
    private static void breach(Map<BlockPos, BlockState> blockMap, List<BlockPos> surfaceList, Map<Long, Integer> topY,
                              BlockPos center, List<BlockPos> rooms, RandomSource random) {
        if (rooms.isEmpty()) return;
        final float s = random.nextFloat();
        if (s < 0.40f) return; // hidden
        BlockPos room = rooms.get(0);
        for (BlockPos r : rooms) if (r.getY() > room.getY()) room = r; // the shallowest (highest) room
        final Set<Long> breached = new HashSet<>();
        if (s < 0.75f) {
            sinkhole(blockMap, topY, center, room, breached);
        } else {
            gash(blockMap, topY, center, room, random, breached);
        }
        surfaceList.removeIf(p -> breached.contains(key(p.getX() - center.getX(), p.getZ() - center.getZ())));
    }

    /** A ~2-radius vertical shaft from the room up through the surface — an obvious way in. */
    private static void sinkhole(Map<BlockPos, BlockState> blockMap, Map<Long, Integer> topY, BlockPos center,
                                 BlockPos room, Set<Long> breached) {
        for (int dx = -2; dx <= 2; dx++) {
            for (int dz = -2; dz <= 2; dz++) {
                if (dx * dx + dz * dz > 4) continue;
                final long k = key(room.getX() - center.getX() + dx, room.getZ() - center.getZ() + dz);
                final Integer t = topY.get(k);
                if (t == null) continue;
                for (int y = room.getY(); y <= t + 1; y++) blockMap.remove(new BlockPos(room.getX() + dx, y, room.getZ() + dz));
                breached.add(k);
            }
        }
    }

    /** A long, ~1-2-wide ravine slicing from the surface down to the room — daylight floods the cave. */
    private static void gash(Map<BlockPos, BlockState> blockMap, Map<Long, Integer> topY, BlockPos center,
                             BlockPos room, RandomSource random, Set<Long> breached) {
        final double ang = random.nextDouble() * Math.PI * 2;
        final double dirX = Math.cos(ang), dirZ = Math.sin(ang);
        final double perpX = -dirZ, perpZ = dirX;
        final int len = 8 + random.nextInt(5); // 8-12 long
        for (int i = -len / 2; i <= len / 2; i++) {
            for (int w = -1; w <= 1; w++) {
                final int dx = room.getX() - center.getX() + (int) Math.round(dirX * i + perpX * w);
                final int dz = room.getZ() - center.getZ() + (int) Math.round(dirZ * i + perpZ * w);
                final long k = key(dx, dz);
                final Integer t = topY.get(k);
                if (t == null) continue;
                for (int y = room.getY() - 1; y <= t + 1; y++) blockMap.remove(new BlockPos(center.getX() + dx, y, center.getZ() + dz));
                breached.add(k);
            }
        }
    }
}
