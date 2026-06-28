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
import net.minecraft.world.level.block.StairBlock;
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
                          Block foundation, Block glass, Block fence, Block fieldBorder, Block accent) {}

    // Materials mirror the vanilla village houses: a cobblestone foundation, plank walls with a contrasting
    // plaster/accent upper course (the plains white-terracotta look), STRIPPED-log corner posts, glass-PANE windows.
    public static final Palette PLAINS = new Palette("skyseed:trade_post", Blocks.OAK_PLANKS, Blocks.STRIPPED_OAK_LOG,
            Blocks.OAK_STAIRS, Blocks.OAK_SLAB, Blocks.OAK_DOOR, Blocks.COBBLESTONE, Blocks.GLASS_PANE,
            Blocks.OAK_FENCE, Blocks.DIRT, Blocks.WHITE_TERRACOTTA);
    public static final Palette DESERT = new Palette("skyseed:trade_post_desert", Blocks.SMOOTH_SANDSTONE,
            Blocks.CUT_SANDSTONE, Blocks.SANDSTONE_STAIRS, Blocks.SANDSTONE_SLAB, Blocks.OAK_DOOR,
            Blocks.SANDSTONE, Blocks.GLASS_PANE, Blocks.OAK_FENCE, Blocks.SAND, Blocks.CUT_SANDSTONE);
    public static final Palette SAVANNA = new Palette("skyseed:trade_post_savanna", Blocks.ACACIA_PLANKS,
            Blocks.STRIPPED_ACACIA_LOG, Blocks.ACACIA_STAIRS, Blocks.ACACIA_SLAB, Blocks.ACACIA_DOOR,
            Blocks.COBBLESTONE, Blocks.GLASS_PANE, Blocks.ACACIA_FENCE, Blocks.DIRT, Blocks.ACACIA_PLANKS);
    /** Spruce set shared by the taiga and snowy overrides (they currently diverge only by island surface). */
    public static final Palette SPRUCE = new Palette("skyseed:trade_post_spruce", Blocks.SPRUCE_PLANKS,
            Blocks.STRIPPED_SPRUCE_LOG, Blocks.SPRUCE_STAIRS, Blocks.SPRUCE_SLAB, Blocks.SPRUCE_DOOR,
            Blocks.COBBLESTONE, Blocks.GLASS_PANE, Blocks.SPRUCE_FENCE, Blocks.DIRT, Blocks.SPRUCE_PLANKS);

    public static void generateInto(Path dir, Palette p) throws IOException {
        // Two street skeletons sharing the same lot/building pools, differing only in which streets pool their lanes
        // recurse into: the normal set (the trade post) and a "_dense" set that recurses into "streets_dense", which
        // weights the large (big-building) section higher — so the village_center, pointed at start_dense, reads as
        // having more of the bigger buildings. See street_straight/large below for the lot spacing rationale.
        writeStreetSkeleton(dir, p, p.pool() + "/streets", "");
        writeStreetSkeleton(dir, p, p.pool() + "/streets_dense", "_dense");
        // Each profession gets a distinct building: roof shape + a profession feature, the blacksmith set well apart.
        writeIfAbsent(dir.resolve("shop_farmer.nbt"),
                shop(p, new ShopDesign(Blocks.COMPOSTER.defaultBlockState(), Roof.GABLE, Feature.NONE)));
        writeIfAbsent(dir.resolve("shop_librarian.nbt"),
                shop(p, new ShopDesign(Blocks.LECTERN.defaultBlockState(), Roof.STEPPED, Feature.BOOKS)));
        writeIfAbsent(dir.resolve("shop_fisherman.nbt"),
                shop(p, new ShopDesign(Blocks.BARREL.defaultBlockState(), Roof.GABLE, Feature.NONE)));
        writeIfAbsent(dir.resolve("shop_fletcher.nbt"),
                shop(p, new ShopDesign(Blocks.FLETCHING_TABLE.defaultBlockState(), Roof.FLAT, Feature.HAY)));
        // More professions for variety — each a distinct job-site + roof + feature on the shared 7×7 shell.
        writeIfAbsent(dir.resolve("shop_butcher.nbt"),
                shop(p, new ShopDesign(Blocks.SMOKER.defaultBlockState(), Roof.GABLE, Feature.HAY)));
        writeIfAbsent(dir.resolve("shop_shepherd.nbt"),
                shop(p, new ShopDesign(Blocks.LOOM.defaultBlockState(), Roof.FLAT, Feature.WOOL)));
        writeIfAbsent(dir.resolve("shop_mason.nbt"),
                shop(p, new ShopDesign(Blocks.STONECUTTER.defaultBlockState(), Roof.STEPPED, Feature.NONE)));
        writeIfAbsent(dir.resolve("shop_cartographer.nbt"),
                shop(p, new ShopDesign(Blocks.CARTOGRAPHY_TABLE.defaultBlockState(), Roof.GABLE, Feature.BOOKS)));
        // The blacksmith is named "forge" (not "shop_") so the shop cap leaves it alone — it's a feature building
        // that a large section hosts, not one of the capped 2–4 small shops.
        writeIfAbsent(dir.resolve("forge.nbt"), blacksmith(p));
        // A second large-section landmark alongside the forge: a tall open meeting hall with a bell.
        writeIfAbsent(dir.resolve("great_hall.nbt"), greatHall(p));
        // Three crop fields for variety (wheat / potato / carrot), all the same fenced, water-hydrated 5×5 plot.
        writeIfAbsent(dir.resolve("wheat_field.nbt"),
                field(p, Blocks.WHEAT.defaultBlockState().setValue(BlockStateProperties.AGE_7, 7)));
        writeIfAbsent(dir.resolve("potato_field.nbt"),
                field(p, Blocks.POTATOES.defaultBlockState().setValue(BlockStateProperties.AGE_7, 7)));
        writeIfAbsent(dir.resolve("carrot_field.nbt"),
                field(p, Blocks.CARROTS.defaultBlockState().setValue(BlockStateProperties.AGE_7, 7)));
        writeIfAbsent(dir.resolve("garden.nbt"), garden(p));
        // More scenery plots for the surplus-lot fillers: a village well, an animal pen, a market stall.
        writeIfAbsent(dir.resolve("well.nbt"), well(p));
        writeIfAbsent(dir.resolve("animal_pen.nbt"), animalPen(p));
        writeIfAbsent(dir.resolve("market_stall.nbt"), marketStall(p));
        // A tiny lamp-post plot, the lots' fallback: a lot too tight for a shop/field gets this instead of a bare gap.
        writeIfAbsent(dir.resolve("terminator.nbt"), terminator(p));
        // A plank pier for lots that land over the void — used by the _void filler pool instead of a floating farm.
        writeIfAbsent(dir.resolve("pier.nbt"), pier(p));
        // A tiny hamlet green that reuses this set's shops — the Hamlet theme starts from it (see hamlet/start pool).
        writeIfAbsent(dir.resolve("hamlet_hub.nbt"), hamletHub(p));
    }

    /**
     * Write one street skeleton (the square + the three street sections) whose lanes recurse into {@code streetsPool},
     * suffixing each file with {@code suffix}. The lot connectors still draw from this palette's shared lot/building
     * pools, so both skeletons place the same buildings — they differ only in how often a large section comes up.
     *
     * <p>A 7-long straight spaces lots ~7 apart so the wider 7×7 shops fit side by side; corners are pure turns (a 7×7
     * shop on a tight 3×3 corner would just overlap its neighbours). No 4-way cross: parallel streets 3 apart would
     * make the 5-wide lots between them overlap. The large section's single, isolated lot draws the large_lots pool.
     */
    private static void writeStreetSkeleton(Path dir, Palette p, String streetsPool, String suffix) throws IOException {
        writeIfAbsent(dir.resolve("square" + suffix + ".nbt"), square(p, streetsPool));
        writeIfAbsent(dir.resolve("street_straight" + suffix + ".nbt"), straightStreet(p, streetsPool));
        writeIfAbsent(dir.resolve("street_corner" + suffix + ".nbt"), streetSegment(p, streetsPool,
                new FrontAndTop[]{FrontAndTop.WEST_UP, FrontAndTop.SOUTH_UP}, new FrontAndTop[]{}));
        writeIfAbsent(dir.resolve("street_large" + suffix + ".nbt"), largeStreet(p, streetsPool));
    }

    /**
     * A 3×3 hamlet green: a lamp post and three lot connectors that pull this palette's {@code lots} pool — i.e. the
     * very same profession shops the trade post uses. The Hamlet theme starts from this hub (capped to 1–2 shops),
     * so a hamlet shows the trade post's building diversity in miniature, in the biome's materials.
     */
    private static Built hamletHub(Palette p) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        for (int x = 0; x <= 2; x++) {
            for (int z = 0; z <= 2; z++) {
                m.put(new BlockPos(x, 0, z), Blocks.GRASS_BLOCK.defaultBlockState());
            }
        }
        conn(m, bes, new BlockPos(1, 0, 0), FrontAndTop.NORTH_UP, "skyseed:lot", "skyseed:lot_door",
                p.pool() + "/lots", "minecraft:grass_block");
        conn(m, bes, new BlockPos(0, 0, 1), FrontAndTop.WEST_UP, "skyseed:lot", "skyseed:lot_door",
                p.pool() + "/lots", "minecraft:grass_block");
        conn(m, bes, new BlockPos(2, 0, 1), FrontAndTop.EAST_UP, "skyseed:lot", "skyseed:lot_door",
                p.pool() + "/lots", "minecraft:grass_block");
        m.put(new BlockPos(1, 1, 1), p.fence().defaultBlockState());      // a lamp post on the green
        m.put(new BlockPos(1, 2, 1), Blocks.LANTERN.defaultBlockState());
        anchor(m, bes, new BlockPos(1, 0, 1), "minecraft:grass_block"); // the start's bottom anchor, under the lamp
        return new Built(m, bes);
    }

    /** 7×7 solid village square: the island anchor + a lantern, and four outward street connectors into {@code streetsPool}. */
    private static Built square(Palette p, String streetsPool) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState base = p.foundation().defaultBlockState();
        for (int x = 0; x <= 6; x++) {
            for (int z = 0; z <= 6; z++) {
                m.put(new BlockPos(x, 0, z), base);
            }
        }
        final String floor = id(p.foundation());
        streetConn(m, bes, new BlockPos(3, 0, 0), FrontAndTop.NORTH_UP, streetsPool, floor);
        streetConn(m, bes, new BlockPos(3, 0, 6), FrontAndTop.SOUTH_UP, streetsPool, floor);
        streetConn(m, bes, new BlockPos(0, 0, 3), FrontAndTop.WEST_UP, streetsPool, floor);
        streetConn(m, bes, new BlockPos(6, 0, 3), FrontAndTop.EAST_UP, streetsPool, floor);
        m.put(new BlockPos(3, 1, 3), Blocks.LANTERN.defaultBlockState());
        anchor(m, bes, new BlockPos(3, 0, 3), floor); // last, so the floor loop can't clobber it
        return new Built(m, bes);
    }

    /** A 3×3 marker street deck with through-connectors on {@code streetSides} and lot connectors on {@code lotSides}. */
    private static Built streetSegment(Palette p, String streetsPool, FrontAndTop[] streetSides, FrontAndTop[] lotSides) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState wool = PathSurfacer.MARKER.defaultBlockState();
        for (int x = 0; x <= 2; x++) {
            for (int z = 0; z <= 2; z++) {
                m.put(new BlockPos(x, 1, z), wool); // a path marker one block above every deck tile
            }
        }
        for (final FrontAndTop s : streetSides) {
            streetConn(m, bes, edge(s), s, streetsPool, "minecraft:air");
        }
        for (final FrontAndTop s : lotSides) {
            conn(m, bes, edge(s), s, "skyseed:lot", "skyseed:lot_door", p.pool() + "/lots", "minecraft:air");
        }
        return new Built(m, bes);
    }

    /**
     * A 3×7 "large" street section: the lane runs through its two ends, and a single lot connector sits at the
     * isolated middle of its long side, drawing from the {@code large_lots} pool. Because the section is 7 long, its
     * lot is well clear of the neighbouring sections' lots, so a bigger building (the L-shaped forge) has room to
     * spread along the lane without overlapping anything.
     */
    private static Built largeStreet(Palette p, String streetsPool) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState wool = PathSurfacer.MARKER.defaultBlockState();
        for (int x = 0; x <= 2; x++) {
            for (int z = 0; z <= 6; z++) {
                m.put(new BlockPos(x, 1, z), wool); // a 3×7 path-marker deck
            }
        }
        streetConn(m, bes, new BlockPos(1, 0, 0), FrontAndTop.NORTH_UP, streetsPool, "minecraft:air"); // lane continues
        streetConn(m, bes, new BlockPos(1, 0, 6), FrontAndTop.SOUTH_UP, streetsPool, "minecraft:air");
        conn(m, bes, new BlockPos(2, 0, 3), FrontAndTop.EAST_UP, "skyseed:lot", "skyseed:lot_door",
                p.pool() + "/large_lots", "minecraft:air"); // one large lot at the isolated middle
        return new Built(m, bes);
    }

    /**
     * A 7-long straight street (was a 3×3 segment): the lane runs through its two ends, and a lot connector sits on
     * each long side at the centre. Because the section is 7 long, neighbouring sections' lots are ~7 apart — clear
     * enough for the wider 7×7 shops to sit side by side along the lane without overlapping (vanilla streets space
     * houses out the same way). Draws from the regular {@code lots} pool.
     */
    private static Built straightStreet(Palette p, String streetsPool) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState wool = PathSurfacer.MARKER.defaultBlockState();
        for (int x = 0; x <= 6; x++) {
            for (int z = 0; z <= 2; z++) {
                m.put(new BlockPos(x, 1, z), wool); // a 7×3 path-marker deck
            }
        }
        streetConn(m, bes, new BlockPos(0, 0, 1), FrontAndTop.WEST_UP, streetsPool, "minecraft:air");
        streetConn(m, bes, new BlockPos(6, 0, 1), FrontAndTop.EAST_UP, streetsPool, "minecraft:air");
        conn(m, bes, new BlockPos(3, 0, 0), FrontAndTop.NORTH_UP, "skyseed:lot", "skyseed:lot_door",
                p.pool() + "/lots", "minecraft:air");
        conn(m, bes, new BlockPos(3, 0, 2), FrontAndTop.SOUTH_UP, "skyseed:lot", "skyseed:lot_door",
                p.pool() + "/lots", "minecraft:air");
        return new Built(m, bes);
    }

    /** A profession building's look on the shared 7×7 shell: a roof shape + a feature, so each shop reads differently. */
    private enum Roof { GABLE, FLAT, STEPPED }
    private enum Feature { NONE, FORGE, BOOKS, HAY, WOOL }
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
        final BlockState accent = p.accent().defaultBlockState();
        // 7×7 shell (was 5×5): vanilla's smallest village house is ~7×7, and the wider streets now space lots far
        // enough apart for it. max = the far wall index, mid = the centred door/window column.
        final int n = 7, max = 6, mid = 3;
        for (int x = 0; x < n; x++) {
            for (int z = 0; z < n; z++) {
                m.put(new BlockPos(x, 0, z), wall);
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                final boolean corner = (x == 0 || x == max) && (z == 0 || z == max);
                if (perim) {
                    for (int h = 1; h <= 3; h++) {
                        // the vanilla village-house timber frame: a cobblestone foundation course (h1), a plank lower
                        // wall (h2), a plaster/accent upper course (h3), and stripped-log corner posts the full height
                        m.put(new BlockPos(x, h, z), corner ? post : (h == 1 ? stone : h == 3 ? accent : wall));
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
        m.put(new BlockPos(0, 2, mid), glass);       // a window centred on each of the three non-door walls
        m.put(new BlockPos(max, 2, mid), glass);
        m.put(new BlockPos(mid, 2, max), glass);
        m.put(new BlockPos(1, 1, max - 2), Blocks.RED_BED.defaultBlockState()  // bed in the back-left corner
                .setValue(BedBlock.PART, BedPart.FOOT).setValue(BedBlock.FACING, Direction.SOUTH));
        m.put(new BlockPos(1, 1, max - 1), Blocks.RED_BED.defaultBlockState()
                .setValue(BedBlock.PART, BedPart.HEAD).setValue(BedBlock.FACING, Direction.SOUTH));
        m.put(new BlockPos(max - 1, 1, max - 1), d.jobSite()); // the job site in the back-right corner
        m.put(new BlockPos(1, 1, 1), Blocks.TORCH.defaultBlockState());
        roof(m, p, d.roof(), wall, max);
        feature(m, d.feature(), stone, max);
        return new Built(m, bes);
    }

    /** Build the per-design roof on top of the y4 ceiling of a {@code max+1}-wide shell. */
    private static void roof(Map<BlockPos, BlockState> m, Palette p, Roof roof, BlockState wall, int max) {
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
            case STEPPED -> { // a stepped ziggurat: filled layers shrinking by one ring each level up to the cap
                for (int lo = 1, hi = max - 1, y = 5; lo <= hi; lo++, hi--, y++) {
                    for (int x = lo; x <= hi; x++) {
                        for (int z = lo; z <= hi; z++) {
                            m.put(new BlockPos(x, y, z), wall);
                        }
                    }
                }
            }
        }
    }

    /** Add a per-profession feature inside a {@code max+1}-wide shell, set against the back-right wall. */
    private static void feature(Map<BlockPos, BlockState> m, Feature feature, BlockState stone, int max) {
        switch (feature) {
            case FORGE -> { // a furnace forge with a stone chimney up through the roof
                m.put(new BlockPos(1, 1, 1), Blocks.FURNACE.defaultBlockState());
                for (int y = 4; y <= max; y++) {
                    m.put(new BlockPos(1, y, 1), stone);
                }
            }
            case BOOKS -> { // a small library against the side wall
                m.put(new BlockPos(max - 1, 1, 2), Blocks.BOOKSHELF.defaultBlockState());
                m.put(new BlockPos(max - 1, 2, 2), Blocks.BOOKSHELF.defaultBlockState());
                m.put(new BlockPos(max - 1, 1, 3), Blocks.BOOKSHELF.defaultBlockState());
            }
            case HAY -> { // a hay store
                m.put(new BlockPos(max - 1, 1, 1), Blocks.HAY_BLOCK.defaultBlockState());
                m.put(new BlockPos(max - 1, 1, 2), Blocks.HAY_BLOCK.defaultBlockState());
            }
            case WOOL -> { // a shepherd's wool store
                m.put(new BlockPos(max - 1, 1, 1), Blocks.WHITE_WOOL.defaultBlockState());
                m.put(new BlockPos(max - 1, 2, 1), Blocks.WHITE_WOOL.defaultBlockState());
                m.put(new BlockPos(max - 1, 1, 2), Blocks.LIGHT_GRAY_WOOL.defaultBlockState());
            }
            case NONE -> { }
        }
    }

    /**
     * A great hall — a tall, open meeting house (5 wide × 9 deep) for the large sections: higher walls than a shop, a
     * steep gable, benches down both sides of a central aisle and a bell at the head. A second big-building option
     * alongside the forge, so a large lot reads as a real landmark. Stays 5 wide (the large section's clear span, same
     * as the forge); the extra size is depth + height.
     */
    private static Built greatHall(Palette p) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState wall = p.wall().defaultBlockState();
        final BlockState post = p.post().defaultBlockState();
        final BlockState stone = p.foundation().defaultBlockState();
        final BlockState glass = p.glass().defaultBlockState();
        final int mx = 4, mz = 8, mid = 2; // 5 wide (X 0-4) × 9 deep (Z 0-8)
        for (int x = 0; x <= mx; x++) {
            for (int z = 0; z <= mz; z++) {
                m.put(new BlockPos(x, 0, z), stone); // a stone footing
                final boolean perim = x == 0 || x == mx || z == 0 || z == mz;
                final boolean corner = (x == 0 || x == mx) && (z == 0 || z == mz);
                if (perim) {
                    for (int h = 1; h <= 5; h++) { // tall walls: stone foundation course, planks above, log corners
                        m.put(new BlockPos(x, h, z), corner ? post : (h == 1 ? stone : wall));
                    }
                }
                m.put(new BlockPos(x, 6, z), wall); // ceiling
            }
        }
        StructureParts.gableRoof(m, 0, mx, 0, mz, 6, wall, p.stairs(), p.slab(), 0); // a steep gable over the tall walls
        // Door + lot connector on the −Z wall (faces the lane once rotated into place).
        m.put(new BlockPos(mid, 1, 0), p.door().defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.LOWER).setValue(DoorBlock.FACING, Direction.NORTH));
        m.put(new BlockPos(mid, 2, 0), p.door().defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.UPPER).setValue(DoorBlock.FACING, Direction.NORTH));
        conn(m, bes, new BlockPos(mid, 0, 0), FrontAndTop.NORTH_UP, "skyseed:lot_door", "skyseed:lot",
                "minecraft:empty", id(p.wall()));
        // tall windows down both long walls
        for (int z = 2; z <= mz - 2; z += 2) {
            m.put(new BlockPos(0, 2, z), glass);
            m.put(new BlockPos(0, 3, z), glass);
            m.put(new BlockPos(mx, 2, z), glass);
            m.put(new BlockPos(mx, 3, z), glass);
        }
        // benches (upside-down... bottom stairs) down each side, facing the central aisle
        for (int z = 2; z <= mz - 2; z++) {
            m.put(new BlockPos(1, 1, z), p.stairs().defaultBlockState().setValue(StairBlock.FACING, Direction.EAST));
            m.put(new BlockPos(mx - 1, 1, z), p.stairs().defaultBlockState().setValue(StairBlock.FACING, Direction.WEST));
        }
        // a bell at the head of the hall, flanked by lanterns
        m.put(new BlockPos(mid, 1, mz - 1), Blocks.BELL.defaultBlockState());
        m.put(new BlockPos(1, 1, mz - 1), Blocks.LANTERN.defaultBlockState());
        m.put(new BlockPos(mx - 1, 1, mz - 1), Blocks.LANTERN.defaultBlockState());
        return new Built(m, bes);
    }

    /** The forge's footprint: an L (a 5-wide × 3-deep front wing + a 3-wide × 7-deep left wing); the back-right
     *  notch (x≥3, z≥3) is left open as the patio. {@code false} for anything outside the 5×7 footprint. */
    private static boolean forgeRoom(int x, int z) {
        return x >= 0 && x <= 4 && z >= 0 && z <= 6 && (x <= 2 || z <= 2);
    }

    /**
     * The blacksmith — a deliberately bigger, L-shaped building (5 wide × 7 deep) to test the jigsaw with larger
     * footprints. The roofed forge is an L with a stone-faced base, a furnace and a chimney; the back-right notch is
     * an open cobblestone patio with an anvil and a fence railing. Stays 5 wide (so it fits between lots ~6 apart on a
     * street) but runs deeper, into the open space away from the lane. Built from the palette like the other shops.
     */
    private static Built blacksmith(Palette p) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState wall = p.wall().defaultBlockState();
        final BlockState stone = p.foundation().defaultBlockState();
        final BlockState slab = p.slab().defaultBlockState();
        final BlockState glass = p.glass().defaultBlockState();
        for (int x = 0; x <= 4; x++) {
            for (int z = 0; z <= 6; z++) {
                if (!forgeRoom(x, z)) {
                    m.put(new BlockPos(x, 0, z), stone); // the open cobblestone patio in the notch
                    continue;
                }
                m.put(new BlockPos(x, 0, z), wall);  // room floor
                m.put(new BlockPos(x, 4, z), wall);  // flat ceiling; the roof sits on it
                boolean edge = false;
                for (final Direction d : Direction.Plane.HORIZONTAL) {
                    if (!forgeRoom(x + d.getStepX(), z + d.getStepZ())) {
                        edge = true;
                        break;
                    }
                }
                if (edge) {
                    m.put(new BlockPos(x, 1, z), stone); // a stone base course — a smithy look
                    m.put(new BlockPos(x, 2, z), wall);
                    m.put(new BlockPos(x, 3, z), wall);
                    m.put(new BlockPos(x, 5, z), slab);  // a low parapet around the flat roof
                }
            }
        }
        // Door + lot connector on the front-centre wall (faces the street once the jigsaw rotates the piece).
        m.put(new BlockPos(2, 1, 0), p.door().defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.LOWER).setValue(DoorBlock.FACING, Direction.NORTH));
        m.put(new BlockPos(2, 2, 0), p.door().defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.UPPER).setValue(DoorBlock.FACING, Direction.NORTH));
        conn(m, bes, new BlockPos(2, 0, 0), FrontAndTop.NORTH_UP, "skyseed:lot_door", "skyseed:lot",
                "minecraft:empty", id(p.wall()));
        m.put(new BlockPos(0, 2, 1), glass);
        m.put(new BlockPos(0, 2, 4), glass);
        // The forge: a furnace with a stone chimney up through the roof.
        m.put(new BlockPos(3, 1, 1), Blocks.FURNACE.defaultBlockState());
        m.put(new BlockPos(3, 4, 1), stone);
        m.put(new BlockPos(3, 5, 1), stone);
        m.put(new BlockPos(3, 6, 1), stone);
        m.put(new BlockPos(1, 2, 1), Blocks.TORCH.defaultBlockState());
        // A bed in the left wing and the smithing table.
        m.put(new BlockPos(1, 1, 4), Blocks.RED_BED.defaultBlockState()
                .setValue(BedBlock.PART, BedPart.FOOT).setValue(BedBlock.FACING, Direction.SOUTH));
        m.put(new BlockPos(1, 1, 5), Blocks.RED_BED.defaultBlockState()
                .setValue(BedBlock.PART, BedPart.HEAD).setValue(BedBlock.FACING, Direction.SOUTH));
        m.put(new BlockPos(1, 1, 2), Blocks.SMITHING_TABLE.defaultBlockState());
        // The patio: an anvil and a fence railing along its open (right + back) edges.
        m.put(new BlockPos(3, 1, 4), Blocks.ANVIL.defaultBlockState());
        m.put(new BlockPos(4, 1, 4), Blocks.OAK_FENCE.defaultBlockState());
        m.put(new BlockPos(4, 1, 5), Blocks.OAK_FENCE.defaultBlockState());
        m.put(new BlockPos(4, 1, 6), Blocks.OAK_FENCE.defaultBlockState());
        m.put(new BlockPos(3, 1, 6), Blocks.OAK_FENCE.defaultBlockState());
        m.put(new BlockPos(3, 2, 6), Blocks.LANTERN.defaultBlockState());
        StructureParts.linkFences(m);
        return new Built(m, bes);
    }

    /** A 5×5 fenced wheat field — tilled, watered and grown — with a gate onto the street. No villager (scenery). */
    private static Built field(Palette p, BlockState crop) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState border = p.fieldBorder().defaultBlockState();
        final BlockState farmland = Blocks.FARMLAND.defaultBlockState().setValue(BlockStateProperties.MOISTURE, 7);
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
                m.put(new BlockPos(x, 1, z), crop);
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

    /** A 5×5 village well: a cobble (biome foundation) rim around a central water pool, four posts, a slab roof, a lantern. */
    private static Built well(Palette p) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState cobble = p.foundation().defaultBlockState();
        final BlockState fence = p.fence().defaultBlockState();
        final int max = 4, mid = 2;
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), cobble);            // a paved apron
            }
        }
        for (int x = 1; x <= 3; x++) {                            // the well box: a rim around a central water pool
            for (int z = 1; z <= 3; z++) {
                m.put(new BlockPos(x, 1, z), (x == mid && z == mid) ? Blocks.WATER.defaultBlockState() : cobble);
            }
        }
        for (final int[] c : new int[][]{{1, 1}, {3, 1}, {1, 3}, {3, 3}}) {   // corner posts holding a slab roof
            m.put(new BlockPos(c[0], 2, c[1]), fence);
            m.put(new BlockPos(c[0], 3, c[1]), fence);
        }
        for (int x = 1; x <= 3; x++) {
            for (int z = 1; z <= 3; z++) {
                m.put(new BlockPos(x, 4, z), p.slab().defaultBlockState());
            }
        }
        m.put(new BlockPos(mid, 3, mid), Blocks.LANTERN.defaultBlockState().setValue(BlockStateProperties.HANGING, true));
        conn(m, bes, new BlockPos(mid, 0, 0), FrontAndTop.NORTH_UP, "skyseed:lot_door", "skyseed:lot",
                "minecraft:empty", id(p.foundation()));
        return new Built(m, bes);
    }

    /** A 7×7 fenced animal pen: a grass paddock ringed by fence (open at the front gate), with hay, a trough, a lamp. */
    private static Built animalPen(Palette p) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState fence = p.fence().defaultBlockState();
        final int max = 6, mid = 3;
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), Blocks.GRASS_BLOCK.defaultBlockState());
                final boolean edge = x == 0 || x == max || z == 0 || z == max;
                if (edge && !(x == mid && z == 0)) {             // fence ring, open at the front-centre gate
                    m.put(new BlockPos(x, 1, z), fence);
                }
            }
        }
        m.put(new BlockPos(1, 1, max - 1), Blocks.HAY_BLOCK.defaultBlockState());           // feed
        m.put(new BlockPos(2, 1, max - 1), Blocks.HAY_BLOCK.defaultBlockState());
        m.put(new BlockPos(max - 1, 1, max - 1), Blocks.CAULDRON.defaultBlockState());      // a water trough
        m.put(new BlockPos(max - 1, 1, 1), fence);                                          // a lamp post
        m.put(new BlockPos(max - 1, 2, 1), Blocks.LANTERN.defaultBlockState());
        m.put(new BlockPos(mid, 1, mid), Blocks.SHORT_GRASS.defaultBlockState());
        conn(m, bes, new BlockPos(mid, 0, 0), FrontAndTop.NORTH_UP, "skyseed:lot_door", "skyseed:lot",
                "minecraft:empty", "minecraft:grass_block");
        return new Built(m, bes);
    }

    /** A 5×5 open-air market stall: a striped wool awning on fence posts over a counter of barrels and a composter. */
    private static Built marketStall(Palette p) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState floor = p.foundation().defaultBlockState();
        final BlockState fence = p.fence().defaultBlockState();
        final int max = 4, mid = 2;
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), floor);
            }
        }
        for (final int[] c : new int[][]{{0, 0}, {max, 0}, {0, max}, {max, max}}) {   // posts holding the awning
            m.put(new BlockPos(c[0], 1, c[1]), fence);
            m.put(new BlockPos(c[0], 2, c[1]), fence);
        }
        for (int x = 0; x <= max; x++) {                                              // a striped wool awning
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 3, z), (x + z) % 2 == 0 ? Blocks.WHITE_WOOL.defaultBlockState()
                        : Blocks.RED_WOOL.defaultBlockState());
            }
        }
        m.put(new BlockPos(1, 1, max - 1), Blocks.HAY_BLOCK.defaultBlockState());      // a counter of produce
        m.put(new BlockPos(2, 1, max - 1), Blocks.MELON.defaultBlockState());          // (no barrel/composter — those
        m.put(new BlockPos(3, 1, max - 1), Blocks.PUMPKIN.defaultBlockState());        //  read as a shop job-site)
        m.put(new BlockPos(mid, 2, mid), Blocks.LANTERN.defaultBlockState().setValue(BlockStateProperties.HANGING, true));
        conn(m, bes, new BlockPos(mid, 0, 0), FrontAndTop.NORTH_UP, "skyseed:lot_door", "skyseed:lot",
                "minecraft:empty", id(p.foundation()));
        return new Built(m, bes);
    }

    /**
     * A tiny 3×3 lot terminator: a lamp post on a small grass plot with a couple of flowers. It's the lots' fallback —
     * smaller than any building or field, so a lot the jigsaw couldn't squeeze a (taller, wider) shop or field into
     * still gets a tidy little decoration instead of a bare gap, the way vanilla drops a lamp/patch on a leftover plot.
     */
    private static Built terminator(Palette p) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        for (int x = 0; x <= 2; x++) {
            for (int z = 0; z <= 2; z++) {
                m.put(new BlockPos(x, 0, z), Blocks.GRASS_BLOCK.defaultBlockState());
            }
        }
        m.put(new BlockPos(0, 1, 0), Blocks.POPPY.defaultBlockState());
        m.put(new BlockPos(2, 1, 2), Blocks.OXEYE_DAISY.defaultBlockState());
        m.put(new BlockPos(1, 1, 1), p.fence().defaultBlockState());    // lamp post
        m.put(new BlockPos(1, 2, 1), Blocks.LANTERN.defaultBlockState());
        conn(m, bes, new BlockPos(1, 0, 0), FrontAndTop.NORTH_UP, "skyseed:lot_door", "skyseed:lot",
                "minecraft:empty", "minecraft:grass_block");
        return new Built(m, bes);
    }

    /**
     * An "over the void" lot decoration — a 5×5 plank pier (matching the wooden bridges, not a floating grass farm)
     * with a fence railing open at the entrance, a lamp post and a couple of barrels: a small supply dock. The
     * generator swaps the normal field/garden fillers for these on lots that sit over the void (see the {@code _void}
     * filler pool).
     */
    private static Built pier(Palette p) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState plank = p.wall().defaultBlockState();
        final BlockState fence = p.fence().defaultBlockState();
        final int max = 4, mid = 2;
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), plank); // a plank deck, like the bridges that reach it
                final boolean edge = x == 0 || x == max || z == 0 || z == max;
                if (edge && !(x == mid && z == 0)) { // a fence railing, open at the front-centre entrance
                    m.put(new BlockPos(x, 1, z), fence);
                }
            }
        }
        m.put(new BlockPos(1, 1, 2), fence); // a lamp post
        m.put(new BlockPos(1, 2, 2), Blocks.LANTERN.defaultBlockState());
        m.put(new BlockPos(3, 1, 1), Blocks.BARREL.defaultBlockState()); // a little dockside storage
        m.put(new BlockPos(3, 1, 3), Blocks.BARREL.defaultBlockState());
        //? if >=26.1.2 {
        /*m.put(new BlockPos(3, 1, 2), Blocks.IRON_CHAIN.defaultBlockState()); // a mooring chain (the pier's signature)*/
        //?} else {
        m.put(new BlockPos(3, 1, 2), Blocks.CHAIN.defaultBlockState()); // a mooring chain (the pier's signature)
        //?}
        conn(m, bes, new BlockPos(mid, 0, 0), FrontAndTop.NORTH_UP, "skyseed:lot_door", "skyseed:lot",
                "minecraft:empty", id(p.wall()));
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
                                   FrontAndTop dir, String streetsPool, String finalState) {
        conn(m, bes, pos, dir, "skyseed:street", "skyseed:street", streetsPool, finalState);
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
