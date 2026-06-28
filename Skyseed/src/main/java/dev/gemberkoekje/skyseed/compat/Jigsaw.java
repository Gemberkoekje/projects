package dev.gemberkoekje.skyseed.compat;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.ChunkPos;
import net.minecraft.world.level.StructureManager;
import net.minecraft.world.level.block.Rotation;
import net.minecraft.world.level.chunk.ChunkGenerator;
import net.minecraft.world.level.levelgen.structure.BoundingBox;
import net.minecraft.world.level.levelgen.structure.PoolElementStructurePiece;
import net.minecraft.world.level.levelgen.structure.Structure;
import net.minecraft.world.level.levelgen.structure.StructurePiece;
import net.minecraft.world.level.levelgen.structure.pools.JigsawPlacement;
import net.minecraft.world.level.levelgen.structure.pools.StructurePoolElement;
import net.minecraft.world.level.levelgen.structure.pools.StructureTemplatePool;
import net.minecraft.world.level.levelgen.structure.pools.alias.PoolAliasLookup;
import net.minecraft.world.level.levelgen.structure.structures.JigsawStructure;
import net.minecraft.world.level.levelgen.structure.templatesystem.StructureTemplateManager;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Optional;

/**
 * Version-volatile jigsaw assembly, isolated behind a stable signature.
 *
 * <p>{@code JigsawPlacement.generateJigsaw}'s parameter list changes occasionally between versions; routing the one
 * call site through here keeps that churn out of {@code GenerationJob}. See {@code REFACTORPLAN.md}.
 */
public final class Jigsaw {

    private Jigsaw() {
    }

    /** Assemble the jigsaw {@code pool} starting from {@code target}, depth-limited, at {@code origin}. */
    public static void place(ServerLevel level, Holder<StructureTemplatePool> pool, ResourceLocation target,
                             int depth, BlockPos origin, boolean keepJigsaws) {
        placeCapped(level, pool, target, depth, origin, keepJigsaws, "", 0, null);
    }

    /**
     * As {@link #place}, but forces a family of elements to an exact count. Vanilla's jigsaw has no per-element
     * limit, and even with the pool stacked toward one element the assembly only places <em>as many as randomly
     * roll and fit</em> — so a "cap" alone cannot raise a sparse village up to a target, only trim a dense one down.
     * This instead guarantees the planned count: a lot pool made entirely of the capped element places as many lots
     * as fit, then the assembled list is normalised — the {@code capCount} pieces nearest {@code origin} are kept,
     * and every surplus one is <em>replaced</em> (at its own position + rotation, before anything is stamped) by a
     * random element drawn from {@code fillerPool}. So a trade post lands its rolled 2–4 shops whenever that many
     * lots placed at all, and the rest of the lots become the fields/gardens from the filler pool.
     *
     * <p>A {@code capCount <= 0} (or empty prefix) disables it and behaves exactly like {@link #place}. A null
     * {@code fillerPool} drops the surplus instead of replacing it (leaving the lot empty).
     */
    public static void placeCapped(ServerLevel level, Holder<StructureTemplatePool> pool, ResourceLocation target,
                                   int depth, BlockPos origin, boolean keepJigsaws, String capPrefix, int capCount,
                                   Holder<StructureTemplatePool> fillerPool) {
        placeCapped(level, pool, target, depth, origin, keepJigsaws, capPrefix, capCount, fillerPool, level.getSeed());
    }

    /** As {@link #placeCapped(ServerLevel, Holder, ResourceLocation, int, BlockPos, boolean, String, int, Holder)},
     *  taking the start jigsaw {@code target} as a version-agnostic {@link Id} (the generator's JigsawSite path). */
    public static void placeCapped(ServerLevel level, Holder<StructureTemplatePool> pool, Id target,
                                   int depth, BlockPos origin, boolean keepJigsaws, String capPrefix, int capCount,
                                   Holder<StructureTemplatePool> fillerPool) {
        placeCapped(level, pool, Ids.parse(target.value()), depth, origin, keepJigsaws, capPrefix, capCount, fillerPool);
    }

    /**
     * As {@link #placeCapped(ServerLevel, Holder, ResourceLocation, int, BlockPos, boolean, String, int, Holder)} but
     * with an explicit {@code featureSeed} driving the jigsaw assembly RNG (vanilla seeds it from the world seed and
     * the origin's chunk, so a fixed origin always assembles the same village). Production passes the world seed; a
     * test can pass a varying seed to sample many different villages deterministically.
     */
    public static void placeCapped(ServerLevel level, Holder<StructureTemplatePool> pool, ResourceLocation target,
                                   int depth, BlockPos origin, boolean keepJigsaws, String capPrefix, int capCount,
                                   Holder<StructureTemplatePool> fillerPool, long featureSeed) {
        final ChunkGenerator generator = level.getChunkSource().getGenerator();
        final StructureTemplateManager templates = level.getStructureManager();
        final StructureManager structureManager = level.structureManager();
        // Seed the cap's filler selection + the piece stamping deterministically from (featureSeed, origin) instead of
        // the shared world RNG (level.getRandom()), so the same call always produces the same structure. In-game the
        // featureSeed is the world seed and the origin varies per island, so structures still differ by location; in
        // gametests the explicit featureSeed + fixed origin make the whole placement reproducible (no more flaky runs).
        final RandomSource random = RandomSource.create(featureSeed ^ origin.asLong());
        final Structure.GenerationContext context = new Structure.GenerationContext(
                level.registryAccess(), generator, generator.getBiomeSource(), level.getChunkSource().randomState(),
                templates, featureSeed, new ChunkPos(origin), level, biome -> true);
        final Optional<Structure.GenerationStub> stub = JigsawPlacement.addPieces(
                context, pool, Optional.of(target), depth, origin, false, Optional.empty(), 128,
                PoolAliasLookup.EMPTY, JigsawStructure.DEFAULT_DIMENSION_PADDING, JigsawStructure.DEFAULT_LIQUID_SETTINGS);
        if (stub.isEmpty()) {
            return;
        }
        final List<StructurePiece> pieces = new ArrayList<>(stub.get().getPiecesBuilder().build().pieces());
        if (capCount > 0 && !capPrefix.isEmpty()) {
            // A parallel "over the void" filler pool, by naming convention <filler>_void: lots that land over the void
            // draw from it (plank piers) instead of the on-island fields/gardens. The terrain is already in the world
            // at this point, so we can tell island from void below each lot. Falls back to the normal pool if absent.
            Holder<StructureTemplatePool> voidFillerPool = fillerPool;
            if (fillerPool != null && fillerPool.unwrapKey().isPresent()) {
                final ResourceLocation voidId = fillerPool.unwrapKey().get().location().withSuffix("_void");
                if (Lookup.hasTemplatePool(level.registryAccess(), voidId)) {
                    voidFillerPool = Lookup.templatePool(level.registryAccess(), voidId);
                }
            }
            normaliseCappedPieces(pieces, origin, capPrefix, capCount, fillerPool, voidFillerPool, templates, random, level);
        }
        for (final StructurePiece piece : pieces) {
            if (piece instanceof PoolElementStructurePiece poolPiece) {
                poolPiece.place(level, structureManager, generator, random, BoundingBox.infinite(), origin, keepJigsaws);
            }
        }
    }

    /**
     * Keep the {@code cap} capped-family pieces nearest {@code origin}; replace each surplus one with a random
     * {@code fillerPool} element at the same spot (or drop it if {@code fillerPool} is null). Replacing in the piece
     * list — before stamping — means the filler lands on the (already-clear) lot footprint with no overlap fuss.
     */
    private static void normaliseCappedPieces(List<StructurePiece> pieces, BlockPos origin, String capPrefix, int cap,
                                              Holder<StructureTemplatePool> fillerPool,
                                              Holder<StructureTemplatePool> voidFillerPool,
                                              StructureTemplateManager templates, RandomSource random, ServerLevel level) {
        final List<PoolElementStructurePiece> capped = new ArrayList<>();
        for (final StructurePiece piece : pieces) {
            if (piece instanceof PoolElementStructurePiece poolPiece
                    && poolPiece.getElement().toString().contains(capPrefix)) {
                capped.add(poolPiece);
            }
        }
        if (capped.size() <= cap) {
            return;
        }
        capped.sort(Comparator.comparingInt(piece -> {
            final BoundingBox box = piece.getBoundingBox();
            final int dx = (box.minX() + box.maxX()) / 2 - origin.getX();
            final int dz = (box.minZ() + box.maxZ()) / 2 - origin.getZ();
            return dx * dx + dz * dz;
        }));
        final List<PoolElementStructurePiece> surplus = new ArrayList<>(capped.subList(cap, capped.size()));
        for (final PoolElementStructurePiece keep : surplus) {
            final int index = pieces.indexOf(keep);
            if (fillerPool == null) {
                pieces.remove(index);
                continue;
            }
            // A lot over the void gets the over-void filler set (piers); one on the island gets the normal fields.
            final Holder<StructureTemplatePool> pool = overVoid(level, keep.getBoundingBox()) ? voidFillerPool : fillerPool;
            final StructurePoolElement filler = pool.value().getRandomTemplate(random);
            final BlockPos pos = keep.getPosition();
            final Rotation rotation = keep.getRotation();
            pieces.set(index, new PoolElementStructurePiece(templates, filler, pos, filler.getGroundLevelDelta(),
                    rotation, filler.getBoundingBox(templates, pos, rotation), JigsawStructure.DEFAULT_LIQUID_SETTINGS));
        }
    }

    /** True if nothing solid sits within a few blocks below the centre of {@code box} — i.e. the lot is over the void
     *  (the island terrain is already placed when this runs, and a lot's own foundation isn't dropped until later). */
    private static boolean overVoid(ServerLevel level, BoundingBox box) {
        final int cx = (box.minX() + box.maxX()) / 2;
        final int cz = (box.minZ() + box.maxZ()) / 2;
        for (int d = 1; d <= 2; d++) { // the island's fill sits right under a lot; a couple of blocks is enough to tell
            if (!level.getBlockState(new BlockPos(cx, box.minY() - d, cz)).isAir()) {
                return false; // island ground below
            }
        }
        return true;
    }
}
