package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.FrontAndTop;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.JigsawBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The <b>End City</b> (SKYENDPLAN Phase 3 flagship; SKYENDCITYPLAN) — a recursing <b>jigsaw</b> tileset modelled on
 * Mojang's {@code minecraft:end_city}, so each spawn is a stepped, overhanging purpur tower rather than one box. A
 * {@code start} base section (the vanilla {@code base_floor}: hollow purpur shell, doorway, an {@code end_city_treasure}
 * chest, and — interim until Phase 4 — the cantilevered End ship carrying the guaranteed <b>elytra</b>) roots the stack;
 * overhanging {@code floor_a}/{@code floor_b} tiers (vanilla {@code second_/third_floor}) stack on top through vertical
 * up/down jigsaw connectors, and a terraced {@code roof} (vanilla {@code third_roof}) caps it. Shulkers spawn on the
 * island as theme mobs. End-only; grown from the End City Seed. Phases 2–5 (towers, bridges, the fat-tower ship,
 * detailing) extend the same pools — see SKYENDCITYPLAN.md.
 */
public final class EndCityTemplates {
    private EndCityTemplates() {}

    /** Tiers + the roof self-stack through this pool: an UP connector (target {@code ec_tier}) mates a DOWN one (name {@code ec_tier}). */
    private static final String FLOOR_POOL = "skyseed:end_city/floor";
    /** A tier's outward side branch draws from here: a {@code tower_base} (a spire), a {@code bridge_end} (a span to a
     *  new section), or empty. */
    private static final String BRANCH_POOL = "skyseed:end_city/branch";
    /** A tower then rises through here: {@code tower_piece} (stacks) / {@code tower_top} (caps) / empty. */
    private static final String TOWER_PIECE_POOL = "skyseed:end_city/tower_piece";
    /** A bridge spans across here: {@code bridge_piece} / {@code bridge_stairs} (continue) / {@code wing} (a new section) / empty. */
    private static final String BRIDGE_POOL = "skyseed:end_city/bridge";

    private static final BlockState PURPUR = Blocks.PURPUR_BLOCK.defaultBlockState();
    private static final BlockState PILLAR = Blocks.PURPUR_PILLAR.defaultBlockState();
    private static final BlockState SLAB = Blocks.PURPUR_SLAB.defaultBlockState();
    private static final BlockState END_BRICK = Blocks.END_STONE_BRICKS.defaultBlockState();
    private static final BlockState GLASS = Blocks.MAGENTA_STAINED_GLASS.defaultBlockState();
    private static final BlockState ROD = Blocks.END_ROD.defaultBlockState();   // FACING up by default
    private static final BlockState AIR = Blocks.AIR.defaultBlockState();

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("start.nbt"), start());
        writeIfAbsent(dir.resolve("floor_a.nbt"), floorTier(5, false));
        writeIfAbsent(dir.resolve("floor_b.nbt"), floorTier(6, true));
        writeIfAbsent(dir.resolve("roof.nbt"), roof());
        writeIfAbsent(dir.resolve("tower_base.nbt"), towerBase());
        writeIfAbsent(dir.resolve("tower_piece.nbt"), towerPiece());
        writeIfAbsent(dir.resolve("tower_top.nbt"), towerTop());
        writeIfAbsent(dir.resolve("bridge_end.nbt"), bridgeEnd());
        writeIfAbsent(dir.resolve("bridge_piece.nbt"), bridgePiece());
        writeIfAbsent(dir.resolve("bridge_stairs.nbt"), bridgeStairs());
        writeIfAbsent(dir.resolve("wing.nbt"), wing());
    }

    // ---- shared helpers ----------------------------------------------------------------------------------------

    private static BlockState stair(Direction face) {
        return Blocks.PURPUR_STAIRS.defaultBlockState().setValue(BlockStateProperties.HORIZONTAL_FACING, face);
    }

    /** Place a jigsaw connector. A <em>vertical</em> seam uses the {@code aligned} joint (the parent UP and child DOWN
     *  connectors share the {@code north} top vector, so they mate with no spin); a <em>horizontal</em> seam stays
     *  {@code rollable} (free to spin), as {@link StructureParts#jig} bakes. */
    private static void jigAt(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int y, int z,
                              FrontAndTop orient, String name, String target, String pool, String finalState, boolean aligned) {
        m.put(new BlockPos(x, y, z), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, orient));
        final CompoundTag t = jig(name, target, pool, finalState);
        if (aligned) {
            t.putString("joint", "aligned");
        }
        bes.put(new BlockPos(x, y, z), t);
    }

    /** A tier/roof's UP seat that stacks the next tier/roof on top (becomes purpur if nothing rolls). */
    private static void upJig(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int y, int z) {
        jigAt(m, bes, x, y, z, FrontAndTop.UP_NORTH, "skyseed:ec_up", "skyseed:ec_tier", FLOOR_POOL, "minecraft:purpur_block", true);
    }

    /** A tier/roof's DOWN underside seat, matching a parent's {@link #upJig}. */
    private static void downJig(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int y, int z) {
        jigAt(m, bes, x, y, z, FrontAndTop.DOWN_NORTH, "skyseed:ec_tier", "skyseed:ec_cap", FLOOR_POOL, "minecraft:purpur_block", true);
    }

    /** A tier's outward side branch (horizontal) that may sprout a tower or a bridge; unused → air (no floating wart). */
    private static void sideJig(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int y, int z, FrontAndTop dir) {
        jigAt(m, bes, x, y, z, dir, "skyseed:ec_wall", "skyseed:ec_branch", BRANCH_POOL, "minecraft:air", false);
    }

    /** The stem (horizontal) on a tower_base / bridge_end that mates a tier's {@link #sideJig}. */
    private static void branchStem(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int y, int z, FrontAndTop dir) {
        jigAt(m, bes, x, y, z, dir, "skyseed:ec_branch", "skyseed:ec_dead", BRANCH_POOL, "minecraft:air", false);
    }

    /** A bridge segment's outward connector (horizontal) that draws the next span/wing from the bridge pool. */
    private static void bridgeOut(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int y, int z, FrontAndTop dir) {
        jigAt(m, bes, x, y, z, dir, "skyseed:ec_seam", "skyseed:ec_bridge", BRIDGE_POOL, "minecraft:air", false);
    }

    /** A bridge segment's / wing's incoming connector (horizontal), matching a parent's {@link #bridgeOut}. */
    private static void bridgeIn(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int y, int z, FrontAndTop dir) {
        jigAt(m, bes, x, y, z, dir, "skyseed:ec_bridge", "skyseed:ec_dead", BRIDGE_POOL, "minecraft:air", false);
    }

    /** A tower segment's UP seat (vertical) — stacks the next tower piece/top. */
    private static void towerUp(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int y, int z) {
        jigAt(m, bes, x, y, z, FrontAndTop.UP_NORTH, "skyseed:ec_up", "skyseed:ec_spire", TOWER_PIECE_POOL, "minecraft:air", true);
    }

    /** A tower segment's DOWN underside seat, matching a parent's {@link #towerUp}. */
    private static void towerDown(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int y, int z) {
        jigAt(m, bes, x, y, z, FrontAndTop.DOWN_NORTH, "skyseed:ec_spire", "skyseed:ec_dead", TOWER_PIECE_POOL, "minecraft:air", true);
    }

    /** A hollow purpur shell over {@code [x0..x1] × [z0..z1]}, floor {@code y0}, ceiling {@code y1}, corner pillars. */
    private static void shell(Map<BlockPos, BlockState> m, int x0, int x1, int z0, int z1, int y0, int y1) {
        for (int x = x0; x <= x1; x++) {
            for (int z = z0; z <= z1; z++) {
                m.put(new BlockPos(x, y0, z), PURPUR);
                m.put(new BlockPos(x, y1, z), PURPUR);
                final boolean perim = x == x0 || x == x1 || z == z0 || z == z1;
                final boolean corner = (x == x0 || x == x1) && (z == z0 || z == z1);
                for (int y = y0 + 1; y < y1; y++) {
                    m.put(new BlockPos(x, y, z), perim ? (corner ? PILLAR : PURPUR) : AIR);
                }
            }
        }
    }

    /** A purpur-stair parapet ringing the 11×11 lip's outer edge at height {@code y}, each stair facing inward. */
    private static void parapet(Map<BlockPos, BlockState> m, int y) {
        for (int z = 0; z <= 10; z++) {
            m.put(new BlockPos(0, y, z), stair(Direction.EAST));
            m.put(new BlockPos(10, y, z), stair(Direction.WEST));
        }
        for (int x = 0; x <= 10; x++) {
            m.put(new BlockPos(x, y, 0), stair(Direction.SOUTH));
            m.put(new BlockPos(x, y, 10), stair(Direction.NORTH));
        }
    }

    // ---- pieces ------------------------------------------------------------------------------------------------

    /** The base section (vanilla {@code base_floor}): a 9×9 hollow shell on the island, treasure inside, the interim
     *  ship off the east wall, and an UP connector that roots the stacking tower. */
    private static Built start() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();

        shell(m, 0, 8, 0, 8, 0, 8);                    // floor y0, walls y1..7, ceiling y8
        m.remove(new BlockPos(4, 1, 0));               // south doorway
        m.remove(new BlockPos(4, 2, 0));
        for (final int[] w : new int[][]{{2, 4, 0}, {6, 4, 0}, {0, 4, 2}, {0, 4, 6}, {8, 4, 2}, {8, 4, 6}}) {
            m.put(new BlockPos(w[0], w[1], w[2]), GLASS);   // window slits
        }
        m.put(new BlockPos(4, 2, 4), Blocks.CHEST.defaultBlockState().setValue(BlockStateProperties.HORIZONTAL_FACING, Direction.NORTH));
        bes.put(new BlockPos(4, 2, 4), lootChest("minecraft:chests/end_city_treasure"));
        for (final int[] c : new int[][]{{1, 2, 1}, {7, 2, 7}, {1, 2, 7}}) m.put(new BlockPos(c[0], c[1], c[2]), ROD);
        // No rods above the ceiling here: the base is capped by the tier that stacks on it, and a block at y9 would
        // push the base's bounding box up into that tier's floor (y9) and make the jigsaw reject it as a collision.

        // East-wall boarding opening → the ship.
        for (int y = 1; y <= 3; y++) m.remove(new BlockPos(8, y, 4));
        ship(m, bes);

        anchor(m, bes, new BlockPos(4, 0, 4), "minecraft:end_stone");   // seats on the island (target minecraft:bottom)
        upJig(m, bes, 4, 8, 4);                                          // stacks the first tier directly above
        return new Built(m, bes);
    }

    /** Interim End ship (Phase 4 re-homes it onto a fat tower): a small purpur hull cantilevered off the base's east
     *  wall at ground level, a mast, a dragon-head bowsprit, and the guaranteed-elytra chest at the stern. */
    private static void ship(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes) {
        for (int x = 9; x <= 13; x++) for (int z = 3; z <= 5; z++) m.put(new BlockPos(x, 1, z), PURPUR);  // hull body
        m.put(new BlockPos(14, 1, 4), PURPUR);                                                            // taper
        m.put(new BlockPos(15, 1, 4), PURPUR);                                                            // bow tip
        for (int x = 9; x <= 13; x++) { m.put(new BlockPos(x, 2, 3), PURPUR); m.put(new BlockPos(x, 2, 5), PURPUR); }  // gunwales
        m.put(new BlockPos(14, 2, 4), PURPUR);
        m.put(new BlockPos(15, 2, 4), PURPUR);
        for (int y = 2; y <= 6; y++) m.put(new BlockPos(11, y, 4), PILLAR);   // mast
        m.put(new BlockPos(11, 7, 4), ROD);                                  // masthead light
        m.put(new BlockPos(13, 2, 4), ROD);                                 // deck light
        m.put(new BlockPos(15, 3, 4), Blocks.DRAGON_HEAD.defaultBlockState().setValue(BlockStateProperties.ROTATION_16, 12));  // bowsprit
        m.put(new BlockPos(9, 2, 4), Blocks.CHEST.defaultBlockState().setValue(BlockStateProperties.HORIZONTAL_FACING, Direction.WEST));
        bes.put(new BlockPos(9, 2, 4), lootChest("skyseed:chests/end_ship"));
        m.put(new BlockPos(10, 2, 4), Blocks.BREWING_STAND.defaultBlockState());
    }

    /** A stacking tier (vanilla {@code second_/third_floor}): an 11×11 lip floor overhanging 9×9 walls (the stepped
     *  silhouette), a stair parapet, magenta windows, corner rods, and DOWN/UP connectors so tiers chain vertically.
     *  {@code wallTop} sets the wall height; {@code accents} bands the walls with end-stone brick (the floor_b variant). */
    private static Built floorTier(int wallTop, boolean accents) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int ceil = wallTop + 1;

        for (int x = 0; x <= 10; x++) for (int z = 0; z <= 10; z++) m.put(new BlockPos(x, 0, z), PURPUR);  // 11×11 lip floor
        for (int x = 1; x <= 9; x++) {                                                                     // 9×9 walls (overhang 1)
            for (int z = 1; z <= 9; z++) {
                m.put(new BlockPos(x, ceil, z), PURPUR);                                                   // ceiling
                final boolean perim = x == 1 || x == 9 || z == 1 || z == 9;
                final boolean corner = (x == 1 || x == 9) && (z == 1 || z == 9);
                for (int y = 1; y <= wallTop; y++) {
                    final BlockState w = corner ? PILLAR : (accents && (y == 2 || y == wallTop) ? END_BRICK : PURPUR);
                    if (perim) m.put(new BlockPos(x, y, z), w);
                    else m.put(new BlockPos(x, y, z), AIR);
                }
            }
        }
        for (final int[] g : new int[][]{{5, 3, 1}, {5, 3, 9}, {1, 3, 5}, {9, 3, 5}}) m.put(new BlockPos(g[0], g[1], g[2]), GLASS);
        parapet(m, 1);
        for (final int[] c : new int[][]{{0, 1, 0}, {10, 1, 0}, {0, 1, 10}, {10, 1, 10}}) m.put(new BlockPos(c[0], c[1], c[2]), ROD);

        // Outward branches at the lip edges (their destination cells are clear of the lip, so a tower won't collide).
        sideJig(m, bes, 0, 3, 5, FrontAndTop.WEST_UP);
        sideJig(m, bes, 10, 3, 5, FrontAndTop.EAST_UP);
        downJig(m, bes, 5, 0, 5);        // underside seat — mates the tier below
        upJig(m, bes, 5, ceil, 5);       // stacks the next tier/roof
        return new Built(m, bes);
    }

    /** The cap (vanilla {@code third_roof}): the lip + parapet, then a stepped purpur ziggurat crowned with an end rod. */
    private static Built roof() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();

        for (int x = 0; x <= 10; x++) for (int z = 0; z <= 10; z++) m.put(new BlockPos(x, 0, z), PURPUR);  // lip floor
        parapet(m, 1);
        for (final int[] c : new int[][]{{0, 1, 0}, {10, 1, 0}, {0, 1, 10}, {10, 1, 10}}) m.put(new BlockPos(c[0], c[1], c[2]), ROD);
        // Stepped ziggurat: each ring inset 1 and a block higher.
        for (int step = 1; step <= 3; step++) {
            final int lo = step, hi = 10 - step, y = step;
            for (int x = lo; x <= hi; x++) for (int z = lo; z <= hi; z++) m.put(new BlockPos(x, y, z), step == 3 ? SLAB : PURPUR);
        }
        for (int y = 4; y <= 6; y++) m.put(new BlockPos(5, y, 5), PILLAR);   // central spire
        m.put(new BlockPos(5, 7, 5), ROD);                                  // crowning light

        downJig(m, bes, 5, 0, 5);        // underside seat only — no UP connector, so the roof caps the stack
        return new Built(m, bes);
    }

    // ---- thin side towers (Phase 2) --------------------------------------------------------------------------------

    /** A purpur perimeter ring of the 3×3 footprint at height {@code y} (the 1×1 core stays a hollow shaft). */
    private static void tube(Map<BlockPos, BlockState> m, int y) {
        for (int x = 0; x <= 2; x++) {
            for (int z = 0; z <= 2; z++) {
                if (x != 1 || z != 1) {
                    m.put(new BlockPos(x, y, z), PURPUR);
                }
            }
        }
    }

    /** {@code tower_base} (vanilla {@code tower_base}): a slender 3×3 shaft that stems off a tier's side and rises into
     *  a spire. The stem is rotated to whatever side it attaches; the body cantilevers over the void like vanilla. */
    private static Built towerBase() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        for (int x = 0; x <= 2; x++) for (int z = 0; z <= 2; z++) m.put(new BlockPos(x, 0, z), PURPUR);  // floor
        for (int y = 1; y <= 4; y++) tube(m, y);                                                          // shaft walls
        branchStem(m, bes, 2, 2, 1, FrontAndTop.EAST_UP);   // stems toward the tier (the jigsaw spins it to any side)
        towerUp(m, bes, 1, 4, 1);                            // rises into tower_piece / tower_top
        return new Built(m, bes);
    }

    /** {@code tower_piece} (vanilla {@code tower_piece}): a stacking 3×3 shaft segment with a window and an inner rod. */
    private static Built towerPiece() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        for (int y = 0; y <= 3; y++) tube(m, y);
        m.put(new BlockPos(0, 2, 1), GLASS);    // a magenta window
        m.put(new BlockPos(1, 1, 1), ROD);      // inner light
        towerDown(m, bes, 1, 0, 1);
        towerUp(m, bes, 1, 3, 1);
        return new Built(m, bes);
    }

    /** {@code tower_top} (vanilla {@code tower_top}): caps the spire with a roof and crowning end rods. */
    private static Built towerTop() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        for (int y = 0; y <= 2; y++) tube(m, y);
        for (int x = 0; x <= 2; x++) for (int z = 0; z <= 2; z++) m.put(new BlockPos(x, 3, z), PURPUR);  // cap
        for (final int[] c : new int[][]{{0, 4, 0}, {2, 4, 0}, {0, 4, 2}, {2, 4, 2}, {1, 4, 1}}) m.put(new BlockPos(c[0], c[1], c[2]), ROD);
        towerDown(m, bes, 1, 0, 1);             // no UP — caps the spire
        return new Built(m, bes);
    }

    // ---- bridges + a second section (Phase 3) ----------------------------------------------------------------------

    /** A 3-wide walkway from {@code x0..x1} at height {@code y}: an end-stone-brick path flanked by purpur kerbs + rails. */
    private static void deck(Map<BlockPos, BlockState> m, int x0, int x1, int y) {
        for (int x = x0; x <= x1; x++) {
            m.put(new BlockPos(x, y, 1), END_BRICK);
            m.put(new BlockPos(x, y, 0), PURPUR);
            m.put(new BlockPos(x, y, 2), PURPUR);
            m.put(new BlockPos(x, y + 1, 0), PURPUR);
            m.put(new BlockPos(x, y + 1, 2), PURPUR);
        }
    }

    /** {@code bridge_end} (vanilla {@code bridge_end}): the span that leaves a tier and heads out over the void. */
    private static Built bridgeEnd() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        deck(m, 0, 3, 0);
        branchStem(m, bes, 3, 1, 1, FrontAndTop.EAST_UP);   // mates a tier's side branch
        bridgeOut(m, bes, 0, 1, 1, FrontAndTop.WEST_UP);    // continues the span outward
        return new Built(m, bes);
    }

    /** {@code bridge_piece} (vanilla {@code bridge_piece}): a straight span segment. */
    private static Built bridgePiece() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        deck(m, 0, 3, 0);
        bridgeIn(m, bes, 3, 1, 1, FrontAndTop.EAST_UP);
        bridgeOut(m, bes, 0, 1, 1, FrontAndTop.WEST_UP);
        return new Built(m, bes);
    }

    /** {@code bridge_stairs} (vanilla {@code bridge_gentle/steep_stairs}): a span that steps down two blocks. */
    private static Built bridgeStairs() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        deck(m, 3, 3, 2);   // east step (enters high)
        deck(m, 2, 2, 1);   // middle step
        deck(m, 0, 1, 0);   // west (leaves low)
        bridgeIn(m, bes, 3, 3, 1, FrontAndTop.EAST_UP);
        bridgeOut(m, bes, 0, 1, 1, FrontAndTop.WEST_UP);
        return new Built(m, bes);
    }

    /** {@code wing} (a bridge-reached {@code base_floor}): a second 7×7 section out over the void — its own treasure
     *  and an UP seat, so it stacks its own tiers (and may sprout more spires/spans). */
    private static Built wing() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        shell(m, 0, 6, 0, 6, 0, 7);
        for (final int[] g : new int[][]{{2, 4, 0}, {4, 4, 0}, {0, 4, 2}, {0, 4, 4}}) m.put(new BlockPos(g[0], g[1], g[2]), GLASS);
        m.remove(new BlockPos(6, 2, 3));        // headroom over the east-wall opening where the bridge lands (the
                                                // bridgeIn below is its lower half) — a 2-tall doorway
        m.put(new BlockPos(2, 2, 3), Blocks.CHEST.defaultBlockState().setValue(BlockStateProperties.HORIZONTAL_FACING, Direction.EAST));
        bes.put(new BlockPos(2, 2, 3), lootChest("minecraft:chests/end_city_treasure"));
        m.put(new BlockPos(3, 2, 5), ROD);
        bridgeIn(m, bes, 6, 1, 3, FrontAndTop.EAST_UP);     // the bridge lands on the east wall
        upJig(m, bes, 3, 7, 3);                             // stacks its own tiers
        return new Built(m, bes);
    }
}
