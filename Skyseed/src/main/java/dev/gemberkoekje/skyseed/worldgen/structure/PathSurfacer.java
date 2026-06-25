package dev.gemberkoekje.skyseed.worldgen.structure;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/**
 * Resolves the path markers a connective jigsaw piece leaves behind (SKYJIGSAWPLAN §3a) into a terrain-aware
 * surface — a worn dirt path where the deck sits on ground, a self-railing wooden bridge where it runs out over
 * the void. Connective pieces bake no floor; they place a reserved {@link #MARKER} one block <em>above</em> each
 * path tile, and this pass reads what is under each marker and fills it in:
 * <ul>
 *   <li>solid ground under the deck → a worn {@code dirt_path};</li>
 *   <li>void under the deck → a wooden-slab deck, and for every side that is itself an open drop (a neighbour
 *       that is not a path tile and is also over void) a full-block edge beam + a fence railing — a free,
 *       barebones bridge that rails its exposed sides and caps its dead-ends, scaling with path width.</li>
 * </ul>
 * Two-phase: it snapshots every marker first, resolves decks + edges from that snapshot, then clears the markers
 * <em>last</em> — so reading a neighbour can never be fooled by an already-cleared marker. Whether a tile is over
 * void is decided by the block <em>under the deck</em> (not the deck itself), so it is robust to the deck tile
 * having been cleared to air by a connector. Called once per assembled structure by {@code GenerationJob}; a
 * no-op wherever there are no markers.
 */
public final class PathSurfacer {
    private PathSurfacer() {}

    /** The reserved sentinel a connective piece places one block above each path tile. Never used decoratively. */
    public static final Block MARKER = Blocks.PURPLE_WOOL;

    private static final int SCAN_DOWN = 2;
    private static final int SCAN_UP = 4;
    private static final int FLAGS = Block.UPDATE_CLIENTS; // no physics — fences are linked separately afterwards
    private static final int SUPPORT_DEPTH = 6; // how far a foundation hangs under a floor left over the void

    /** Resolve every path marker within {@code reach} (horizontal half-extent) of {@code origin}. */
    public static void resolve(ServerLevel level, BlockPos origin, int reach) {
        // Phase A — snapshot the markers and the set of deck (path-tile) columns they cover.
        final List<BlockPos> markers = new ArrayList<>();
        final Set<Long> deckTiles = new HashSet<>();
        final BlockPos.MutableBlockPos p = new BlockPos.MutableBlockPos();
        for (int dx = -reach; dx <= reach; dx++) {
            for (int dz = -reach; dz <= reach; dz++) {
                p.set(origin.getX() + dx, origin.getY(), origin.getZ() + dz);
                if (!level.isLoaded(p)) {
                    continue; // never force-load chunks the structure didn't reach — a wide reach stays cheap
                }
                for (int dy = -SCAN_DOWN; dy <= SCAN_UP; dy++) {
                    p.set(origin.getX() + dx, origin.getY() + dy, origin.getZ() + dz);
                    if (level.getBlockState(p).is(MARKER)) {
                        markers.add(p.immutable());
                        deckTiles.add(p.below().asLong());
                    }
                }
            }
        }
        if (markers.isEmpty()) {
            return;
        }
        // Phase B — resolve each deck tile, reading the void/ground test from below the deck.
        for (final BlockPos marker : markers) {
            final BlockPos deck = marker.below();
            if (level.getBlockState(deck.below()).isAir()) {
                bridge(level, deck, deckTiles);
            } else {
                level.setBlock(deck, Blocks.DIRT_PATH.defaultBlockState(), FLAGS); // a uniform worn path (no stripes)
            }
        }
        // Phase C — clear the markers (after every neighbour has been read).
        for (final BlockPos marker : markers) {
            level.setBlock(marker, Blocks.AIR.defaultBlockState(), FLAGS);
        }
    }

    /** A void deck tile: a slab deck, plus an edge beam + fence railing on each side that is itself an open drop. */
    private static void bridge(ServerLevel level, BlockPos deck, Set<Long> deckTiles) {
        level.setBlock(deck, Blocks.OAK_SLAB.defaultBlockState(), FLAGS);
        for (final Direction dir : Direction.Plane.HORIZONTAL) {
            final BlockPos nb = deck.relative(dir);
            if (!deckTiles.contains(nb.asLong()) && level.getBlockState(nb.below()).isAir()) {
                level.setBlock(nb, Blocks.OAK_PLANKS.defaultBlockState(), FLAGS);        // edge beam
                level.setBlock(nb.above(), Blocks.OAK_FENCE.defaultBlockState(), FLAGS); // railing
            }
        }
    }

    /**
     * Drop a short dirt foundation under any building or pier floor left hanging over the void, so a lot or a bridge
     * that ran off the island edge reads as anchored rather than floating in mid-air. Scans the structure's deck
     * level (one below {@code origin}) for solid floor blocks (planks / grass / dirt / farmland — building floors and
     * bridge edge beams; the thin slab deck is carried by those beams) sitting over an open drop, and stilts each
     * one down a few blocks. Called after {@link #resolve} so the bridges it just laid get supported too.
     */
    public static void supportFloatingFloors(ServerLevel level, BlockPos origin, int reach) {
        final int deckY = origin.getY() - 1; // the structure floor sits one below the jigsaw origin
        final BlockPos.MutableBlockPos p = new BlockPos.MutableBlockPos();
        for (int dx = -reach; dx <= reach; dx++) {
            for (int dz = -reach; dz <= reach; dz++) {
                p.set(origin.getX() + dx, deckY, origin.getZ() + dz);
                if (!level.isLoaded(p) || !isFloorBlock(level.getBlockState(p))
                        || !level.getBlockState(p.below()).isAir()) {
                    continue;
                }
                for (int d = 1; d <= SUPPORT_DEPTH; d++) {
                    level.setBlock(new BlockPos(p.getX(), deckY - d, p.getZ()), Blocks.DIRT.defaultBlockState(), FLAGS);
                }
            }
        }
    }

    /** Blocks that read as a building/pier floor — what a foundation should hang under when it's over the void. */
    private static boolean isFloorBlock(BlockState s) {
        return s.is(Blocks.OAK_PLANKS) || s.is(Blocks.GRASS_BLOCK) || s.is(Blocks.DIRT)
                || s.is(Blocks.COARSE_DIRT) || s.is(Blocks.FARMLAND);
    }
}
