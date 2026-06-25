package dev.gemberkoekje.skyseed.compat;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.ChunkPos;
import net.minecraft.world.level.StructureManager;
import net.minecraft.world.level.chunk.ChunkGenerator;
import net.minecraft.world.level.levelgen.structure.BoundingBox;
import net.minecraft.world.level.levelgen.structure.PoolElementStructurePiece;
import net.minecraft.world.level.levelgen.structure.Structure;
import net.minecraft.world.level.levelgen.structure.StructurePiece;
import net.minecraft.world.level.levelgen.structure.pools.JigsawPlacement;
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
        placeCapped(level, pool, target, depth, origin, keepJigsaws, "", 0);
    }

    /**
     * As {@link #place}, but caps a family of elements. Vanilla's jigsaw has no per-element limit, so a pool that is
     * allowed to run long places as many of every element as fit. This splits the difference: assemble the full
     * layout, then before stamping it in, drop any piece whose element name contains {@code capPrefix} beyond the
     * {@code capCount} that sit nearest {@code origin}. A village can thus have long, branching streets and many
     * fields while its shops stay a tidy handful — the spare lots simply don't receive their (capped) building.
     * A {@code capCount <= 0} (or empty prefix) disables the cap and behaves exactly like {@link #place}.
     *
     * <p>This mirrors {@code generateJigsaw}: it builds the piece list via the public {@code addPieces} and stamps
     * each {@link PoolElementStructurePiece}; the only addition is the filter step in between.
     */
    public static void placeCapped(ServerLevel level, Holder<StructureTemplatePool> pool, ResourceLocation target,
                                   int depth, BlockPos origin, boolean keepJigsaws, String capPrefix, int capCount) {
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
            dropCappedPieces(pieces, origin, capPrefix, capCount);
        }
        for (final StructurePiece piece : pieces) {
            if (piece instanceof PoolElementStructurePiece poolPiece) {
                poolPiece.place(level, structureManager, generator, random, BoundingBox.infinite(), origin, keepJigsaws);
            }
        }
    }

    /** Drop capped-family pieces beyond the {@code cap} nearest {@code origin}, keeping the central ones. */
    private static void dropCappedPieces(List<StructurePiece> pieces, BlockPos origin, String capPrefix, int cap) {
        final List<StructurePiece> capped = new ArrayList<>();
        for (final StructurePiece piece : pieces) {
            if (piece instanceof PoolElementStructurePiece poolPiece
                    && poolPiece.getElement().toString().contains(capPrefix)) {
                capped.add(piece);
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
        pieces.removeAll(capped.subList(cap, capped.size()));
    }
}
