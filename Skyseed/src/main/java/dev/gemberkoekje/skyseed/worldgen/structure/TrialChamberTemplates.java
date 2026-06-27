package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.FrontAndTop;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.JigsawBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The grand <b>Trial Chamber</b> — a buried tuff/copper complex, assembled by the jigsaw from a central
 * <b>hub</b> (the breeze boss + ominous vault + the ladder entrance) and a pool of <b>room</b> pieces (each a
 * trial spawner and a vault, or a twin-vault treasure room) that attach to the hub's four edge connectors. The
 * whole assembly is sunk deep via the theme {@code sink}; connectors sit at floor level and each piece carries
 * a pre-carved doorway, so the rooms open off the hub. Self-contained: clear the spawners for Trial Keys, open
 * the vaults; with Bad Omen the spawners turn ominous and feed the centre ominous vault (heavy core → mace).
 * (v2 — split from the original single 11×11 template into a modular pool for layout variety.)
 */
public final class TrialChamberTemplates {
    private TrialChamberTemplates() {}

    private static final BlockState AIR = Blocks.AIR.defaultBlockState();
    private static final String TUFF = "minecraft:tuff_bricks";

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("hub.nbt"), hub());
        writeIfAbsent(dir.resolve("room_zombie.nbt"), room("minecraft:zombie", 1));
        writeIfAbsent(dir.resolve("room_skeleton.nbt"), room("minecraft:skeleton", 1));
        writeIfAbsent(dir.resolve("room_spider.nbt"), room("minecraft:spider", 1));
        writeIfAbsent(dir.resolve("room_breeze.nbt"), room("minecraft:breeze", 1));
        writeIfAbsent(dir.resolve("room_treasure.nbt"), room(null, 2));
        writeIfAbsent(dir.resolve("gallery.nbt"), gallery());
    }

    /** A deterministic tuff/copper masonry mix (mostly tuff bricks). */
    private static BlockState mix(int a, int b) {
        return switch (Math.floorMod(a * 7 + b * 5, 7)) {
            case 0 -> Blocks.CUT_COPPER.defaultBlockState();
            case 1 -> Blocks.CHISELED_TUFF.defaultBlockState();
            case 2 -> Blocks.POLISHED_TUFF.defaultBlockState();
            default -> Blocks.TUFF_BRICKS.defaultBlockState();
        };
    }

    private static final BlockState LANTERN = Blocks.LANTERN.defaultBlockState().setValue(BlockStateProperties.HANGING, true);

    /**
     * The start piece: a 7×7 hub with the breeze boss spawner + ominous vault, a corner ladder up to the
     * surface, and four edge connectors (each with a doorway carved above it) drawing the rooms pool.
     */
    private static Built hub() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int max = 6, mid = 3, ceil = 6; // 7×7, interior y1-5 (5 tall)
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), mix(x, z));
                m.put(new BlockPos(x, ceil, z), mix(x, z));
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                for (int y = 1; y < ceil; y++) {
                    m.put(new BlockPos(x, y, z), (perim && !hubDoorway(x, y, z, max, mid)) ? mix(x, y + z) : AIR);
                }
            }
        }
        // Boss: breeze trial spawner dead centre, with the ominous vault in the far corner.
        m.put(new BlockPos(mid, 1, mid), Blocks.TRIAL_SPAWNER.defaultBlockState());
        bes.put(new BlockPos(mid, 1, mid), trialSpawner("minecraft:breeze"));
        m.put(new BlockPos(5, 1, 5), Blocks.VAULT.defaultBlockState().setValue(BlockStateProperties.OMINOUS, true));
        bes.put(new BlockPos(5, 1, 5), ominousVault());

        // Four edge connectors at floor level (become tuff after assembly); the doorway above each is carved air.
        connector(m, bes, new BlockPos(mid, 0, 0), FrontAndTop.NORTH_UP);
        connector(m, bes, new BlockPos(mid, 0, max), FrontAndTop.SOUTH_UP);
        connector(m, bes, new BlockPos(0, 0, mid), FrontAndTop.WEST_UP);
        connector(m, bes, new BlockPos(max, 0, mid), FrontAndTop.EAST_UP);

        // Ladder entrance in the NW corner, punched up through the ceiling and the island surface (nbt y7 = surface).
        final BlockState ladder = Blocks.LADDER.defaultBlockState().setValue(BlockStateProperties.HORIZONTAL_FACING, Direction.EAST);
        for (int y = 1; y <= 7; y++) {
            m.put(new BlockPos(1, y, 1), ladder);
        }
        for (final int[] l : new int[][]{{2, 2}, {4, 2}, {2, 4}, {4, 4}}) {
            m.put(new BlockPos(l[0], ceil - 1, l[1]), LANTERN);
        }
        // The "bottom" anchor seats the hub on the island; placed last so the floor loop doesn't overwrite it.
        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), TUFF);
        return new Built(m, bes);
    }

    /** The hub's wall doorways: the four edge-midpoint columns, lower two blocks, carved open for the passages. */
    private static boolean hubDoorway(int x, int y, int z, int max, int mid) {
        return y <= 2 && ((x == mid && (z == 0 || z == max)) || (z == mid && (x == 0 || x == max)));
    }

    private static void connector(Map<BlockPos, BlockState> m, Map<BlockPos, CompoundTag> bes, BlockPos p, FrontAndTop facing) {
        m.put(p, Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, facing));
        bes.put(p, jig("skyseed:chamber_edge", "skyseed:room_door", "skyseed:trial_chamber/rooms", TUFF));
    }

    /**
     * A 5×5 room piece: a connector + doorway on the −Z wall (faces the hub after the jigsaw rotates it in), a
     * trial spawner ({@code mob}, or none for a treasure room) and {@code vaults} regular vaults.
     */
    private static Built room(String mob, int vaults) {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int max = 4, mid = 2, ceil = 5; // 5×5, interior y1-4 (4 tall)
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), mix(x, z));
                m.put(new BlockPos(x, ceil, z), mix(x, z));
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                for (int yy = 1; yy < ceil; yy++) {
                    final boolean door = x == mid && z == 0 && yy <= 2; // −Z wall doorway
                    m.put(new BlockPos(x, yy, z), (perim && !door) ? mix(x, yy + z) : AIR);
                }
            }
        }
        // Connector + doorway on the −Z wall.
        m.put(new BlockPos(mid, 0, 0), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.NORTH_UP));
        bes.put(new BlockPos(mid, 0, 0), jig("skyseed:room_door", "skyseed:chamber_edge", "minecraft:empty", TUFF));

        if (mob != null) {
            m.put(new BlockPos(mid, 1, mid), Blocks.TRIAL_SPAWNER.defaultBlockState());
            bes.put(new BlockPos(mid, 1, mid), trialSpawner(mob));
        }
        // Vaults along the back wall (away from the doorway).
        m.put(new BlockPos(1, 1, 3), Blocks.VAULT.defaultBlockState());
        if (vaults > 1) {
            m.put(new BlockPos(3, 1, 3), Blocks.VAULT.defaultBlockState());
        }
        m.put(new BlockPos(mid, ceil - 1, mid), LANTERN);
        return new Built(m, bes);
    }

    /**
     * A 5×5 connective gallery (Phase 5 layout variety): a straight tuff corridor with a room-style doorway on the
     * −Z wall (mates the hub/parent) and a hub-style edge connector on the +Z wall that re-draws the rooms pool — so
     * the chamber chains rooms through corridors rather than only spoking them off the hub.
     */
    private static Built gallery() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int max = 4, mid = 2, ceil = 5;
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), mix(x, z));
                m.put(new BlockPos(x, ceil, z), mix(x, z));
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                for (int yy = 1; yy < ceil; yy++) {
                    final boolean door = x == mid && (z == 0 || z == max) && yy <= 2; // both Z walls open through
                    m.put(new BlockPos(x, yy, z), (perim && !door) ? mix(x, yy + z) : AIR);
                }
            }
        }
        m.put(new BlockPos(mid, 0, 0), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.NORTH_UP));
        bes.put(new BlockPos(mid, 0, 0), jig("skyseed:room_door", "skyseed:chamber_edge", "minecraft:empty", TUFF));   // in
        m.put(new BlockPos(mid, 0, max), Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.SOUTH_UP));
        bes.put(new BlockPos(mid, 0, max), jig("skyseed:chamber_edge", "skyseed:room_door", "skyseed:trial_chamber/rooms", TUFF));  // out → another room/gallery
        m.put(new BlockPos(mid, ceil - 1, mid), LANTERN);
        return new Built(m, bes);
    }

    /** A trial-spawner BE that spawns waves of {@code mob} in both normal and ominous modes. */
    private static CompoundTag trialSpawner(String mob) {
        final CompoundTag entity = new CompoundTag();
        entity.putString("id", mob);
        final CompoundTag spawnData = new CompoundTag();
        spawnData.put("entity", entity.copy());
        final CompoundTag be = new CompoundTag();
        be.putString("id", "minecraft:trial_spawner");
        be.put("spawn_data", spawnData);
        be.put("normal_config", spawnerConfig(mob, 5, 2));
        be.put("ominous_config", spawnerConfig(mob, 8, 3));
        return be;
    }

    private static CompoundTag spawnerConfig(String mob, int total, int simultaneous) {
        final CompoundTag entity = new CompoundTag();
        entity.putString("id", mob);
        final CompoundTag potData = new CompoundTag();
        potData.put("entity", entity.copy());
        final CompoundTag potential = new CompoundTag();
        potential.put("data", potData);
        potential.putInt("weight", 1);
        final ListTag potentials = new ListTag();
        potentials.add(potential);
        final CompoundTag cfg = new CompoundTag();
        cfg.put("spawn_potentials", potentials);
        cfg.putFloat("total_mobs", total);
        cfg.putFloat("simultaneous_mobs", simultaneous);
        return cfg;
    }

    /** An ominous vault BE — requires an ominous trial key and ejects the ominous reward (heavy core, etc.). */
    private static CompoundTag ominousVault() {
        final CompoundTag key = new CompoundTag();
        key.putString("id", "minecraft:ominous_trial_key");
        key.putInt("count", 1);
        final CompoundTag config = new CompoundTag();
        config.putString("loot_table", "minecraft:chests/trial_chambers/reward_ominous");
        config.put("key_item", key);
        final CompoundTag be = new CompoundTag();
        be.putString("id", "minecraft:vault");
        be.put("config", config);
        return be;
    }
}
