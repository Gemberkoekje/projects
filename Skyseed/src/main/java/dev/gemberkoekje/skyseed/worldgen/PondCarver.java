package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.compat.Lookup;
import dev.gemberkoekje.skyseed.worldgen.theme.GroundEntry;
import dev.gemberkoekje.skyseed.worldgen.theme.Pond;
import net.minecraft.core.BlockPos;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.tags.BlockTags;
import net.minecraft.tags.FluidTags;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.DoublePlantBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;
import net.minecraft.world.level.block.state.properties.DoubleBlockHalf;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Carves a contained water feature (pond blob or meandering river) into the island top, walls any open edge into a
 * containing ring so the pool can't sheet off the rim, dresses the bed/shore with sand/clay/gravel, and scatters
 * water plants and shore banks across the exact carved columns.
 */
final class PondCarver {
    private PondCarver() {}

    private static final int[][] NEIGHBORS_8 = {
            {1, 0}, {-1, 0}, {0, 1}, {0, -1}, {1, 1}, {1, -1}, {-1, 1}, {-1, -1}
    };

    /** Strength of the pond's harmonic edge wobble (up to ~28%). */
    private static final double POND_RIM_WOBBLE = 0.28;

    /**
     * Water surface for a pond: flush with the island's top at the pond's rim, not the un-domed base
     * ({@code center.getY()}). The top is domed up by {@code topDome} at the centre, so filling to the
     * base would leave the pool recessed several blocks below the surface; this lifts it to the surface
     * and only digs {@code depth} down from there.
     */
    static int pondWaterY(BlockPos center, int topDome, int baseRadius, Pond pond) {
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
    static Set<Long> carvePond(Map<BlockPos, BlockState> blockMap, List<BlockPos> surfaceList, BlockPos center,
                               int topDome, int waterY, int baseRadius, Pond pond, BlockState water, RandomSource random) {
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

    /**
     * Contain a carved pool and dress it with water-side materials. Every land column touching the water has
     * any gap below the surface walled up to {@code waterY} (the "ring of dirt" that keeps the pool from
     * sheeting off the edge); the bed gets sand/clay/gravel, and the shore gets sandy/gravelly patches. Runs
     * before plants and banks so decorations sit on the finished, contained edge.
     */
    static void containPond(Map<BlockPos, BlockState> blockMap, List<BlockPos> surfaceList, BlockPos center,
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

    /**
     * Soften the banks of a carved pool: step the land down toward the waterline in concentric rings, turning a
     * sheer channel into a gentle slope. For natural variation, each island leaves its banks <em>steep</em>, fully
     * <em>sloped</em>, or a coherent <em>mix</em> of the two. Only ever lowers land that sits above the water surface,
     * so it can't spill the pool — and the flush inner ring it creates is exactly where sugar cane can then grow.
     */
    static void terraceBanks(Map<BlockPos, BlockState> blockMap, List<BlockPos> surfaceList, BlockPos center,
                             int waterY, Set<Long> carved, BlockState surface, RandomSource random) {
        if (carved.isEmpty()) {
            return;
        }
        final float style = random.nextFloat();
        if (style < 0.34f) {
            return; // STEEP island — leave the carve sheer
        }
        final boolean mixed = style >= 0.67f; // else SLOPED (every stretch); MIXED slopes only some stretches
        final int steps = 2 + random.nextInt(2); // slope length: 2-3 rings out from the water
        final long salt = random.nextLong(); // a per-island pattern for the mixed style

        // Current surface height per column, from the contained surface list.
        final Map<Long, Integer> topByCol = new HashMap<>();
        for (BlockPos p : surfaceList) {
            topByCol.merge(colKey(p.getX() - center.getX(), p.getZ() - center.getZ()), p.getY(), Math::max);
        }

        final Map<Long, BlockPos> lowered = new HashMap<>(); // column -> its new (lowered) surface block
        final Set<Long> seen = new HashSet<>(carved);
        Set<Long> prev = carved;
        for (int d = 1; d <= steps; d++) {
            final int targetY = waterY + (d - 1); // ring 1 sits flush with the water; each ring out rises one block
            final Set<Long> ring = new HashSet<>();
            for (long k : prev) {
                final int dx = (int) (k >> 32);
                final int dz = (int) k;
                for (int[] n : NEIGHBORS_8) {
                    final long nk = colKey(dx + n[0], dz + n[1]);
                    if (!seen.add(nk)) {
                        continue;
                    }
                    ring.add(nk);
                    final int wx = center.getX() + dx + n[0];
                    final int wz = center.getZ() + dz + n[1];
                    if (mixed && !slopeHere(wx, wz, salt)) {
                        continue; // this stretch stays steep
                    }
                    final Integer top = topByCol.get(nk);
                    if (top == null || top <= targetY) {
                        continue; // off-island, or already at/below the step height (only ever lower, never raise)
                    }
                    for (int y = targetY + 1; y <= top; y++) {
                        blockMap.remove(new BlockPos(wx, y, wz));
                    }
                    final BlockPos newTop = new BlockPos(wx, targetY, wz);
                    blockMap.put(newTop, surface);
                    lowered.put(nk, newTop);
                    topByCol.put(nk, targetY);
                }
            }
            prev = ring;
        }

        if (!lowered.isEmpty()) {
            surfaceList.removeIf(p -> lowered.containsKey(colKey(p.getX() - center.getX(), p.getZ() - center.getZ())));
            surfaceList.addAll(lowered.values());
        }
    }

    /** Coherent ~8-block-region gate for the "mixed" bank style: some stretches slope, others stay steep. */
    private static boolean slopeHere(int wx, int wz, long salt) {
        long h = (((long) (wx >> 3)) * 0x9E3779B97F4A7C15L) ^ (((long) (wz >> 3)) * 0xC2B2AE3D27D4EB4FL) ^ salt;
        h ^= h >>> 31;
        return (h & 1L) == 0L;
    }

    /** Pond-bed material: mostly sand, some gravel and clay. @return the bed block, or {@code null} to keep the island's own block. */
    private static BlockState pondBed(RandomSource random) {
        final float x = random.nextFloat();
        if (x < 0.30f) {
            return Blocks.SAND.defaultBlockState();
        }
        if (x < 0.50f) {
            return Blocks.GRAVEL.defaultBlockState();
        }
        if (x < 0.62f) {
            return Blocks.CLAY.defaultBlockState();
        }
        return null; // keep the existing island body
    }

    /** Shore material at the waterline: sandy/gravelly patches, otherwise {@code fallback}. @return the shore block, or {@code fallback} (which may be {@code null} = leave as-is). */
    private static BlockState pondShore(RandomSource random, BlockState fallback) {
        final float x = random.nextFloat();
        if (x < 0.22f) {
            return Blocks.SAND.defaultBlockState();
        }
        if (x < 0.35f) {
            return Blocks.GRAVEL.defaultBlockState();
        }
        return fallback;
    }

    /** Candidate columns for an irregular radial pond — a few angular harmonics give it a blobby, non-round edge. */
    private static List<int[]> pondColumns(Pond pond, int baseRadius, RandomSource random) {
        final List<int[]> out = new ArrayList<>();
        // Keep the pool inside the island (extent·baseRadius, default 0.5) so its rim always has solid ground for the
        // containment ring to wall against — overshooting the rim is what made it overflow. A theme can raise extent
        // for a near island-filling lake (huge water islands), trading rim width for a bigger pool.
        final int r = Math.max(1, Math.min(pond.radius(), (int) Math.round(baseRadius * pond.extent())));
        final RimNoise edge = RimNoise.sample(random, POND_RIM_WOBBLE);
        final int maxR = (int) Math.ceil(r * 1.25) + 1;
        for (int dx = -maxR; dx <= maxR; dx++) {
            for (int dz = -maxR; dz <= maxR; dz++) {
                final double dist = Math.sqrt((double) dx * dx + (double) dz * dz);
                final double angle = Math.atan2(dz, dx);
                if (dist <= edge.rim(r, angle)) {
                    out.add(new int[]{dx, dz});
                }
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
                if (Math.abs(perp - centerline) <= half) {
                    out.add(new int[]{dx, dz});
                }
            }
        }
        return out;
    }

    /** Scatter water plants through the carved water columns: lily pads on the surface, kelp/seagrass/coral on the floor. */
    static void placePondPlants(Map<BlockPos, BlockState> blockMap, BlockPos center, int waterY, Pond pond, Set<Long> carved, RandomSource random) {
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
                    if (Lookup.hasBlock(g.block())) {
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
                BlockState st = Lookup.blockState(id);
                if (st.hasProperty(BlockStateProperties.WATERLOGGED)) {
                    st = st.setValue(BlockStateProperties.WATERLOGGED, Boolean.TRUE); // coral fans, sea pickle, …
                }
                blockMap.put(floor, st);
            }
        }
    }

    /** Grow shore plants (e.g. sugar cane) on the ring of land right at the carved water's edge. */
    static void placePondBanks(Map<BlockPos, BlockState> blockMap, List<BlockPos> surfaceList,
                               BlockPos center, Pond pond, Set<Long> carved, RandomSource random) {
        if (pond.bank().isEmpty() || carved.isEmpty()) {
            return;
        }
        for (BlockPos col : surfaceList) { // carved columns were already removed from surfaceList
            final int dx = col.getX() - center.getX();
            final int dz = col.getZ() - center.getZ();
            // The bank ring: a column whose 4-neighbour is carved water. (Sugar cane gets a stricter, exact check in
            // bankPlant — water must sit beside its *supporting* block at the same Y, or it pops on a steep bank.)
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
                    if (Lookup.hasBlock(g.block())) {
                        bankPlant(blockMap, above, g.block(), random);
                    }
                    break;
                }
            }
        }
    }

    /** Place a bank plant; sugar cane stacks 1-3 tall, everything else is a single block. */
    private static void bankPlant(Map<BlockPos, BlockState> blockMap, BlockPos above, ResourceLocation id, RandomSource random) {
        final BlockState state = Lookup.blockState(id);
        if (id.getPath().equals("sugar_cane")) {
            final int h = 1 + random.nextInt(3); // 1-3 tall (rolled regardless of placement, to keep generation deterministic)
            if (!caneCanStand(blockMap, above.below())) {
                return; // would pop on the first tick — only grow cane where water sits beside its supporting block
            }
            for (int i = 0; i < h; i++) {
                blockMap.put(above.above(i), state);
            }
        } else {
            blockMap.put(above, state);
        }
    }

    /** Sugar cane survives only on dirt/sand-type ground with water at the same Y horizontally beside it — else it pops. */
    private static boolean caneCanStand(Map<BlockPos, BlockState> blockMap, BlockPos support) {
        final BlockState soil = blockMap.get(support);
        if (soil == null || !(soil.is(BlockTags.DIRT) || soil.is(Blocks.SAND) || soil.is(Blocks.RED_SAND))) {
            return false;
        }
        return isWater(blockMap, support.east()) || isWater(blockMap, support.west())
                || isWater(blockMap, support.north()) || isWater(blockMap, support.south());
    }

    private static boolean isWater(Map<BlockPos, BlockState> blockMap, BlockPos p) {
        final BlockState s = blockMap.get(p);
        return s != null && s.getFluidState().is(FluidTags.WATER);
    }
}
