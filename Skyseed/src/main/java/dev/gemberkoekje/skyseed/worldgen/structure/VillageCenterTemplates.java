package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.FrontAndTop;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.BedBlock;
import net.minecraft.world.level.block.BellBlock;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.DoorBlock;
import net.minecraft.world.level.block.GrindstoneBlock;
import net.minecraft.world.level.block.JigsawBlock;
import net.minecraft.world.level.block.LanternBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.AttachFace;
import net.minecraft.world.level.block.state.properties.BedPart;
import net.minecraft.world.level.block.state.properties.BellAttachType;
import net.minecraft.world.level.block.state.properties.DoubleBlockHalf;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * Code-authored Village Center pieces, generated to {@code .nbt} at dev time (see {@link DevStructureGenerator}).
 * A bell-topped plaza whose four connectors each point at a <em>specific</em> hall pool, so the layout is
 * deterministic and every one of the 13 professions is guaranteed — a "full trading hall" island.
 */
public final class VillageCenterTemplates {
    private VillageCenterTemplates() {}

    private record Built(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> blockEntities) {}

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("plaza.nbt"), plaza());
        // 13 professions across four themed halls (4 + 3 + 3 + 3).
        writeIfAbsent(dir.resolve("hall_farm.nbt"), hall(
                Blocks.COMPOSTER.defaultBlockState(),        // farmer
                Blocks.LOOM.defaultBlockState(),             // shepherd
                Blocks.FLETCHING_TABLE.defaultBlockState(),  // fletcher
                Blocks.SMOKER.defaultBlockState()));         // butcher
        writeIfAbsent(dir.resolve("hall_smith.nbt"), hall(
                Blocks.BLAST_FURNACE.defaultBlockState(),    // armorer
                Blocks.GRINDSTONE.defaultBlockState().setValue(GrindstoneBlock.FACE, AttachFace.FLOOR), // weaponsmith
                Blocks.SMITHING_TABLE.defaultBlockState()));  // toolsmith
        writeIfAbsent(dir.resolve("hall_scholar.nbt"), hall(
                Blocks.LECTERN.defaultBlockState(),          // librarian
                Blocks.CARTOGRAPHY_TABLE.defaultBlockState(),// cartographer
                Blocks.BREWING_STAND.defaultBlockState()));   // cleric
        writeIfAbsent(dir.resolve("hall_craft.nbt"), hall(
                Blocks.STONECUTTER.defaultBlockState(),      // mason
                Blocks.CAULDRON.defaultBlockState(),         // leatherworker
                Blocks.BARREL.defaultBlockState()));          // fisherman
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
     * 9×9 cobblestone plaza: "bottom" anchor, a bell meeting-point, corner lanterns, four connectors (one hall
     * each). The plaza is two wider than the 7-wide halls, so each hall (centred on an edge midpoint) leaves the
     * plaza's corner columns open — walkable gaps to reach the bell and the halls without breaking blocks.
     */
    private static Built plaza() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState cob = Blocks.COBBLESTONE.defaultBlockState();
        for (int x = 0; x < 9; x++) {
            for (int z = 0; z < 9; z++) {
                m.put(new BlockPos(x, 0, z), cob);
            }
        }
        // Anchor + bell meeting-point on top, lanterns at the corners.
        m.put(new BlockPos(4, 0, 4), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.DOWN_SOUTH));
        bes.put(new BlockPos(4, 0, 4), jig("minecraft:bottom", "minecraft:empty", "minecraft:empty", "minecraft:cobblestone"));
        m.put(new BlockPos(4, 1, 4), Blocks.BELL.defaultBlockState()
                .setValue(BellBlock.ATTACHMENT, BellAttachType.FLOOR).setValue(BellBlock.FACING, Direction.NORTH));
        for (int[] c : new int[][]{{1, 1}, {7, 1}, {1, 7}, {7, 7}}) {
            m.put(new BlockPos(c[0], 1, c[1]), Blocks.LANTERN.defaultBlockState());
        }
        // One connector per side, each drawing from its own hall pool (deterministic — all 13 professions).
        edge(m, bes, new BlockPos(4, 0, 0), FrontAndTop.NORTH_UP, "skyseed:village_center/hall_farm");
        edge(m, bes, new BlockPos(4, 0, 8), FrontAndTop.SOUTH_UP, "skyseed:village_center/hall_scholar");
        edge(m, bes, new BlockPos(0, 0, 4), FrontAndTop.WEST_UP, "skyseed:village_center/hall_craft");
        edge(m, bes, new BlockPos(8, 0, 4), FrontAndTop.EAST_UP, "skyseed:village_center/hall_smith");
        return new Built(m, bes);
    }

    private static void edge(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockPos p, FrontAndTop facing, String pool) {
        m.put(p, Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, facing));
        bes.put(p, jig("skyseed:plaza_edge", "skyseed:hall_door", pool, "minecraft:cobblestone"));
    }

    private static final int[][] JOB_SLOTS = {{1, 2}, {5, 2}, {1, 3}, {5, 3}};
    private static final int[][] BED_FEET = {{1, 4}, {2, 4}, {4, 4}, {5, 4}};

    /** A 7×7 oak trading hall: door + connector on −Z, a bed and job-site block per profession given (1–4). */
    private static Built hall(BlockState... jobSites) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState floor = Blocks.OAK_PLANKS.defaultBlockState();
        final BlockState post = Blocks.OAK_LOG.defaultBlockState();
        final BlockState glass = Blocks.GLASS.defaultBlockState();
        final int n = 7, max = 6, mid = 3;
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
        // Door + inward connector on the -Z wall (faces the plaza after the jigsaw rotates the hall in).
        m.put(new BlockPos(mid, 1, 0), Blocks.OAK_DOOR.defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.LOWER).setValue(DoorBlock.FACING, Direction.NORTH));
        m.put(new BlockPos(mid, 2, 0), Blocks.OAK_DOOR.defaultBlockState()
                .setValue(DoorBlock.HALF, DoubleBlockHalf.UPPER).setValue(DoorBlock.FACING, Direction.NORTH));
        m.put(new BlockPos(mid, 0, 0), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.NORTH_UP));
        bes.put(new BlockPos(mid, 0, 0), jig("skyseed:hall_door", "skyseed:plaza_edge", "minecraft:empty", "minecraft:cobblestone"));
        // Windows + a hanging lantern so it stays lit (no mob spawns inside).
        m.put(new BlockPos(0, 2, 2), glass);
        m.put(new BlockPos(0, 2, 4), glass);
        m.put(new BlockPos(max, 2, 2), glass);
        m.put(new BlockPos(max, 2, 4), glass);
        m.put(new BlockPos(mid, 2, max), glass);
        m.put(new BlockPos(mid, 3, mid), Blocks.LANTERN.defaultBlockState().setValue(LanternBlock.HANGING, true));
        // A bed + job-site block per profession.
        for (int i = 0; i < jobSites.length && i < JOB_SLOTS.length; i++) {
            final int[] bed = BED_FEET[i];
            m.put(new BlockPos(bed[0], 1, bed[1]), Blocks.RED_BED.defaultBlockState()
                    .setValue(BedBlock.PART, BedPart.FOOT).setValue(BedBlock.FACING, Direction.SOUTH));
            m.put(new BlockPos(bed[0], 1, bed[1] + 1), Blocks.RED_BED.defaultBlockState()
                    .setValue(BedBlock.PART, BedPart.HEAD).setValue(BedBlock.FACING, Direction.SOUTH));
            final int[] job = JOB_SLOTS[i];
            m.put(new BlockPos(job[0], 1, job[1]), jobSites[i]);
        }
        return new Built(m, bes);
    }
}
