package dev.gemberkoekje.skyseed.worldgen.structure;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.RespawnAnchorBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The Wither Arena — the Nether chapter's capstone venue. An enclosed, roofed <b>obsidian</b> bowl (13×13×10) so the
 * Wither's skull explosions can't break the arena or blow you into the void; nether-brick trim courses, glowstone
 * lighting, and a single narrow entrance the boss can't fit through. The floor carries a soul-sand summoning patch
 * (build your Wither there) and a sheltered corner with a <b>charged respawn anchor</b> (set spawn so a death drops
 * you back here, not home) and a {@code bastion_treasure} reward chest behind an arrow-slit window. The Wither is
 * craftable mid-Nether (soul sand from the Soul islands + skulls from the Fortress); this is the survivable place to
 * fight it, for the Nether Star → Beacon. See SKYNETHERPLAN.
 *
 * <p>Note: the plan's "soul-sand floor" gives way to obsidian here — soul sand isn't blast-resistant (and slows you);
 * the floor stays obsidian with a soul-sand summoning patch, which keeps the arena intact through the fight.
 */
public final class WitherArenaTemplates {
    private WitherArenaTemplates() {}

    public static void generateInto(Path dir) throws IOException {
        StructureParts.writeIfAbsent(dir.resolve("arena.nbt"), arena());
    }

    private static Built arena() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState obs = Blocks.OBSIDIAN.defaultBlockState();
        final BlockState nb = Blocks.NETHER_BRICKS.defaultBlockState();
        final BlockState soul = Blocks.SOUL_SAND.defaultBlockState();
        final BlockState glow = Blocks.GLOWSTONE.defaultBlockState();
        final int max = 12, mid = 6, wallTop = 8, roofY = 9;

        // Floor (obsidian) with a 5×5 soul-sand summoning patch in the centre.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                final boolean soulPatch = x >= mid - 2 && x <= mid + 2 && z >= mid - 2 && z <= mid + 2;
                m.put(new BlockPos(x, 0, z), soulPatch ? soul : obs);
            }
        }
        // Walls (obsidian) with nether-brick trim courses, a narrow entrance on the south wall, glowstone windows.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                if (x != 0 && x != max && z != 0 && z != max) {
                    continue;
                }
                for (int y = 1; y <= wallTop; y++) {
                    if (x == mid && z == 0 && y <= 3) {
                        continue; // entrance the Wither can't fit through
                    }
                    BlockState w = (y == 1 || y == wallTop) ? nb : obs;
                    final boolean wallLight = y == 4
                            && (((x == 0 || x == max) && z == mid) || ((z == 0 || z == max) && x == mid));
                    if (wallLight) {
                        w = glow;
                    }
                    m.put(new BlockPos(x, y, z), w);
                }
            }
        }
        // Roof (obsidian) — traps the boss — with glowstone panels.
        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                final boolean roofLight = (x == mid && z == mid)
                        || (x == 3 && z == 3) || (x == max - 3 && z == max - 3)
                        || (x == 3 && z == max - 3) || (x == max - 3 && z == 3);
                m.put(new BlockPos(x, roofY, z), roofLight ? glow : obs);
            }
        }
        // Sheltered corner (uses the arena's own walls on two sides): inner wall with a doorway, an arrow-slit window,
        // and a low roof — small enough the boss can't reach in. Holds the charged anchor and the reward chest.
        for (int z = 1; z <= 3; z++) {
            for (int y = 1; y <= 3; y++) {
                if (z == 1 && y <= 2) {
                    continue; // doorway from the arena into the shelter
                }
                m.put(new BlockPos(3, y, z), obs);
            }
        }
        for (int x = 1; x <= 2; x++) {
            for (int y = 1; y <= 3; y++) {
                if (x == 2 && y == 2) {
                    continue; // arrow-slit window onto the arena
                }
                m.put(new BlockPos(x, y, 3), obs);
            }
        }
        for (int x = 0; x <= 3; x++) {
            for (int z = 0; z <= 3; z++) {
                m.put(new BlockPos(x, 3, z), obs); // shelter roof
            }
        }
        m.put(new BlockPos(1, 1, 1), Blocks.RESPAWN_ANCHOR.defaultBlockState().setValue(RespawnAnchorBlock.CHARGE, 4));
        m.put(new BlockPos(1, 1, 2), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.EAST));
        bes.put(new BlockPos(1, 1, 2), StructureParts.lootChest("minecraft:chests/bastion_treasure"));

        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:soul_sand");
        return new Built(m, bes);
    }
}
