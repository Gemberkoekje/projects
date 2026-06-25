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
        final ChunkGenerator generator = level.getChunkSource().getGenerator();
        final StructureTemplateManager templates = level.getStructureManager();
        final StructureManager structureManager = level.structureManager();
        final RandomSource random = level.getRandom();
        final Structure.GenerationContext context = new Structure.GenerationContext(
                level.registryAccess(), generator, generator.getBiomeSource(), level.getChunkSource().randomState(),
                templates, level.getSeed(), new ChunkPos(origin), level, biome -> true);
        final Optional<Structure.GenerationStub> stub = JigsawPlacement.addPieces(
                context, pool, Optional.of(target), depth, origin, false, Optional.empty(), 128,
                PoolAliasLookup.EMPTY, JigsawStructure.DEFAULT_DIMENSION_PADDING, JigsawStructure.DEFAULT_LIQUID_SETTINGS);
        if (stub.isEmpty()) {
            return;
        }
        final List<StructurePiece> pieces = new ArrayList<>(stub.get().getPiecesBuilder().build().pieces());
        if (capCount > 0 && !capPrefix.isEmpty()) {
            normaliseCappedPieces(pieces, origin, capPrefix, capCount, fillerPool, templates, random);
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
                                              StructureTemplateManager templates, RandomSource random) {
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
            final StructurePoolElement filler = fillerPool.value().getRandomTemplate(random);
            final BlockPos pos = keep.getPosition();
            final Rotation rotation = keep.getRotation();
            pieces.set(index, new PoolElementStructurePiece(templates, filler, pos, filler.getGroundLevelDelta(),
                    rotation, filler.getBoundingBox(templates, pos, rotation), JigsawStructure.DEFAULT_LIQUID_SETTINGS));
        }
    }
}
