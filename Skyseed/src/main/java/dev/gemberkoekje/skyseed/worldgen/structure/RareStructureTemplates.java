package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.ChestBlock;
import net.minecraft.world.level.block.LayeredCauldronBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * Code-authored templates for the {@code rare_structures} surprises that occasionally germinate in place of an
 * ordinary island: the snowy {@link #igloo()} (a sealed snow hut with a trapped zombie villager to cure and an
 * igloo-loot chest), the {@link #abandonedCottage()} (a cobwebbed ruin of a Hamlet home, haunted by a zombie
 * villager), and the {@link #oceanRuin()} (a flooded stone-brick basin with suspicious sand and a sunken chest).
 * The mobs themselves come from each rare structure's {@code mobs} pack (spawned at the island centre); these
 * templates are the architecture. Written to {@code .nbt} at dev time — see {@link DevStructureGenerator}.
 */
public final class RareStructureTemplates {
    private RareStructureTemplates() {}

    public static void generateInto(Path base) throws IOException {
        writeIfAbsent(base.resolve("igloo/igloo.nbt"), igloo());
        writeIfAbsent(base.resolve("abandoned/cottage.nbt"), abandonedCottage());
        writeIfAbsent(base.resolve("ocean_ruin/ruin.nbt"), oceanRuin());
        writeIfAbsent(base.resolve("evoker_cell/cell.nbt"), evokerCell());
        writeIfAbsent(base.resolve("vault_cell/cell.nbt"), vaultCell());
    }

    /**
     * A 5×5 sealed snow dome: snow walls and roof keep it dark, so the zombie villager spawned inside (via the
     * theme's mobs pack) survives daylight until the player digs in. A brewing stand, a water cauldron and an
     * igloo-loot chest furnish it — once cured by the player, the villager claims the brewing stand as a cleric.
     */
    private static Built igloo() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState snow = Blocks.SNOW_BLOCK.defaultBlockState();
        final int max = 4, mid = 2;

        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), snow); // floor
                m.put(new BlockPos(x, 4, z), snow); // roof (sealed, keeps it dark)
                if (x == 0 || x == max || z == 0 || z == max) {
                    for (int h = 1; h <= 3; h++) {
                        m.put(new BlockPos(x, h, z), snow); // walls
                    }
                }
            }
        }
        // The igloo's kit, in the corners — the centre stays clear for the zombie villager to spawn into.
        m.put(new BlockPos(1, 1, 1), Blocks.BREWING_STAND.defaultBlockState());
        m.put(new BlockPos(3, 1, 1), Blocks.WATER_CAULDRON.defaultBlockState().setValue(LayeredCauldronBlock.LEVEL, 3));
        m.put(new BlockPos(3, 1, 3), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.WEST));
        bes.put(new BlockPos(3, 1, 3), StructureParts.lootChest("minecraft:chests/igloo_chest"));
        m.put(new BlockPos(2, 1, 3), Blocks.RED_CARPET.defaultBlockState());
        m.put(new BlockPos(1, 1, 3), Blocks.RED_CARPET.defaultBlockState());

        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:snow_block");
        return new Built(m, bes);
    }

    /**
     * A 7×7 oak cottage gone to ruin: cobwebs inside, gaps punched in the walls and roof, an open doorway, a
     * village-house chest — and, pointedly, no bed (the spawned zombie villager is the last "resident").
     */
    private static Built abandonedCottage() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState plank = Blocks.OAK_PLANKS.defaultBlockState();
        final BlockState log = Blocks.OAK_LOG.defaultBlockState();
        final BlockState web = Blocks.COBWEB.defaultBlockState();
        final int max = 6, mid = 3;

        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), plank); // floor
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                final boolean corner = (x == 0 || x == max) && (z == 0 || z == max);
                if (perim) {
                    for (int h = 1; h <= 3; h++) {
                        m.put(new BlockPos(x, h, z), corner ? log : plank);
                    }
                }
                m.put(new BlockPos(x, 4, z), plank); // roof
            }
        }
        // Punch the ruin: missing wall blocks and roof holes let the weather (and the light) in.
        for (final BlockPos gap : new BlockPos[]{
                new BlockPos(max, 2, 2), new BlockPos(0, 3, 4), new BlockPos(4, 3, max), new BlockPos(2, 2, 0),
                new BlockPos(2, 4, 2), new BlockPos(4, 4, 4), new BlockPos(3, 4, 5), new BlockPos(5, 4, 2)}) {
            m.remove(gap);
        }
        // Open doorway in the front (z=0) wall — the door long since gone.
        m.remove(new BlockPos(mid, 1, 0));
        m.remove(new BlockPos(mid, 2, 0));

        // Cobwebs strung through the corners (never the centre, where the zombie villager spawns).
        for (final BlockPos w : new BlockPos[]{
                new BlockPos(1, 1, 1), new BlockPos(1, 2, 1), new BlockPos(5, 1, 5),
                new BlockPos(5, 3, 1), new BlockPos(1, 1, 5), new BlockPos(5, 2, 4)}) {
            m.put(w, web);
        }
        // A looted-but-not-empty chest, and a chunk of fallen masonry.
        m.put(new BlockPos(5, 1, 1), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.WEST));
        bes.put(new BlockPos(5, 1, 1), StructureParts.lootChest("minecraft:chests/village/village_plains_house"));
        m.put(new BlockPos(1, 1, 4), Blocks.CRACKED_STONE_BRICKS.defaultBlockState());

        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:oak_planks");
        return new Built(m, bes);
    }

    /**
     * A 7×7 flooded stone-brick basin standing where the Aquatic pond would be: weathered walls hold a 2-deep
     * pool, suspicious sand hides archaeology finds on the floor, and a sunken chest carries underwater-ruin
     * loot. The lower two wall courses are kept intact to contain the water; the top course is broken for ruin.
     */
    private static Built oceanRuin() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState water = Blocks.WATER.defaultBlockState();
        final int max = 6, mid = 3;

        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), weathered(x, z)); // floor
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                if (perim) {
                    // Two intact lower courses contain the pool; a broken (sometimes-missing) top course.
                    m.put(new BlockPos(x, 1, z), weathered(x, z + 1));
                    m.put(new BlockPos(x, 2, z), weathered(x + 1, z));
                    if ((x * 5 + z) % 3 != 0) {
                        m.put(new BlockPos(x, 3, z), weathered(x, z));
                    }
                } else {
                    // Interior: a 2-deep pool over the floor.
                    m.put(new BlockPos(x, 1, z), water);
                    m.put(new BlockPos(x, 2, z), water);
                }
            }
        }
        // Suspicious sand/gravel sunk in the floor — brush them for ocean-ruin archaeology.
        for (final BlockPos s : new BlockPos[]{new BlockPos(2, 0, 2), new BlockPos(4, 0, 4), new BlockPos(2, 0, 4)}) {
            m.put(s, Blocks.SUSPICIOUS_SAND.defaultBlockState());
            bes.put(s, StructureParts.suspicious("minecraft:archaeology/ocean_ruin_warm"));
        }
        m.put(new BlockPos(4, 0, 2), Blocks.SUSPICIOUS_GRAVEL.defaultBlockState());
        bes.put(new BlockPos(4, 0, 2), StructureParts.suspicious("minecraft:archaeology/ocean_ruin_cold"));

        // A sunken chest in the pool corner.
        m.put(new BlockPos(1, 1, 1), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.EAST));
        bes.put(new BlockPos(1, 1, 1), StructureParts.lootChest("minecraft:chests/underwater_ruin_big"));
        // A couple of broken pillars rising clear of the water.
        for (int h = 1; h <= 4; h++) {
            m.put(new BlockPos(5, h, 5), weathered(5, h));
        }
        m.put(new BlockPos(5, 1, 2), weathered(0, 0));
        m.put(new BlockPos(5, 2, 2), Blocks.MOSSY_STONE_BRICKS.defaultBlockState());

        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:stone_bricks");
        return new Built(m, bes);
    }

    /**
     * The <b>Evoker Cell</b> — a small sealed dark-oak room (a mansion fragment) holding one evoker, spawned via
     * the rare structure's {@code mobs} pack into the dark centre. Kept dark so the evoker stays dormant until
     * the player breaks in; on its death it drops the bootstrap <b>Totem of Undying</b>. A woodland-mansion chest
     * and bookshelves dress it. Rare on a Forest grown in a {@code dark_forest} biome. See SKYGRANDSTRUCTURESPLAN.
     */
    private static Built evokerCell() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState plank = Blocks.DARK_OAK_PLANKS.defaultBlockState();
        final BlockState log = Blocks.DARK_OAK_LOG.defaultBlockState();
        final int max = 6, mid = 3; // 7×7 sealed, 5×5 interior

        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), plank); // floor
                m.put(new BlockPos(x, 4, z), plank); // ceiling — sealed and dark
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                final boolean corner = (x == 0 || x == max) && (z == 0 || z == max);
                if (perim) {
                    for (int h = 1; h <= 3; h++) {
                        m.put(new BlockPos(x, h, z), corner ? log : plank);
                    }
                }
            }
        }
        // Mansion dressing — red carpet underfoot, a bookshelf, a woodland-mansion chest. The centre stays clear
        // for the evoker to spawn into.
        for (final int[] c : new int[][]{{2, 2}, {4, 2}, {2, 4}, {4, 4}, {3, 2}, {3, 4}}) {
            m.put(new BlockPos(c[0], 1, c[1]), Blocks.RED_CARPET.defaultBlockState());
        }
        m.put(new BlockPos(5, 1, 1), Blocks.BOOKSHELF.defaultBlockState());
        m.put(new BlockPos(5, 2, 1), Blocks.BOOKSHELF.defaultBlockState());
        m.put(new BlockPos(1, 1, 1), Blocks.CHEST.defaultBlockState().setValue(ChestBlock.FACING, Direction.EAST));
        bes.put(new BlockPos(1, 1, 1), StructureParts.lootChest("minecraft:chests/woodland_mansion"));

        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:dark_oak_planks");
        return new Built(m, bes);
    }

    /**
     * The <b>Vault Cell</b> — a small tuff/copper room buried (theme {@code sink}) in an Ancient island: two
     * {@code trial_spawner}s and a {@code vault}. Dig in, clear the spawners for <b>Trial Keys</b>, open the
     * vault for the reward — a self-contained mini trial-chamber and the reliable key source. The trial mechanics
     * are native 1.21 block-entity behaviour; the default vault already requires a trial key. See plan.
     */
    private static Built vaultCell() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();
        final BlockState air = Blocks.AIR.defaultBlockState();
        final int max = 6, mid = 3; // 7×7, 5×5×3 interior — carved out of solid fill when buried

        for (int x = 0; x <= max; x++) {
            for (int z = 0; z <= max; z++) {
                m.put(new BlockPos(x, 0, z), tuffMix(x, z));   // floor
                m.put(new BlockPos(x, 4, z), tuffMix(x, z));   // ceiling
                final boolean perim = x == 0 || x == max || z == 0 || z == max;
                for (int h = 1; h <= 3; h++) {
                    m.put(new BlockPos(x, h, z), perim ? tuffMix(x, h + z) : air); // walls / hollow interior
                }
            }
        }
        // Two trial spawners (a zombie wave and a skeleton wave) and a vault. Completing a spawner yields a
        // Trial Key (native); the default vault consumes a key and ejects the trial-chamber reward.
        m.put(new BlockPos(2, 1, 2), Blocks.TRIAL_SPAWNER.defaultBlockState());
        bes.put(new BlockPos(2, 1, 2), trialSpawner("minecraft:zombie"));
        m.put(new BlockPos(4, 1, 4), Blocks.TRIAL_SPAWNER.defaultBlockState());
        bes.put(new BlockPos(4, 1, 4), trialSpawner("minecraft:skeleton"));
        m.put(new BlockPos(1, 1, 3), Blocks.VAULT.defaultBlockState());

        StructureParts.anchor(m, bes, new BlockPos(mid, 0, mid), "minecraft:tuff_bricks");
        return new Built(m, bes);
    }

    /** A trial-spawner block entity configured to spawn waves of {@code mobId} (schema verified in-game). */
    private static CompoundTag trialSpawner(String mobId) {
        final CompoundTag entity = new CompoundTag();
        entity.putString("id", mobId);
        final CompoundTag spawnData = new CompoundTag();
        spawnData.put("entity", entity.copy());
        final CompoundTag potData = new CompoundTag();
        potData.put("entity", entity.copy());
        final CompoundTag potential = new CompoundTag();
        potential.put("data", potData);
        potential.putInt("weight", 1);
        final ListTag potentials = new ListTag();
        potentials.add(potential);
        final CompoundTag normal = new CompoundTag();
        normal.put("spawn_potentials", potentials);
        normal.putFloat("total_mobs", 4.0f);
        normal.putFloat("simultaneous_mobs", 2.0f);
        final CompoundTag be = new CompoundTag();
        be.putString("id", "minecraft:trial_spawner");
        be.put("spawn_data", spawnData);
        be.put("normal_config", normal);
        return be;
    }

    /** A deterministic tuff/copper mix for the trial-cell masonry (mostly tuff bricks). */
    private static BlockState tuffMix(int a, int b) {
        return switch (Math.floorMod(a * 7 + b * 5, 6)) {
            case 0 -> Blocks.CUT_COPPER.defaultBlockState();
            case 1 -> Blocks.CHISELED_TUFF.defaultBlockState();
            default -> Blocks.TUFF_BRICKS.defaultBlockState();
        };
    }

    /** A deterministic weathered stone-brick mix (plain / mossy / cracked) for a ruined, varied look. */
    private static BlockState weathered(int a, int b) {
        final int h = Math.floorMod(a * 7 + b * 13, 5);
        if (h == 0) {
            return Blocks.MOSSY_STONE_BRICKS.defaultBlockState();
        }
        if (h == 1) {
            return Blocks.CRACKED_STONE_BRICKS.defaultBlockState();
        }
        return Blocks.STONE_BRICKS.defaultBlockState();
    }
}
