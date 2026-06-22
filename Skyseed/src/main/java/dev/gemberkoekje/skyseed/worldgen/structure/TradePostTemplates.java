package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
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
import net.minecraft.world.level.block.state.properties.DoubleBlockHalf;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * Code-authored Trade Post pieces, generated to {@code .nbt} at dev time (see {@link DevStructureGenerator}).
 * A central plaza carries four outward jigsaw connectors; the buildings pool fills them with shops, each a
 * small cabin with a bed and a job-site block. Real multi-piece jigsaw assembly, like a vanilla village.
 */
public final class TradePostTemplates {
    private TradePostTemplates() {}

    private record Built(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> blockEntities) {}

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("plaza.nbt"), plaza());
        writeIfAbsent(dir.resolve("shop_farmer.nbt"), shop(Blocks.COMPOSTER.defaultBlockState()));
        writeIfAbsent(dir.resolve("shop_librarian.nbt"), shop(Blocks.LECTERN.defaultBlockState()));
        writeIfAbsent(dir.resolve("shop_fisherman.nbt"), shop(Blocks.BARREL.defaultBlockState()));
        writeIfAbsent(dir.resolve("shop_fletcher.nbt"), shop(Blocks.FLETCHING_TABLE.defaultBlockState()));
        writeIfAbsent(dir.resolve("shop_toolsmith.nbt"), shop(Blocks.SMITHING_TABLE.defaultBlockState()));
    }

    private static void writeIfAbsent(Path file, Built b) throws IOException {
        if (!Files.exists(file)) {
            StructureWriter.write(b.blocks(), b.blockEntities(), file);
            Skyseed.LOGGER.info("[skyseed] generated structure template {}", file.getFileName());
        }
    }

    private static CompoundTag jig(String name, String target, String pool, String finalState) {
        final CompoundTag t = new CompoundTag();
        t.putString("id", "minecraft:jigsaw");
        t.putString("name", name);
        t.putString("target", target);
        t.putString("pool", pool);
        t.putString("final_state", finalState);
        t.putString("joint", "rollable");
        return t;
    }

    /**
     * 7×7 cobblestone plaza: a "bottom" anchor, a lantern centre, and four outward connectors to the buildings
     * pool. The plaza is two wider than the 5-wide shops so each shop, centred on an edge midpoint, leaves the
     * plaza's corner columns open — walkable gaps to sneak in and out without breaking a building.
     */
    private static Built plaza() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState cob = Blocks.COBBLESTONE.defaultBlockState();
        for (int x = 0; x < 7; x++) {
            for (int z = 0; z < 7; z++) {
                m.put(new BlockPos(x, 0, z), cob);
            }
        }
        // Anchor (becomes cobblestone), with a lantern centrepiece on top.
        m.put(new BlockPos(3, 0, 3), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.DOWN_SOUTH));
        bes.put(new BlockPos(3, 0, 3), jig("minecraft:bottom", "minecraft:empty", "minecraft:empty", "minecraft:cobblestone"));
        m.put(new BlockPos(3, 1, 3), Blocks.LANTERN.defaultBlockState());

        // Outward connectors at the four edge midpoints, drawing from the buildings pool.
        addEdge(m, bes, new BlockPos(3, 0, 0), FrontAndTop.NORTH_UP);
        addEdge(m, bes, new BlockPos(3, 0, 6), FrontAndTop.SOUTH_UP);
        addEdge(m, bes, new BlockPos(0, 0, 3), FrontAndTop.WEST_UP);
        addEdge(m, bes, new BlockPos(6, 0, 3), FrontAndTop.EAST_UP);
        return new Built(m, bes);
    }

    private static void addEdge(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockPos p, FrontAndTop facing) {
        m.put(p, Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, facing));
        bes.put(p, jig("skyseed:plaza_edge", "skyseed:building_door", "skyseed:trade_post/buildings", "minecraft:cobblestone"));
    }

    /** A 5×5 shop: oak cabin, a door + connector on the −Z side, a bed and the given job-site block inside. */
    private static Built shop(BlockState jobSite) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState floor = Blocks.OAK_PLANKS.defaultBlockState();
        final BlockState wall = Blocks.OAK_PLANKS.defaultBlockState();
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
                        m.put(new BlockPos(x, h, z), corner ? post : wall);
                    }
                }
                m.put(new BlockPos(x, 4, z), floor);
            }
        }
        // Door + connector on the -Z wall (faces the plaza after the jigsaw rotates the piece into place).
        m.put(new BlockPos(mid, 1, 0), Blocks.OAK_DOOR.defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.LOWER).setValue(DoorBlock.FACING, Direction.NORTH));
        m.put(new BlockPos(mid, 2, 0), Blocks.OAK_DOOR.defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.UPPER).setValue(DoorBlock.FACING, Direction.NORTH));
        m.put(new BlockPos(mid, 0, 0), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.NORTH_UP));
        bes.put(new BlockPos(mid, 0, 0), jig("skyseed:building_door", "skyseed:plaza_edge", "minecraft:empty", "minecraft:cobblestone"));
        // Windows on the side/back walls.
        m.put(new BlockPos(0, 2, mid), glass);
        m.put(new BlockPos(max, 2, mid), glass);
        m.put(new BlockPos(mid, 2, max), glass);
        // Bed, job site, torch.
        m.put(new BlockPos(1, 1, 2), Blocks.RED_BED.defaultBlockState()
                .setValue(BedBlock.PART, BedPart.FOOT).setValue(BedBlock.FACING, Direction.SOUTH));
        m.put(new BlockPos(1, 1, 3), Blocks.RED_BED.defaultBlockState()
                .setValue(BedBlock.PART, BedPart.HEAD).setValue(BedBlock.FACING, Direction.SOUTH));
        m.put(new BlockPos(3, 1, 3), jobSite);
        m.put(new BlockPos(3, 1, 1), Blocks.TORCH.defaultBlockState());
        return new Built(m, bes);
    }
}
