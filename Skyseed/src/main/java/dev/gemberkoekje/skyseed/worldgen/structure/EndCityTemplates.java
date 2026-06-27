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
    }

    // ---- shared helpers ----------------------------------------------------------------------------------------

    private static BlockState stair(Direction face) {
        return Blocks.PURPUR_STAIRS.defaultBlockState().setValue(BlockStateProperties.HORIZONTAL_FACING, face);
    }

    /** A vertical jigsaw uses the {@code aligned} joint (not {@code rollable}, which is for rolling around a horizontal
     *  axis): the parent's UP and the child's DOWN connector share the {@code north} top vector, so they mate with no
     *  spin. {@link StructureParts#jig} bakes {@code rollable}; override it here. */
    private static CompoundTag jigAligned(String name, String target) {
        final CompoundTag t = jig(name, target, FLOOR_POOL, "minecraft:purpur_block");
        t.putString("joint", "aligned");
        return t;
    }

    /** An up-facing jigsaw at {@code (x,y,z)} that stacks the next tier/roof on top (becomes purpur if nothing rolls). */
    private static void upJig(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int y, int z) {
        m.put(new BlockPos(x, y, z), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.UP_NORTH));
        bes.put(new BlockPos(x, y, z), jigAligned("skyseed:ec_up", "skyseed:ec_tier"));
    }

    /** A down-facing jigsaw at {@code (x,y,z)} — a tier/roof's underside seat, matching a parent's {@link #upJig}. */
    private static void downJig(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, int x, int y, int z) {
        m.put(new BlockPos(x, y, z), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.DOWN_NORTH));
        bes.put(new BlockPos(x, y, z), jigAligned("skyseed:ec_tier", "skyseed:ec_cap"));
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
}
