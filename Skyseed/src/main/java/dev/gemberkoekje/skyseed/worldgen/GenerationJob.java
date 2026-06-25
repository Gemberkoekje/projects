package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.compat.Jigsaw;
import dev.gemberkoekje.skyseed.compat.Lookup;
import dev.gemberkoekje.skyseed.worldgen.structure.PathSurfacer;
import dev.gemberkoekje.skyseed.worldgen.structure.Traps;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.entity.AgeableMob;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.MobSpawnType;
import net.minecraft.world.entity.animal.Bee;
import net.minecraft.world.entity.animal.IronGolem;
import net.minecraft.world.entity.animal.Sheep;
import net.minecraft.world.entity.npc.Villager;
import net.minecraft.world.entity.npc.VillagerType;
import net.minecraft.world.item.DyeColor;
import net.minecraft.world.level.block.BedBlock;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.CrossCollisionBlock;
import net.minecraft.world.level.block.WallBlock;
import net.minecraft.world.level.block.entity.BeehiveBlockEntity;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BedPart;
import net.minecraft.world.level.chunk.ChunkGenerator;
import net.minecraft.world.level.levelgen.structure.pools.StructureTemplatePool;
import net.minecraft.world.level.material.Fluids;
import net.neoforged.neoforge.event.EventHooks;

/**
 * Drains an {@link IslandPlan} into the world a bounded number of blocks per tick, so an island never
 * places thousands of blocks in a single tick (README → Generation algorithm). The gradual fill doubles as a "grows in"
 * animation. Trees (configured features) are heavier than a setBlock, so they go a couple per tick
 * once the solid blocks have landed; mobs are spawned last, once the whole island exists.
 */
public final class GenerationJob {
    private static final int BLOCKS_PER_TICK = 512;
    private static final int TREES_PER_TICK = 2;
    // Box (around a jigsaw origin) the connection-relink pass scans — generous enough for our biggest templates.
    private static final int LINK_RADIUS = 16;
    private static final int LINK_DOWN = 2;
    private static final int LINK_UP = 28;

    private final ServerLevel level;
    private final IslandPlan plan;
    private int blockIndex = 0;
    private int treeIndex = 0;
    private int treesPlaced = 0;
    private int scatterIndex = 0;
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
            if (plan.scatterPositions().contains(bp.pos())) {
                continue; // ground cover — deferred until after the trees so it can't block them
            }
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
                if (ts.feature().place(level, generator, plan.random(), ts.pos())) {
                    treesPlaced++;
                }
            }
            if (treeIndex < treeCount) {
                return false;
            }
            // Guarantee at least one tree: a vanilla tree feature returns false if its spot is blocked (a snow layer,
            // a sandy surface, the rim), so an island that asked for trees could come up bare. If every site failed,
            // force one onto a cleared grass+air spot.
            if (treesPlaced == 0) {
                forceOneTree(generator);
            }
        }

        // Ground cover, placed now (after the trees) onto the bare terrain, skipping any spot a tree has taken — so a
        // snow layer never makes a tree fail. Same blocks() list, re-scanned for the deferred scatter positions.
        if (scatterIndex < blockCount) {
            int scatterBudget = BLOCKS_PER_TICK;
            while (scatterBudget-- > 0 && scatterIndex < blockCount) {
                IslandPlan.BlockPlacement bp = plan.blocks().get(scatterIndex++);
                if (!plan.scatterPositions().contains(bp.pos())) {
                    continue; // terrain, already placed
                }
                if (bp.state().is(Blocks.SNOW)) {
                    snowColumnTop(bp.pos(), bp.state()); // cap the column's highest block (a tree's canopy), bare beneath
                } else if (level.getBlockState(bp.pos()).isAir()) {
                    level.setBlock(bp.pos(), bp.state(), Block.UPDATE_CLIENTS);
                }
            }
            if (scatterIndex < blockCount) {
                return false;
            }
        }

        if (!mobsSpawned) {
            placeStructures();
            spawnMobs();
            spawnEnclosureAnimals();
            populateHives();
            kickFluids();
            mobsSpawned = true;
        }
        return true;
    }

    /** Last resort so a tree-bearing island is never bare: clear a site to grass + air and place its feature there. */
    private void forceOneTree(ChunkGenerator generator) {
        for (IslandPlan.TreeSite ts : plan.trees()) {
            level.setBlock(ts.pos().below(), Blocks.GRASS_BLOCK.defaultBlockState(), Block.UPDATE_CLIENTS);
            level.setBlock(ts.pos(), Blocks.AIR.defaultBlockState(), Block.UPDATE_CLIENTS);
            if (ts.feature().place(level, generator, plan.random(), ts.pos())) {
                treesPlaced++;
                return;
            }
        }
    }

    /** Lay a snow layer on the highest block of a column (a tree's canopy in a snowy biome), leaving the ground bare. */
    private void snowColumnTop(BlockPos scatterPos, BlockState snow) {
        final BlockPos surface = scatterPos.below();
        BlockPos top = surface;
        for (int y = 1; y <= 24; y++) { // tall enough for any tree's canopy
            final BlockPos p = surface.above(y);
            if (!level.getBlockState(p).isAir()) {
                top = p;
            }
        }
        final BlockPos snowPos = top.above();
        if (level.getBlockState(snowPos).isAir()) {
            level.setBlock(snowPos, snow, Block.UPDATE_CLIENTS);
        }
    }

    /**
     * Nudge planned water sources (a Ladder Island waterfall) into flowing. The block fill places everything with
     * {@code UPDATE_CLIENTS} only, so a source would otherwise sit inert until something disturbs it — scheduling a
     * fluid tick lets physics carry it down the open shaft.
     */
    private void kickFluids() {
        for (final BlockPos pos : plan.fluidTicks()) {
            level.scheduleTick(pos, Fluids.WATER, 1);
        }
    }

    /** Spawn an Animal Island's guaranteed pack inside its enclosure — babies aged down, sheep dyed, fish submerged. */
    private void spawnEnclosureAnimals() {
        for (IslandPlan.AnimalSpawn as : plan.animals()) {
            if (as.inWater()) {
                if (level.getFluidState(as.pos()).isEmpty()) {
                    continue; // the tank water at this spot is missing — skip rather than beach the animal
                }
                final Entity e = as.type().create(level);
                if (e instanceof Mob mob) {
                    mob.moveTo(as.pos().getX() + 0.5, as.pos().getY() + 0.5, as.pos().getZ() + 0.5,
                            plan.random().nextFloat() * 360.0F, 0.0F);
                    applyTraits(mob, as.baby());
                    EventHooks.finalizeMobSpawn(mob, level, level.getCurrentDifficultyAt(as.pos()), MobSpawnType.SPAWNER, null);
                    level.addFreshEntity(mob);
                }
                continue;
            }
            final Entity e = as.type().spawn(level, freeStandSpot(as.pos()), MobSpawnType.SPAWNER);
            if (e != null) {
                applyTraits(e, as.baby());
            }
        }
    }

    /**
     * The position a land mob should spawn at (its feet), given the floor block {@code base} it was planned on.
     * If that column is blocked by a kit block (a cauldron, hay, composter), search outward for a clear column
     * so the mob doesn't spawn inside furniture — e.g. a Witch Hut witch cooking herself in her own cauldron.
     */
    private BlockPos freeStandSpot(BlockPos base) {
        if (standClear(base)) {
            return base.above();
        }
        for (int r = 1; r <= 2; r++) {
            for (int dx = -r; dx <= r; dx++) {
                for (int dz = -r; dz <= r; dz++) {
                    final BlockPos p = base.offset(dx, 0, dz);
                    if (standClear(p)) {
                        return p.above();
                    }
                }
            }
        }
        return base.above();
    }

    /** True if {@code base} is solid footing with two non-colliding blocks above it (room for a standing mob). */
    private boolean standClear(BlockPos base) {
        final BlockPos foot = base.above();
        final BlockPos head = base.above(2);
        return !level.getBlockState(base).getCollisionShape(level, base).isEmpty()
                && level.getBlockState(foot).getCollisionShape(level, foot).isEmpty()
                && level.getBlockState(head).getCollisionShape(level, head).isEmpty();
    }

    private void applyTraits(Entity e, boolean baby) {
        if (baby && e instanceof AgeableMob ageable) {
            ageable.setBaby(true);
        }
        if (e instanceof Sheep sheep) {
            sheep.setColor(DyeColor.byId(plan.random().nextInt(DyeColor.values().length)));
        }
        // Persist so curated spawns stay put — harmless for farm animals (they don't despawn), and it keeps
        // structure-island mobs (a Witch Hut's witch, an Outpost's pillagers) from despawning when unattended.
        if (e instanceof Mob mob) {
            mob.setPersistenceRequired();
        }
    }

    /**
     * Assemble the planned jigsaw structures, then spawn a villager at every bed they placed.
     *
     * <p>Trade-off: unlike the per-block fill, {@link Jigsaw#place} (vanilla {@code JigsawPlacement.generateJigsaw})
     * runs the whole structure
     * in one tick, un-budgeted — a possible frame spike for the big assemblies (Woodland Mansion + wings,
     * Village Center). It's a single vanilla call so it can't be chunked across ticks without reimplementing
     * jigsaw placement; in practice it fires once per island and the spike is acceptable. If it ever becomes a
     * problem, defer large structures by a tick (place them on a later {@link #tick()} than the blocks/trees).
     */
    private void placeStructures() {
        for (IslandPlan.JigsawSite js : plan.jigsaws()) {
            final Holder<StructureTemplatePool> pool = Lookup.templatePool(level.registryAccess(), js.pool());
            final Holder<StructureTemplatePool> fillerPool = js.capFiller().isEmpty() ? null
                    : Lookup.templatePool(level.registryAccess(), ResourceLocation.parse(js.capFiller()));
            Jigsaw.placeCapped(level, pool, js.target(), js.depth(), js.origin(), false,
                    js.capPrefix(), js.capCount(), fillerPool);
            // Re-add any support-dependent trap blocks the jigsaw path would have popped (plate / tripwire).
            Traps.applyAfterJigsaw(level, js.origin());
            // Foundation any solid lot floor left over the void FIRST (while the connective lanes are still markers
            // with empty decks, so they're skipped), THEN resolve the lane markers into terrain-aware paths and
            // over-void bridges — the lanes stay floating bridges, only buildings/fields/gardens get a foundation (§3a).
            if (js.reach() > 0) {
                PathSurfacer.supportFloatingFloors(level, js.origin(), js.reach());
                PathSurfacer.resolve(level, js.origin(), js.reach());
            }
            // Link up any fences / panes / walls (incl. the bridge railings just placed) in their default state.
            linkConnections(level, js.origin(), Math.max(LINK_RADIUS, js.reach()));
            spawnVillagersAtBeds(js.origin(), Math.max(js.pad(), js.reach()));
            for (int i = 0; i < js.ironGolems(); i++) {
                final IronGolem golem = EntityType.IRON_GOLEM.create(level);
                if (golem != null) {
                    final BlockPos spot = golemSpot(js.origin());
                    golem.moveTo(spot.getX() + 0.5, spot.getY(), spot.getZ() + 0.5, 0.0F, 0.0F);
                    golem.setPersistenceRequired();
                    level.addFreshEntity(golem);
                }
            }
        }
    }

    /**
     * Re-derive the connection state of any fences, panes/bars or walls in a just-placed structure's footprint.
     * Jigsaw (structure) placement copies each stored blockstate verbatim with no neighbour update, so a connecting
     * block written in its default state renders as an unconnected post; re-running the vanilla shape update links it
     * to its real in-world neighbours (including the island terrain it sits against). Applied to every structure —
     * a no-op wherever there is nothing to link. Scans a box around the jigsaw origin large enough for our templates.
     */
    public static void linkConnections(ServerLevel level, BlockPos origin) {
        linkConnections(level, origin, LINK_RADIUS);
    }

    /** As {@link #linkConnections(ServerLevel, BlockPos)} but scanning {@code radius} out — sized to the structure. */
    public static void linkConnections(ServerLevel level, BlockPos origin, int radius) {
        final BlockPos.MutableBlockPos p = new BlockPos.MutableBlockPos();
        for (int dy = -LINK_DOWN; dy <= LINK_UP; dy++) {
            for (int dx = -radius; dx <= radius; dx++) {
                for (int dz = -radius; dz <= radius; dz++) {
                    p.set(origin.getX() + dx, origin.getY() + dy, origin.getZ() + dz);
                    final BlockState state = level.getBlockState(p);
                    if (state.getBlock() instanceof CrossCollisionBlock || state.getBlock() instanceof WallBlock) {
                        final BlockState linked = Block.updateFromNeighbourShapes(state, level, p);
                        if (linked != state) {
                            level.setBlock(p.immutable(), linked, Block.UPDATE_CLIENTS);
                        }
                    }
                }
            }
        }
    }

    /**
     * Feet position for a structure's iron golem. The structure floor sits a block below the jigsaw origin, so
     * the golem stands at the origin itself; but it's ~2.7 tall, so spawning it on the floor of a tight pen (the
     * Outpost cage, a 3-tall box) jams its head into the ceiling and suffocates it. Find the lowest level at/above
     * the floor with three clear blocks, dropping the golem to the floor where there's room and only nudging up
     * when the floor column is blocked.
     */
    private BlockPos golemSpot(BlockPos origin) {
        for (int dy = 0; dy <= 4; dy++) {
            final BlockPos feet = origin.above(dy);
            if (clearForGolem(feet)) {
                return feet;
            }
        }
        return origin.above(); // nothing clear nearby — fall back to the old one-up spawn
    }

    /** True if {@code feet} and the two blocks above it have no collision (room for a ~2.7-tall iron golem). */
    private boolean clearForGolem(BlockPos feet) {
        for (int h = 0; h < 3; h++) {
            final BlockPos p = feet.above(h);
            if (!level.getBlockState(p).getCollisionShape(level, p).isEmpty()) {
                return false;
            }
        }
        return true;
    }

    /**
     * One villager per bed in the assembled structure — adult, dressed for the biome, persistent. They
     * arrive unemployed; a villager beside an unclaimed job-site block (the shops carry them) takes up
     * that profession on its own, exactly as in a natural village.
     */
    private void spawnVillagersAtBeds(BlockPos origin, int radius) {
        final BlockPos.MutableBlockPos p = new BlockPos.MutableBlockPos();
        for (int dx = -radius; dx <= radius; dx++) {
            for (int dz = -radius; dz <= radius; dz++) {
                p.set(origin.getX() + dx, origin.getY(), origin.getZ() + dz);
                if (!level.isLoaded(p)) {
                    continue; // a village's reach is wide — don't force-load the empty void around it
                }
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
                    EventHooks.finalizeMobSpawn(mob, level, level.getCurrentDifficultyAt(wp), MobSpawnType.SPAWNER, null);
                    level.addFreshEntity(mob);
                }
                continue;
            }
            final BlockPos surface = ms.pos();
            final BlockPos spawnPos = surface.above();
            // Need solid ground and two blocks of standing room — but plants (flowers, grass, mushrooms)
            // don't block a mob, so allow those; only skip on no ground or a real obstruction (trunk, leaves).
            if (level.getBlockState(surface).isAir() || blocked(spawnPos) || blocked(spawnPos.above())) {
                continue;
            }
            ms.type().spawn(level, spawnPos, MobSpawnType.SPAWNER);
        }
    }

    /** True if a mob can't occupy {@code p}: the block there has a collision shape (air, plants and water don't). */
    private boolean blocked(BlockPos p) {
        return !level.getBlockState(p).getCollisionShape(level, p).isEmpty();
    }
}
