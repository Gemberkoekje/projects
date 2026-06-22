package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The grand <b>Trial Chamber</b> — a buried tuff/copper arena (the first build from SKYGRANDSTRUCTURESPLAN).
 * Self-contained: four regular trial spawners around a {@code breeze} boss spawner; clear them for Trial Keys
 * and open the regular vaults; with Bad Omen the spawners turn ominous and feed the centre <b>ominous vault</b>
 * (the heavy core → mace). Lit by hanging lanterns, entered by a ladder shaft punched up to the surface. Sunk
 * deep via the theme {@code sink}; v1 is a single rotated template (modular jigsaw pieces are a follow-up).
 */
public final class TrialChamberTemplates {
    private TrialChamberTemplates() {}

    private static final BlockState AIR = Blocks.AIR.defaultBlockState();

    private record Built(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> blockEntities) {}

    public static void generateInto(Path dir) throws IOException {
        final Path file = dir.resolve("chamber.nbt");
        if (!Files.exists(file)) {
            final Built b = chamber();
            StructureWriter.write(b.blocks(), b.blockEntities(), file);
            Skyseed.LOGGER.info("[skyseed] generated structure template {}", file.getFileName());
        }
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

    private static Built chamber() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final int max = 10, mid = 5;     // 11×11 outer, 9×9 interior
        final int ceil = 6;              // floor y0, interior y1-5 (5 tall), ceiling y6

        // Shell — floor, ceiling, perimeter walls; the interior carved to air (buried in solid fill).
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), mix(x, z));
                m.put(new BlockPos(x, ceil, z), mix(x, z));
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                for (int y = 1; y < ceil; y++) {
                    m.put(new BlockPos(x, y, z), perim ? mix(x, y + z) : AIR);
                }
            }
        }
        // Four tuff/copper pillars for cover.
        for (final int[] p : new int[][]{{3, 3}, {7, 3}, {3, 7}, {7, 7}}) {
            for (int y = 1; y < ceil; y++) {
                m.put(new BlockPos(p[0], y, p[1]), mix(p[0], y));
            }
        }

        // Boss: a breeze trial spawner dead centre, with the ominous vault beside it.
        m.put(new BlockPos(mid, 1, mid), Blocks.TRIAL_SPAWNER.defaultBlockState());
        bes.put(new BlockPos(mid, 1, mid), trialSpawner("minecraft:breeze"));
        m.put(new BlockPos(mid, 1, mid - 2), Blocks.VAULT.defaultBlockState().setValue(BlockStateProperties.OMINOUS, true));
        bes.put(new BlockPos(mid, 1, mid - 2), ominousVault());

        // Four regular trial spawners, each with a regular vault near it.
        final Object[][] rooms = {{2, 2, "minecraft:zombie"}, {8, 2, "minecraft:skeleton"},
                {2, 8, "minecraft:spider"}, {8, 8, "minecraft:breeze"}};
        for (final Object[] r : rooms) {
            final int sx = (int) r[0], sz = (int) r[1];
            m.put(new BlockPos(sx, 1, sz), Blocks.TRIAL_SPAWNER.defaultBlockState());
            bes.put(new BlockPos(sx, 1, sz), trialSpawner((String) r[2]));
        }
        for (final int[] v : new int[][]{{5, 1}, {9, 5}, {5, 9}}) {
            m.put(new BlockPos(v[0], 1, v[1]), Blocks.VAULT.defaultBlockState());
        }

        // Hanging lanterns for light.
        final BlockState lantern = Blocks.LANTERN.defaultBlockState().setValue(BlockStateProperties.HANGING, true);
        for (final int[] l : new int[][]{{3, 3}, {7, 3}, {3, 7}, {7, 7}, {5, 5}, {5, 2}, {5, 8}, {2, 5}, {8, 5}}) {
            m.put(new BlockPos(l[0], ceil - 1, l[1]), lantern);
        }

        // Entrance: a ladder shaft in the NW corner, punched up through the ceiling and the island surface
        // (nbt y7 = the surface when sunk) — the only tell.
        final BlockState ladder = Blocks.LADDER.defaultBlockState().setValue(BlockStateProperties.HORIZONTAL_FACING, Direction.EAST);
        for (int y = 1; y <= 7; y++) {
            m.put(new BlockPos(1, y, 1), ladder);
        }
        m.put(new BlockPos(2, 7, 1), AIR); // widen the surface mouth a touch
        m.put(new BlockPos(1, 7, 2), AIR);

        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:tuff_bricks");
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
