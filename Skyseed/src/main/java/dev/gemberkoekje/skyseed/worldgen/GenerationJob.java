package dev.gemberkoekje.skyseed.worldgen;

import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.MobSpawnType;
import net.minecraft.world.entity.animal.Bee;
import net.minecraft.world.entity.animal.IronGolem;
import net.minecraft.world.entity.npc.Villager;
import net.minecraft.world.entity.npc.VillagerType;
import net.minecraft.world.level.block.BedBlock;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.core.Holder;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.ResourceKey;
import net.minecraft.world.level.block.entity.BeehiveBlockEntity;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BedPart;
import net.minecraft.world.level.chunk.ChunkGenerator;
import net.minecraft.world.level.levelgen.structure.pools.JigsawPlacement;
import net.minecraft.world.level.levelgen.structure.pools.StructureTemplatePool;

/**
 * Drains an {@link IslandPlan} into the world a bounded number of blocks per tick, so an island never
 * places thousands of blocks in a single tick (README → Generation algorithm). The gradual fill doubles as a "grows in"
 * animation. Trees (configured features) are heavier than a setBlock, so they go a couple per tick
 * once the solid blocks have landed; mobs are spawned last, once the whole island exists.
 */
public final class GenerationJob {
    private static final int BLOCKS_PER_TICK = 512;
    private static final int TREES_PER_TICK = 2;

    private final ServerLevel level;
    private final IslandPlan plan;
    private int blockIndex = 0;
    private int treeIndex = 0;
    private boolean mobsSpawned = false;

    public GenerationJob(ServerLevel level, IslandPlan plan) {
        this.level = level;
        this.plan = plan;
    }

    /** Advance one tick. @return true once the whole island (blocks + trees + mobs) has been placed. */
    public boolean tick() {
        final int blockCount = plan.blocks().size();
        int budget = BLOCKS_PER_TICK;
        while (budget-- > 0 && blockIndex < blockCount) {
            IslandPlan.BlockPlacement bp = plan.blocks().get(blockIndex++);
            // UPDATE_CLIENTS only: show the block without neighbour/physics cascades.
            level.setBlock(bp.pos(), bp.state(), Block.UPDATE_CLIENTS);
        }
        if (blockIndex < blockCount) {
            return false;
        }

        final int treeCount = plan.trees().size();
        if (treeIndex < treeCount) {
            final ChunkGenerator generator = level.getChunkSource().getGenerator();
            int treeBudget = TREES_PER_TICK;
            while (treeBudget-- > 0 && treeIndex < treeCount) {
                IslandPlan.TreeSite ts = plan.trees().get(treeIndex++);
                ts.feature().place(level, generator, plan.random(), ts.pos());
            }
            if (treeIndex < treeCount) {
                return false;
            }
        }

        if (!mobsSpawned) {
            placeStructures();
            spawnMobs();
            populateHives();
            mobsSpawned = true;
        }
        return true;
    }

    /** Assemble the planned jigsaw structures, then spawn a villager at every bed they placed. */
    private void placeStructures() {
        for (IslandPlan.JigsawSite js : plan.jigsaws()) {
            final Holder<StructureTemplatePool> pool = level.registryAccess()
                    .lookupOrThrow(Registries.TEMPLATE_POOL)
                    .getOrThrow(ResourceKey.create(Registries.TEMPLATE_POOL, js.pool()));
            JigsawPlacement.generateJigsaw(level, pool, js.target(), js.depth(), js.origin(), false);
            spawnVillagersAtBeds(js.origin(), js.pad());
            for (int i = 0; i < js.ironGolems(); i++) {
                final IronGolem golem = EntityType.IRON_GOLEM.create(level);
                if (golem != null) {
                    golem.moveTo(js.origin().getX() + 0.5, js.origin().getY() + 1, js.origin().getZ() + 0.5, 0.0F, 0.0F);
                    golem.setPersistenceRequired();
                    level.addFreshEntity(golem);
                }
            }
        }
    }

    /**
     * One villager per bed in the assembled structure — adult, dressed for the biome, persistent. They
     * arrive unemployed; a villager beside an unclaimed job-site block (the shops carry them) takes up
     * that profession on its own, exactly as in a natural village.
     */
    private void spawnVillagersAtBeds(BlockPos origin, int pad) {
        final BlockPos.MutableBlockPos p = new BlockPos.MutableBlockPos();
        for (int dx = -pad; dx <= pad; dx++) {
            for (int dz = -pad; dz <= pad; dz++) {
                for (int dy = -1; dy <= 6; dy++) {
                    p.set(origin.getX() + dx, origin.getY() + dy, origin.getZ() + dz);
                    final BlockState state = level.getBlockState(p);
                    if (!state.is(Blocks.RED_BED) || state.getValue(BedBlock.PART) != BedPart.FOOT) {
                        continue;
                    }
                    final Villager villager = EntityType.VILLAGER.create(level);
                    if (villager != null) {
                        villager.moveTo(p.getX() + 0.5, p.getY(), p.getZ() + 0.5, 0.0F, 0.0F);
                        villager.setVillagerData(villager.getVillagerData().setType(VillagerType.byBiome(level.getBiome(p))));
                        villager.setPersistenceRequired();
                        level.addFreshEntity(villager);
                    }
                }
            }
        }
    }

    /** Fill any bee nests on the island with a few bees (they emerge to pollinate, then return home). */
    @SuppressWarnings({"deprecation", "removal"}) // BeehiveBlockEntity#addOccupant: the simplest worldgen-time way to seed a hive
    private void populateHives() {
        for (BlockPos hive : plan.hives()) {
            if (!(level.getBlockEntity(hive) instanceof BeehiveBlockEntity beehive)) {
                continue;
            }
            for (int i = 0; i < 3; i++) {
                Bee bee = EntityType.BEE.create(level);
                if (bee != null) {
                    bee.moveTo(hive.getX() + 0.5, hive.getY() + 0.5, hive.getZ() + 0.5, 0.0F, 0.0F);
                    beehive.addOccupant(bee);
                }
            }
        }
    }

    /** Spawn the planned mobs: land mobs on top of their surface block, water mobs inside the pond. */
    @SuppressWarnings({"deprecation", "removal"}) // EntityType#spawn convenience overload
    private void spawnMobs() {
        for (IslandPlan.MobSpawn ms : plan.mobs()) {
            if (ms.inWater()) {
                final BlockPos wp = ms.pos();
                if (level.getFluidState(wp).isEmpty()) {
                    continue; // the pond water at this spot got displaced (e.g. by a plant block)
                }
                // Place submerged, centred in the water block — EntityType#spawn aligns to the block top,
                // which would leave a squid/axolotl out of the water and they'd promptly die.
                final Entity e = ms.type().create(level);
                if (e instanceof Mob mob) {
                    mob.moveTo(wp.getX() + 0.5, wp.getY() + 0.5, wp.getZ() + 0.5, plan.random().nextFloat() * 360.0F, 0.0F);
                    mob.finalizeSpawn(level, level.getCurrentDifficultyAt(wp), MobSpawnType.SPAWNER, null);
                    level.addFreshEntity(mob);
                }
                continue;
            }
            final BlockPos surface = ms.pos();
            final BlockPos spawnPos = surface.above();
            // Need solid ground and two blocks of standing room — but plants (flowers, grass, mushrooms)
            // don't block a mob, so allow those; only skip on no ground or a real obstruction (trunk, leaves).
            if (level.getBlockState(surface).isAir()
                    || level.getBlockState(spawnPos).blocksMotion()
                    || level.getBlockState(spawnPos.above()).blocksMotion()) {
                continue;
            }
            ms.type().spawn(level, spawnPos, MobSpawnType.SPAWNER);
        }
    }
}
