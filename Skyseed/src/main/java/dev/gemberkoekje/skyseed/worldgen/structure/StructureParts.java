package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.FrontAndTop;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.JigsawBlock;
import net.minecraft.world.level.block.StairBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Half;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Map;

/** Shared building blocks for the code-authored structure-island templates: the jigsaw anchor, loot chests, roofs. */
public final class StructureParts {
    private StructureParts() {}

    /**
     * Add a pitched gable roof of stairs over a building footprint {@code [x0..x1]×[z0..z1]}: the ridge runs
     * along Z at the centre X column (a solid plank beam), the roof slopes down in X with stairs facing uphill
     * toward the ridge (so the eave edges taper thin), and the two gable ends (z0 / z1) are filled to the
     * roofline. The ridge line is capped with a {@code slab} (a slimmer peak than a full block). {@code eaveY}
     * is the lowest roof course (sit it on the wall top). Pass {@code ov = 1} for a one-block overhang on every
     * side (the building must be inset so {@code x0-1} and {@code z0-1} stay ≥ 0 — structure NBT can't hold
     * negative positions) leaving an open stepped eave, or {@code ov = 0} for flush eaves.
     */
    public static void gableRoof(Map<BlockPos, BlockState> m, int x0, int x1, int z0, int z1, int eaveY,
                                 BlockState planks, Block stairs, Block slab, int ov) {
        final int mid = (x0 + x1) / 2;
        final int ridgeY = eaveY + (mid - x0) + ov;
        for (int x = x0 - ov; x <= x1 + ov; x++) {
            final int ry = ridgeY - Math.abs(x - mid);
            final BlockState roof = x == mid ? slab.defaultBlockState()
                    : stairs.defaultBlockState().setValue(StairBlock.FACING, x < mid ? Direction.EAST : Direction.WEST);
            for (int z = z0 - ov; z <= z1 + ov; z++) {
                m.put(new BlockPos(x, ry, z), roof);
            }
        }
        // Gable triangles: fill the front/back walls (z0, z1) up to the roofline with planks.
        for (int x = x0; x <= x1; x++) {
            final int top = ridgeY - Math.abs(x - mid);
            for (int y = eaveY + 1; y < top; y++) {
                m.put(new BlockPos(x, y, z0), planks);
                m.put(new BlockPos(x, y, z1), planks);
            }
        }
        // Smooth the gable rake: tuck an upside-down stair (facing downhill) under each overhanging rake stair
        // — out on the overhang plane (z0-ov / z1+ov), not in the wall — so the diagonal edge reads as solid.
        // Covers every sloped column including the lowest one at the eave.
        if (ov > 0) {
            for (int x = x0; x <= x1; x++) { // skip the eave-corner overhang columns (x0-ov / x1+ov)
                if (x == mid) {
                    continue;
                }
                final int ry = ridgeY - Math.abs(x - mid);
                final BlockState rake = stairs.defaultBlockState()
                        .setValue(StairBlock.FACING, x < mid ? Direction.WEST : Direction.EAST)
                        .setValue(StairBlock.HALF, Half.TOP);
                m.put(new BlockPos(x, ry - 1, z0 - ov), rake);
                m.put(new BlockPos(x, ry - 1, z1 + ov), rake);
            }
            // A full block under the ridge slab at the gable-overhang peak — a touch nicer than slab-over-air.
            m.put(new BlockPos(mid, ridgeY - 1, z0 - ov), planks);
            m.put(new BlockPos(mid, ridgeY - 1, z1 + ov), planks);
        }
    }

    /**
     * Place the "bottom" anchor jigsaw at {@code p} (the piece centres on the island here, then becomes
     * {@code finalState} after assembly). Call this LAST so a floor loop doesn't overwrite the jigsaw block.
     */
    public static void anchor(Map<BlockPos, BlockState> blocks, Map<BlockPos, CompoundTag> bes, BlockPos p, String finalState) {
        blocks.put(p, Blocks.JIGSAW.defaultBlockState().setValue(JigsawBlock.ORIENTATION, FrontAndTop.DOWN_SOUTH));
        final CompoundTag t = new CompoundTag();
        t.putString("id", "minecraft:jigsaw");
        t.putString("name", "minecraft:bottom");
        t.putString("target", "minecraft:empty");
        t.putString("pool", "minecraft:empty");
        t.putString("final_state", finalState);
        t.putString("joint", "rollable");
        bes.put(p, t);
    }

    /** A chest block-entity bound to a vanilla loot table id (filled on first open). */
    public static CompoundTag lootChest(String lootTable) {
        final CompoundTag be = new CompoundTag();
        be.putString("id", "minecraft:chest");
        be.putString("LootTable", lootTable);
        return be;
    }

    /**
     * A brushable (suspicious sand/gravel) block-entity bound to an archaeology loot table — the player
     * brushes it for a single drop. Pair with a {@code minecraft:suspicious_sand}/{@code _gravel} block.
     */
    public static CompoundTag suspicious(String lootTable) {
        final CompoundTag be = new CompoundTag();
        be.putString("id", "minecraft:brushable_block");
        be.putString("LootTable", lootTable);
        return be;
    }

    /** Write {@code b} to {@code file} as a structure {@code .nbt}, unless the file already exists (dev-time gen). */
    public static void writeIfAbsent(Path file, Built b) throws IOException {
        if (!Files.exists(file)) {
            StructureWriter.write(b.blocks(), b.blockEntities(), file);
            Skyseed.LOGGER.info("[skyseed] generated structure template {}", file.getFileName());
        }
    }

    /** A jigsaw connector block-entity; the piece becomes {@code finalState} once the jigsaw assembles. */
    public static CompoundTag jig(String name, String target, String pool, String finalState) {
        final CompoundTag t = new CompoundTag();
        t.putString("id", "minecraft:jigsaw");
        t.putString("name", name);
        t.putString("target", target);
        t.putString("pool", pool);
        t.putString("final_state", finalState);
        t.putString("joint", "rollable");
        return t;
    }

    /** A vanilla mob-spawner block-entity that spawns {@code mobId}. */
    public static CompoundTag mobSpawner(String mobId) {
        final CompoundTag entity = new CompoundTag();
        entity.putString("id", mobId);
        final CompoundTag spawnData = new CompoundTag();
        spawnData.put("entity", entity.copy());
        final CompoundTag potData = new CompoundTag();
        potData.put("entity", entity.copy());
        final CompoundTag potential = new CompoundTag();
        potential.putInt("weight", 1);
        potential.put("data", potData);
        final ListTag potentials = new ListTag();
        potentials.add(potential);
        final CompoundTag be = new CompoundTag();
        be.putString("id", "minecraft:mob_spawner");
        be.put("SpawnData", spawnData);
        be.put("SpawnPotentials", potentials);
        return be;
    }
}
