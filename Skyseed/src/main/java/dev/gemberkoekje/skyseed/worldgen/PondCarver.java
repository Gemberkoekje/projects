package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.worldgen.theme.GroundEntry;
import dev.gemberkoekje.skyseed.worldgen.theme.Pond;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.DoublePlantBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;
import net.minecraft.world.level.block.state.properties.DoubleBlockHalf;

import java.util.ArrayList;
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

    /** Pond extent as a fraction of the island radius — keeps the pool well inside the rim so it can be contained. */
    private static final double POND_EXTENT_FRACTION = 0.5;
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
        // Keep the pool well inside the island (extent ≈ 0.62·baseRadius after wobble) so its rim always has
        // solid ground for the containment ring to wall against — overshooting the rim is what made it overflow.
        final int r = Math.max(1, Math.min(pond.radius(), (int) Math.round(baseRadius * POND_EXTENT_FRACTION)));
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
                    if (BuiltInRegistries.BLOCK.containsKey(g.block())) {
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
                BlockState st = BuiltInRegistries.BLOCK.get(id).defaultBlockState();
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
            // Only the immediate water's-edge — a 4-neighbour must be a carved water column — so the cane
            // stays put (sugar cane needs water beside it or it pops on the first update).
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
                    if (BuiltInRegistries.BLOCK.containsKey(g.block())) {
                        bankPlant(blockMap, above, g.block(), random);
                    }
                    break;
                }
            }
        }
    }

    /** Place a bank plant; sugar cane stacks 1-3 tall, everything else is a single block. */
    private static void bankPlant(Map<BlockPos, BlockState> blockMap, BlockPos above, ResourceLocation id, RandomSource random) {
        final BlockState state = BuiltInRegistries.BLOCK.get(id).defaultBlockState();
        if (id.getPath().equals("sugar_cane")) {
            final int h = 1 + random.nextInt(3); // 1-3 tall
            for (int i = 0; i < h; i++) {
                blockMap.put(above.above(i), state);
            }
        } else {
            blockMap.put(above, state);
        }
    }
}
