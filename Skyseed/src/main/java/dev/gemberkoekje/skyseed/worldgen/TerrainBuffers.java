package dev.gemberkoekje.skyseed.worldgen;

import net.minecraft.core.BlockPos;
import net.minecraft.world.level.block.state.BlockState;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * The mutable output buffers of pass 1 (terrain): the placed {@code blockMap} plus the per-column lists the later
 * planners read — {@code coreList} (where ores may go), {@code surfaceList} (where decoration/ponds go) and
 * {@code bottomList} (the lowest block of each column, for underside hangs). Bundled into one parameter object so
 * {@link ShapeBuilder#build} takes a single named output instead of four loose accumulators, and a cluster shares one
 * set across its stamps. See {@code SKYJIGSAWPLAN.md} (review Finding 3).
 */
record TerrainBuffers(Map<BlockPos, BlockState> blockMap, List<BlockPos> coreList,
                      List<BlockPos> surfaceList, List<BlockPos> bottomList) {

    /** Fresh, empty buffers. The block map is a {@link LinkedHashMap} so placement order is preserved. */
    static TerrainBuffers create() {
        return new TerrainBuffers(new LinkedHashMap<>(), new ArrayList<>(), new ArrayList<>(), new ArrayList<>());
    }
}
