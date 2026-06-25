package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.FrontAndTop;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.BedBlock;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.DoorBlock;
import net.minecraft.world.level.block.JigsawBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BedPart;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;
import net.minecraft.world.level.block.state.properties.DoubleBlockHalf;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * Trade Post — a real little jigsaw village (SKYJIGSAWPLAN Phase 1). A solid {@code square} start piece radiates a
 * {@code streets} pool ({@code straight}/{@code corner} + an empty terminator) that branches and twists, and the
 * streets hang {@code lot}s off their sides from a {@code lots} pool (the five trade shops; the surplus beyond the
 * shop cap is re-stamped from {@code fillers} = a {@code wheat_field}/{@code garden}). The street pieces bake no
 * floor: each lays {@link PathSurfacer#MARKER} markers, so {@link PathSurfacer} renders the lanes as terrain-aware
 * dirt paths and self-railing wooden bridges over the void. A villager spawns at every shop bed.
 *
 * <p>The piece set is built from a {@link Palette} so each biome gets its own. The shapes are shared (near-copies)
 * for now, but generating a distinct set per biome — rather than recolouring one — is what lets the building
 * <em>details</em> (flat sandstone roofs, snow caps, …) diverge per biome later. {@link #PLAINS} reproduces the
 * original oak/cobblestone pieces exactly; {@link #DESERT} is the first variant (sand/sandstone).
 */
public final class TradePostTemplates {
    private TradePostTemplates() {}

    /** The pool namespace prefix + the material set a trade-post piece set is built from. */
    public record Palette(String pool, Block wall, Block post, Block stairs, Block slab, Block door,
                          Block foundation, Block glass, Block fence, Block fieldBorder) {}

    public static final Palette PLAINS = new Palette("skyseed:trade_post", Blocks.OAK_PLANKS, Blocks.OAK_LOG,
            Blocks.OAK_STAIRS, Blocks.OAK_SLAB, Blocks.OAK_DOOR, Blocks.COBBLESTONE, Blocks.GLASS,
            Blocks.OAK_FENCE, Blocks.DIRT);
    public static final Palette DESERT = new Palette("skyseed:trade_post_desert", Blocks.SMOOTH_SANDSTONE,
            Blocks.CUT_SANDSTONE, Blocks.SANDSTONE_STAIRS, Blocks.SANDSTONE_SLAB, Blocks.OAK_DOOR,
            Blocks.SANDSTONE, Blocks.GLASS, Blocks.OAK_FENCE, Blocks.SAND);
    public static final Palette SAVANNA = new Palette("skyseed:trade_post_savanna", Blocks.ACACIA_PLANKS,
            Blocks.ACACIA_LOG, Blocks.ACACIA_STAIRS, Blocks.ACACIA_SLAB, Blocks.ACACIA_DOOR,
            Blocks.COBBLESTONE, Blocks.GLASS, Blocks.ACACIA_FENCE, Blocks.DIRT);
    /** Spruce set shared by the taiga and snowy overrides (they currently diverge only by island surface). */
    public static final Palette SPRUCE = new Palette("skyseed:trade_post_spruce", Blocks.SPRUCE_PLANKS,
            Blocks.SPRUCE_LOG, Blocks.SPRUCE_STAIRS, Blocks.SPRUCE_SLAB, Blocks.SPRUCE_DOOR,
            Blocks.COBBLESTONE, Blocks.GLASS, Blocks.SPRUCE_FENCE, Blocks.DIRT);

    public static void generateInto(Path dir, Palette p) throws IOException {
        writeIfAbsent(dir.resolve("square.nbt"), square(p));
        writeIfAbsent(dir.resolve("street_straight.nbt"), streetSegment(p,
                new FrontAndTop[]{FrontAndTop.WEST_UP, FrontAndTop.EAST_UP},
                new FrontAndTop[]{FrontAndTop.NORTH_UP, FrontAndTop.SOUTH_UP}));
        writeIfAbsent(dir.resolve("street_corner.nbt"), streetSegment(p,
                new FrontAndTop[]{FrontAndTop.WEST_UP, FrontAndTop.SOUTH_UP},
                new FrontAndTop[]{FrontAndTop.NORTH_UP}));
        // No 4-way cross piece on purpose: crossings pack parallel streets only 3 apart, so the 5-wide lots
        // between them overlap and get rejected. Straight + corner runs keep open space along the sides for lots.
        // Each profession gets a distinct building: roof shape + a profession feature, the blacksmith set well apart.
        writeIfAbsent(dir.resolve("shop_farmer.nbt"),
                shop(p, new ShopDesign(Blocks.COMPOSTER.defaultBlockState(), Roof.GABLE, Feature.NONE)));
        writeIfAbsent(dir.resolve("shop_librarian.nbt"),
                shop(p, new ShopDesign(Blocks.LECTERN.defaultBlockState(), Roof.STEPPED, Feature.BOOKS)));
        writeIfAbsent(dir.resolve("shop_fisherman.nbt"),
                shop(p, new ShopDesign(Blocks.BARREL.defaultBlockState(), Roof.GABLE, Feature.NONE)));
        writeIfAbsent(dir.resolve("shop_fletcher.nbt"),
                shop(p, new ShopDesign(Blocks.FLETCHING_TABLE.defaultBlockState(), Roof.FLAT, Feature.HAY)));
        writeIfAbsent(dir.resolve("shop_toolsmith.nbt"),
                shop(p, new ShopDesign(Blocks.SMITHING_TABLE.defaultBlockState(), Roof.FLAT, Feature.FORGE)));
        writeIfAbsent(dir.resolve("wheat_field.nbt"), wheatField(p));
        writeIfAbsent(dir.resolve("garden.nbt"), garden(p));
    }

    /** 7×7 solid village square: the island anchor + a lantern, and four outward street connectors. */
    private static Built square(Palette p) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState base = p.foundation().defaultBlockState();
        for (int x = 0; x <= 6; x++) {
            for (int z = 0; z <= 6; z++) {
                m.put(new BlockPos(x, 0, z), base);
            }
        }
        final String floor = id(p.foundation());
        streetConn(m, bes, new BlockPos(3, 0, 0), FrontAndTop.NORTH_UP, p, floor);
        streetConn(m, bes, new BlockPos(3, 0, 6), FrontAndTop.SOUTH_UP, p, floor);
        streetConn(m, bes, new BlockPos(0, 0, 3), FrontAndTop.WEST_UP, p, floor);
        streetConn(m, bes, new BlockPos(6, 0, 3), FrontAndTop.EAST_UP, p, floor);
        m.put(new BlockPos(3, 1, 3), Blocks.LANTERN.defaultBlockState());
        anchor(m, bes, new BlockPos(3, 0, 3), floor); // last, so the floor loop can't clobber it
        return new Built(m, bes);
    }

    /** A 3×3 marker street deck with through-connectors on {@code streetSides} and lot connectors on {@code lotSides}. */
    private static Built streetSegment(Palette p, FrontAndTop[] streetSides, FrontAndTop[] lotSides) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState wool = PathSurfacer.MARKER.defaultBlockState();
        for (int x = 0; x <= 2; x++) {
            for (int z = 0; z <= 2; z++) {
                m.put(new BlockPos(x, 1, z), wool); // a path marker one block above every deck tile
            }
        }
        for (final FrontAndTop s : streetSides) {
            streetConn(m, bes, edge(s), s, p, "minecraft:air");
        }
        for (final FrontAndTop s : lotSides) {
            conn(m, bes, edge(s), s, "skyseed:lot", "skyseed:lot_door", p.pool() + "/lots", "minecraft:air");
        }
        return new Built(m, bes);
    }

    /** A profession building's look on the shared 5×5 shell: a roof shape + a feature, so each shop reads differently. */
    private enum Roof { GABLE, FLAT, STEPPED }
    private enum Feature { NONE, FORGE, BOOKS, HAY }
    private record ShopDesign(BlockState jobSite, Roof roof, Feature feature) {}

    /**
     * A 5×5 shop: a bed and the job-site inside, a door + lot connector on the −Z wall facing the street, and a
     * per-profession {@code roof} (gable cottage / flat / stepped) and {@code feature} (a forge with a stone front +
     * chimney, a library of bookshelves, a hay store) so a farmer, a librarian and a blacksmith read differently.
     */
    private static Built shop(Palette p, ShopDesign d) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState wall = p.wall().defaultBlockState();
        final BlockState post = p.post().defaultBlockState();
        final BlockState stone = p.foundation().defaultBlockState();
        final BlockState glass = p.glass().defaultBlockState();
        final int n = 5, max = 4, mid = 2;
        final boolean forge = d.feature() == Feature.FORGE;
        for (int x = 0; x < n; x++) {
            for (int z = 0; z < n; z++) {
                m.put(new BlockPos(x, 0, z), wall);
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                final boolean corner = (x == 0 || x == max) && (z == 0 || z == max);
                if (perim) {
                    for (int h = 1; h <= 3; h++) {
                        // the forge faces its lowest course in stone (a smithy look); corners stay posts
                        m.put(new BlockPos(x, h, z), corner ? post : (forge && h == 1 ? stone : wall));
                    }
                }
                m.put(new BlockPos(x, 4, z), wall); // a flat ceiling; the roof switch builds on top of it
            }
        }
        // Door + lot connector on the −Z wall (faces the street once the jigsaw rotates the piece into place).
        m.put(new BlockPos(mid, 1, 0), p.door().defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.LOWER).setValue(DoorBlock.FACING, Direction.NORTH));
        m.put(new BlockPos(mid, 2, 0), p.door().defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.UPPER).setValue(DoorBlock.FACING, Direction.NORTH));
        conn(m, bes, new BlockPos(mid, 0, 0), FrontAndTop.NORTH_UP, "skyseed:lot_door", "skyseed:lot",
                "minecraft:empty", id(p.wall()));
        m.put(new BlockPos(0, 2, mid), glass);
        m.put(new BlockPos(max, 2, mid), glass);
        m.put(new BlockPos(mid, 2, max), glass);
        m.put(new BlockPos(1, 1, 2), Blocks.RED_BED.defaultBlockState()
                .setValue(BedBlock.PART, BedPart.FOOT).setValue(BedBlock.FACING, Direction.SOUTH));
        m.put(new BlockPos(1, 1, 3), Blocks.RED_BED.defaultBlockState()
                .setValue(BedBlock.PART, BedPart.HEAD).setValue(BedBlock.FACING, Direction.SOUTH));
        m.put(new BlockPos(3, 1, 3), d.jobSite());
        m.put(new BlockPos(3, 1, 1), Blocks.TORCH.defaultBlockState());
        roof(m, p, d.roof(), wall);
        feature(m, d.feature(), stone);
        return new Built(m, bes);
    }

    /** Build the per-design roof on top of the y4 ceiling. */
    private static void roof(Map<BlockPos, BlockState> m, Palette p, Roof roof, BlockState wall) {
        final int max = 4;
        switch (roof) {
            case GABLE -> StructureParts.gableRoof(m, 0, max, 0, max, 4, wall, p.stairs(), p.slab(), 0);
            case FLAT -> {
                final BlockState slab = p.slab().defaultBlockState();
                for (int x = 0; x <= max; x++) {
                    for (int z = 0; z <= max; z++) {
                        if (x == 0 || x == max || z == 0 || z == max) {
                            m.put(new BlockPos(x, 5, z), slab); // a low parapet ringing the flat roof
                        }
                    }
                }
            }
            case STEPPED -> { // a stepped pyramid: the 5×5 ceiling, then a 3×3, then a 1×1 cap
                for (int x = 1; x <= 3; x++) {
                    for (int z = 1; z <= 3; z++) {
                        m.put(new BlockPos(x, 5, z), wall);
                    }
                }
                m.put(new BlockPos(2, 6, 2), wall);
            }
        }
    }

    /** Add a per-profession feature inside / on the building. */
    private static void feature(Map<BlockPos, BlockState> m, Feature feature, BlockState stone) {
        switch (feature) {
            case FORGE -> { // a furnace forge with a stone chimney up through the roof
                m.put(new BlockPos(1, 1, 1), Blocks.FURNACE.defaultBlockState());
                m.put(new BlockPos(1, 4, 1), stone);
                m.put(new BlockPos(1, 5, 1), stone);
                m.put(new BlockPos(1, 6, 1), stone);
            }
            case BOOKS -> { // a small library against the side wall
                m.put(new BlockPos(3, 1, 2), Blocks.BOOKSHELF.defaultBlockState());
                m.put(new BlockPos(3, 2, 2), Blocks.BOOKSHELF.defaultBlockState());
                m.put(new BlockPos(2, 2, 3), Blocks.BOOKSHELF.defaultBlockState());
            }
            case HAY -> { // a hay store
                m.put(new BlockPos(2, 1, 1), Blocks.HAY_BLOCK.defaultBlockState());
                m.put(new BlockPos(3, 1, 2), Blocks.HAY_BLOCK.defaultBlockState());
            }
            case NONE -> { }
        }
    }

    /** A 5×5 fenced wheat field — tilled, watered and grown — with a gate onto the street. No villager (scenery). */
    private static Built wheatField(Palette p) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState border = p.fieldBorder().defaultBlockState();
        final BlockState farmland = Blocks.FARMLAND.defaultBlockState().setValue(BlockStateProperties.MOISTURE, 7);
        final BlockState wheat = Blocks.WHEAT.defaultBlockState().setValue(BlockStateProperties.AGE_7, 7);
        final BlockState fence = p.fence().defaultBlockState();
        final int max = 4, mid = 2;
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                m.put(new BlockPos(x, 0, z), perim ? border : farmland);
            }
        }
        m.put(new BlockPos(mid, 0, mid), Blocks.WATER.defaultBlockState()); // hydrates the field, contained by farmland
        for (int x = 1; x <= 3; x++) {
            for (int z = 1; z <= 3; z++) {
                if (x == mid && z == mid) {
                    continue; // the water tile
                }
                m.put(new BlockPos(x, 1, z), wheat);
            }
        }
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                if (perim && !(x == mid && z == 0)) {
                    m.put(new BlockPos(x, 1, z), fence); // a fence ring with a gate gap on the street side
                }
            }
        }
        conn(m, bes, new BlockPos(mid, 0, 0), FrontAndTop.NORTH_UP, "skyseed:lot_door", "skyseed:lot",
                "minecraft:empty", id(p.fieldBorder()));
        StructureParts.linkFences(m);
        return new Built(m, bes);
    }

    /** A 5×5 flower garden with a central lamp post. No villager (scenery). */
    private static Built garden(Palette p) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int max = 4, mid = 2;
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), Blocks.GRASS_BLOCK.defaultBlockState());
            }
        }
        m.put(new BlockPos(1, 1, 1), Blocks.POPPY.defaultBlockState());
        m.put(new BlockPos(3, 1, 1), Blocks.DANDELION.defaultBlockState());
        m.put(new BlockPos(1, 1, 3), Blocks.AZURE_BLUET.defaultBlockState());
        m.put(new BlockPos(3, 1, 3), Blocks.OXEYE_DAISY.defaultBlockState());
        m.put(new BlockPos(mid, 1, mid), p.fence().defaultBlockState()); // lamp post
        m.put(new BlockPos(mid, 2, mid), Blocks.LANTERN.defaultBlockState());
        conn(m, bes, new BlockPos(mid, 0, 0), FrontAndTop.NORTH_UP, "skyseed:lot_door", "skyseed:lot",
                "minecraft:empty", "minecraft:grass_block");
        return new Built(m, bes);
    }

    /** The edge-midpoint connector position on a 3×3 deck for a given outward facing. */
    private static BlockPos edge(FrontAndTop side) {
        return switch (side) {
            case NORTH_UP -> new BlockPos(1, 0, 0);
            case SOUTH_UP -> new BlockPos(1, 0, 2);
            case WEST_UP -> new BlockPos(0, 0, 1);
            case EAST_UP -> new BlockPos(2, 0, 1);
            default -> throw new IllegalArgumentException("unsupported street side " + side);
        };
    }

    /** A self-linking street connector at {@code pos} facing {@code dir}, drawing from this palette's street pool. */
    private static void streetConn(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockPos pos,
                                   FrontAndTop dir, Palette p, String finalState) {
        conn(m, bes, pos, dir, "skyseed:street", "skyseed:street", p.pool() + "/streets", finalState);
    }

    /** A jigsaw connector block-entity at {@code pos}. */
    private static void conn(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockPos pos,
                             FrontAndTop dir, String name, String target, String pool, String finalState) {
        m.put(pos, Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, dir));
        bes.put(pos, jig(name, target, pool, finalState));
    }

    /** The registry id of a block, for a jigsaw connector's final (replacement) state. */
    private static String id(Block b) {
        return BuiltInRegistries.BLOCK.getKey(b).toString();
    }
}
