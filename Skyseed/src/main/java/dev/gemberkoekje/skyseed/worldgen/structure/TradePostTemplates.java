package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.FrontAndTop;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.BedBlock;
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
 * Trade Post — a real little jigsaw village (SKYJIGSAWPLAN Phase 1). A solid cobblestone {@code square} start
 * piece radiates a {@code streets} pool ({@code straight}/{@code corner}/{@code cross} + an empty terminator)
 * that branches and twists at depth 6, and the streets hang {@code lot}s off their sides from a {@code lots}
 * pool — the five trade shops plus non-shop scenery (a fenced {@code wheat_field}, a {@code garden}) and a
 * weighted empty terminator so the village breathes. The street pieces bake no floor: each lays
 * {@link PathSurfacer#MARKER} markers, so {@link PathSurfacer} renders the lanes as terrain-aware dirt paths on
 * the island and self-railing wooden bridges where a lane runs out over the void. A villager spawns at every
 * shop bed (the fields and garden carry none).
 */
public final class TradePostTemplates {
    private TradePostTemplates() {}

    private static final String STREETS = "skyseed:trade_post/streets";
    private static final String LOTS = "skyseed:trade_post/lots";

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("square.nbt"), square());
        writeIfAbsent(dir.resolve("street_straight.nbt"), streetSegment(
                new FrontAndTop[]{FrontAndTop.WEST_UP, FrontAndTop.EAST_UP},
                new FrontAndTop[]{FrontAndTop.NORTH_UP, FrontAndTop.SOUTH_UP}));
        writeIfAbsent(dir.resolve("street_corner.nbt"), streetSegment(
                new FrontAndTop[]{FrontAndTop.WEST_UP, FrontAndTop.SOUTH_UP},
                new FrontAndTop[]{FrontAndTop.NORTH_UP}));
        // No 4-way cross piece on purpose: crossings pack parallel streets only 3 apart, so the 5-wide lots
        // between them overlap and get rejected. Straight + corner runs keep open space along the sides for lots.
        writeIfAbsent(dir.resolve("shop_farmer.nbt"), shop(Blocks.COMPOSTER.defaultBlockState()));
        writeIfAbsent(dir.resolve("shop_librarian.nbt"), shop(Blocks.LECTERN.defaultBlockState()));
        writeIfAbsent(dir.resolve("shop_fisherman.nbt"), shop(Blocks.BARREL.defaultBlockState()));
        writeIfAbsent(dir.resolve("shop_fletcher.nbt"), shop(Blocks.FLETCHING_TABLE.defaultBlockState()));
        writeIfAbsent(dir.resolve("shop_toolsmith.nbt"), shop(Blocks.SMITHING_TABLE.defaultBlockState()));
        writeIfAbsent(dir.resolve("wheat_field.nbt"), wheatField());
        writeIfAbsent(dir.resolve("garden.nbt"), garden());
    }

    /** 7×7 solid-cobblestone village square: the island anchor + a lantern, and four outward street connectors. */
    private static Built square() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState cob = Blocks.COBBLESTONE.defaultBlockState();
        for (int x = 0; x <= 6; x++) {
            for (int z = 0; z <= 6; z++) {
                m.put(new BlockPos(x, 0, z), cob);
            }
        }
        streetConn(m, bes, new BlockPos(3, 0, 0), FrontAndTop.NORTH_UP, "minecraft:cobblestone");
        streetConn(m, bes, new BlockPos(3, 0, 6), FrontAndTop.SOUTH_UP, "minecraft:cobblestone");
        streetConn(m, bes, new BlockPos(0, 0, 3), FrontAndTop.WEST_UP, "minecraft:cobblestone");
        streetConn(m, bes, new BlockPos(6, 0, 3), FrontAndTop.EAST_UP, "minecraft:cobblestone");
        m.put(new BlockPos(3, 1, 3), Blocks.LANTERN.defaultBlockState());
        anchor(m, bes, new BlockPos(3, 0, 3), "minecraft:cobblestone"); // last, so the floor loop can't clobber it
        return new Built(m, bes);
    }

    /** A 3×3 marker street deck with through-connectors on {@code streetSides} and lot connectors on {@code lotSides}. */
    private static Built streetSegment(FrontAndTop[] streetSides, FrontAndTop[] lotSides) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState wool = PathSurfacer.MARKER.defaultBlockState();
        for (int x = 0; x <= 2; x++) {
            for (int z = 0; z <= 2; z++) {
                m.put(new BlockPos(x, 1, z), wool); // a path marker one block above every deck tile
            }
        }
        for (final FrontAndTop s : streetSides) {
            streetConn(m, bes, edge(s), s, "minecraft:air");
        }
        for (final FrontAndTop s : lotSides) {
            conn(m, bes, edge(s), s, "skyseed:lot", "skyseed:lot_door", LOTS, "minecraft:air");
        }
        return new Built(m, bes);
    }

    /** A 5×5 oak shop: a bed and a job-site block inside, a door + lot connector on the −Z wall facing the street. */
    private static Built shop(BlockState jobSite) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState floor = Blocks.OAK_PLANKS.defaultBlockState();
        final BlockState post = Blocks.OAK_LOG.defaultBlockState();
        final BlockState glass = Blocks.GLASS.defaultBlockState();
        final int n = 5, max = 4, mid = 2;
        for (int x = 0; x < n; x++) {
            for (int z = 0; z < n; z++) {
                m.put(new BlockPos(x, 0, z), floor);
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                final boolean corner = (x == 0 || x == max) && (z == 0 || z == max);
                if (perim) {
                    for (int h = 1; h <= 3; h++) {
                        m.put(new BlockPos(x, h, z), corner ? post : floor);
                    }
                }
                m.put(new BlockPos(x, 4, z), floor);
            }
        }
        // Door + lot connector on the −Z wall (faces the street once the jigsaw rotates the piece into place).
        m.put(new BlockPos(mid, 1, 0), Blocks.OAK_DOOR.defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.LOWER).setValue(DoorBlock.FACING, Direction.NORTH));
        m.put(new BlockPos(mid, 2, 0), Blocks.OAK_DOOR.defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.UPPER).setValue(DoorBlock.FACING, Direction.NORTH));
        conn(m, bes, new BlockPos(mid, 0, 0), FrontAndTop.NORTH_UP, "skyseed:lot_door", "skyseed:lot",
                "minecraft:empty", "minecraft:oak_planks");
        m.put(new BlockPos(0, 2, mid), glass);
        m.put(new BlockPos(max, 2, mid), glass);
        m.put(new BlockPos(mid, 2, max), glass);
        m.put(new BlockPos(1, 1, 2), Blocks.RED_BED.defaultBlockState()
                .setValue(BedBlock.PART, BedPart.FOOT).setValue(BedBlock.FACING, Direction.SOUTH));
        m.put(new BlockPos(1, 1, 3), Blocks.RED_BED.defaultBlockState()
                .setValue(BedBlock.PART, BedPart.HEAD).setValue(BedBlock.FACING, Direction.SOUTH));
        m.put(new BlockPos(3, 1, 3), jobSite);
        m.put(new BlockPos(3, 1, 1), Blocks.TORCH.defaultBlockState());
        StructureParts.gableRoof(m, 0, max, 0, max, 4, floor, Blocks.OAK_STAIRS, Blocks.OAK_SLAB, 0);
        return new Built(m, bes);
    }

    /** A 5×5 fenced wheat field — tilled, watered and grown — with a gate onto the street. No villager (scenery). */
    private static Built wheatField() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState dirt = Blocks.DIRT.defaultBlockState();
        final BlockState farmland = Blocks.FARMLAND.defaultBlockState().setValue(BlockStateProperties.MOISTURE, 7);
        final BlockState wheat = Blocks.WHEAT.defaultBlockState().setValue(BlockStateProperties.AGE_7, 7);
        final BlockState fence = Blocks.OAK_FENCE.defaultBlockState();
        final int max = 4, mid = 2;
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                m.put(new BlockPos(x, 0, z), perim ? dirt : farmland);
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
                "minecraft:empty", "minecraft:dirt");
        StructureParts.linkFences(m);
        return new Built(m, bes);
    }

    /** A 5×5 flower garden with a central lamp post. No villager (scenery). */
    private static Built garden() {
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
        m.put(new BlockPos(mid, 1, mid), Blocks.OAK_FENCE.defaultBlockState()); // lamp post
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

    /** A self-linking street connector at {@code p} facing {@code dir}. */
    private static void streetConn(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockPos p,
                                   FrontAndTop dir, String finalState) {
        conn(m, bes, p, dir, "skyseed:street", "skyseed:street", STREETS, finalState);
    }

    /** A jigsaw connector block-entity at {@code p}. */
    private static void conn(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockPos p,
                             FrontAndTop dir, String name, String target, String pool, String finalState) {
        m.put(p, Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, dir));
        bes.put(p, jig(name, target, pool, finalState));
    }
}
