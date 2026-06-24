package dev.gemberkoekje.skyseed.compat;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.levelgen.structure.pools.JigsawPlacement;
import net.minecraft.world.level.levelgen.structure.pools.StructureTemplatePool;

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
                             int depth, BlockPos origin, boolean useExpansionHack) {
        JigsawPlacement.generateJigsaw(level, pool, target, depth, origin, useExpansionHack);
    }
}
