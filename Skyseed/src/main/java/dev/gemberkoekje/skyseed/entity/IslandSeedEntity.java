package dev.gemberkoekje.skyseed.entity;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.registry.ModEntities;
import dev.gemberkoekje.skyseed.registry.ModItems;
import dev.gemberkoekje.skyseed.registry.SkyseedRegistries;
import dev.gemberkoekje.skyseed.worldgen.GenerationJob;
import dev.gemberkoekje.skyseed.worldgen.IslandGenerator;
import dev.gemberkoekje.skyseed.worldgen.IslandGrowth;
import dev.gemberkoekje.skyseed.worldgen.IslandPlacement;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan;
import dev.gemberkoekje.skyseed.worldgen.theme.IslandTheme;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Registry;
import net.minecraft.core.particles.ParticleTypes;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.network.syncher.EntityDataAccessor;
import net.minecraft.network.syncher.EntityDataSerializers;
import net.minecraft.network.syncher.SynchedEntityData;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.sounds.SoundEvents;
import net.minecraft.sounds.SoundSource;
import net.minecraft.util.RandomSource;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.entity.projectile.ThrowableItemProjectile;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.Vec3;

import java.util.List;

/**
 * The thrown Skyseed. It arms for {@link #ARM_DURATION} ticks (~2 s) while flying — or resting, if it lands
 * first — then germinates: it resolves the seed's {@link #getTheme() theme}, plans a procedural island with
 * {@link IslandGenerator#planIsland}, and hands the plan to a {@link GenerationJob} that grows it into the
 * world over the following ticks (particles and a sound mark the moment). A Precise-mode throw instead flies
 * through everything and germinates at an exact target (see {@code IslandSeedItem}).
 */
public class IslandSeedEntity extends ThrowableItemProjectile {
    private static final EntityDataAccessor<String> DATA_THEME =
            SynchedEntityData.defineId(IslandSeedEntity.class, EntityDataSerializers.STRING);

    /** Ticks from spawn to germination (~2 s at 20 tps). */
    public static final int ARM_DURATION = 40;

    /** Horizontal search: nudge off a collision at most this many times, this far each, before giving up sideways. */
    private static final int MAX_H_ATTEMPTS = 10;
    private static final int H_STEP = 4;
    private static final int MAX_H_DIST = 48; // a "decent" horizontal distance to look before falling back to up/down
    /** Vertical fall-back lifts, only tried when there's no horizontal room: up first, then down. */
    private static final int[] V_FALLBACK = { 8, 16, 24, -8, -16, 32 };

    private int armTicks = 0;

    // Precise mode: fly through everything and germinate at this exact point (see IslandSeedItem).
    private boolean precise = false;
    private double targetX;
    private double targetY;
    private double targetZ;

    public IslandSeedEntity(EntityType<? extends IslandSeedEntity> type, Level level) {
        super(type, level);
    }

    /** Spawned from the item's use(). */
    public IslandSeedEntity(Level level, LivingEntity thrower) {
        super(ModEntities.ISLAND_SEED.get(), thrower, level);
    }

    @Override
    protected Item getDefaultItem() {
        return ModItems.DEFAULT_SEED.get(); // fallback only — the thrown item is set per seed in the network handler
    }

    @Override
    protected void defineSynchedData(SynchedEntityData.Builder builder) {
        super.defineSynchedData(builder); // defines the projectile's item-stack data; required
        builder.define(DATA_THEME, "");
    }

    public void setTheme(ResourceLocation theme) {
        this.entityData.set(DATA_THEME, theme == null ? "" : theme.toString());
    }

    /** @return this seed's theme id, or {@code null} if none was set (a bare/legacy seed). */
    public ResourceLocation getTheme() {
        String s = this.entityData.get(DATA_THEME);
        return s.isEmpty() ? null : ResourceLocation.tryParse(s);
    }

    /** Mark this a Precise-mode throw: it flies through everything and germinates at {@code target}. */
    public void setPreciseTarget(Vec3 target) {
        this.precise = true;
        this.targetX = target.x;
        this.targetY = target.y;
        this.targetZ = target.z;
    }

    @Override
    public void tick() {
        super.tick();
        if (this.level() instanceof ServerLevel serverLevel) {
            this.armTicks++;
            // Sparkle while arming so the countdown reads as "charging".
            if (this.armTicks < ARM_DURATION && this.armTicks % 4 == 0) {
                serverLevel.sendParticles(ParticleTypes.HAPPY_VILLAGER,
                        this.getX(), this.getY() + 0.1, this.getZ(), 2, 0.15, 0.15, 0.15, 0.0);
            }
            if (this.armTicks >= ARM_DURATION) {
                germinate(serverLevel);
            }
        }
    }

    /** Precise seeds pass through everything and germinate at their target; Classic uses block-hit resting. */
    @Override
    protected void onHit(HitResult result) {
        if (this.precise) {
            return;
        }
        super.onHit(result);
    }

    /** On hitting a block, stop and rest there — keep arming until the timer fires. */
    @Override
    protected void onHitBlock(BlockHitResult result) {
        super.onHitBlock(result);
        this.setDeltaMovement(Vec3.ZERO);
        this.setNoGravity(true);
    }

    private void germinate(ServerLevel level) {
        IslandTheme theme = resolveTheme(level);
        if (theme == null) {
            Skyseed.LOGGER.warn("[skyseed] no island themes are loaded — nothing germinated");
            fizzle(level);
            this.discard();
            return;
        }

        // Precise mode: germinate exactly at the chosen target, regardless of where the arc carried us.
        if (precise) {
            this.setPos(targetX, targetY, targetZ);
        }

        // Placement: grow at the rest point / target if it's clear, else nudge horizontally off whatever it would
        // grow into (so islands sit adjacent, not stacked), and only lift up/down if there's no horizontal room.
        final BlockPos base = this.blockPosition();
        // Dimension gate: a seed only grows where it has an implementation (its base dimensions, or a dimension-keyed
        // override). Thrown into a dimension it doesn't implement — an overworld seed in the Nether, say — it fizzles
        // rather than growing the wrong, foreign base island here.
        if (!IslandGenerator.formValidFor(theme, level.getBiome(base), base.getY(), level.dimension().location())) {
            fizzle(level);
            this.discard();
            return;
        }
        final List<Vec3> players = level.players().stream().map(p -> p.position()).toList();
        final BlockPos.MutableBlockPos probe = new BlockPos.MutableBlockPos();
        final IslandPlacement.Occupancy occupied = (x, y, z) -> {
            final BlockState s = level.getBlockState(probe.set(x, y, z));
            return !s.isAir() && !s.canBeReplaced();
        };
        final IslandPlan plan = findClearSpot(level, theme, base, players, occupied);
        if (plan == null) {
            fizzle(level); // nowhere clear to grow — give the seed back rather than carve into things
            this.discard();
            return;
        }

        level.sendParticles(ParticleTypes.HAPPY_VILLAGER, this.getX(), this.getY(), this.getZ(),
                50, 1.2, 1.2, 1.2, 0.25);
        level.sendParticles(ParticleTypes.POOF, this.getX(), this.getY(), this.getZ(),
                25, 0.7, 0.7, 0.7, 0.05);
        level.playSound(null, this.getX(), this.getY(), this.getZ(),
                SoundEvents.BEACON_ACTIVATE, SoundSource.BLOCKS, 1.0F, 1.2F);

        // Tick-budgeted placement: the scheduler grows the island in over the next ticks (README → Generation algorithm).
        IslandGrowth.enqueue(new GenerationJob(level, plan));

        // Cross-dimension twin (the Ruined Portal): grow a matching island at the vanilla-linked coordinate in the
        // other dimension. Spawned directly here, not via another thrown seed, so it never spawns a twin of its own.
        if (theme.twin()) {
            spawnTwin(level, this.blockPosition(), theme);
        }

        this.discard();
    }

    /**
     * Grow a twin island at the 8:1 dimension-linked coordinate in the paired dimension (overworld &harr; nether), so
     * a repaired-and-lit frame on both sides connects with no linking code (SKYNETHERPLAN → Ruined Portal twins). The
     * twin germinates the same theme — the dimension itself decides the form (goodies in the overworld, a bare frame
     * in the Nether). Placement stays close to the linked spot (small steps only) so the portals still link.
     */
    private void spawnTwin(ServerLevel origin, BlockPos center, IslandTheme theme) {
        final net.minecraft.resources.ResourceKey<Level> to;
        if (origin.dimension() == Level.OVERWORLD) {
            to = Level.NETHER;
        } else if (origin.dimension() == Level.NETHER) {
            to = Level.OVERWORLD;
        } else {
            return; // twins only pair the overworld and the Nether
        }
        final ServerLevel other = origin.getServer().getLevel(to);
        if (other == null) {
            return;
        }
        final BlockPos linked = linkedPortalPos(center, to, other);
        if (!IslandGenerator.formValidFor(theme, other.getBiome(linked), linked.getY(), other.dimension().location())) {
            return; // the theme doesn't implement the other dimension — no twin
        }
        final IslandPlan twin = placeTwinNear(other, theme, linked);
        if (twin != null) {
            IslandGrowth.enqueue(new GenerationJob(other, twin));
        }
    }

    /** The vanilla 8:1 cross-dimension coordinate (overworld/8 &harr; nether*8), Y kept and clamped to {@code to}. */
    public static BlockPos linkedPortalPos(BlockPos c, net.minecraft.resources.ResourceKey<Level> to, ServerLevel toLevel) {
        final int x;
        final int z;
        if (to == Level.NETHER) {
            x = Math.floorDiv(c.getX(), 8);
            z = Math.floorDiv(c.getZ(), 8);
        } else {
            x = c.getX() * 8;
            z = c.getZ() * 8;
        }
        int y = c.getY();
        if (to == Level.NETHER) {
            y = net.minecraft.util.Mth.clamp(y, 16, 110); // above the lava sea, below the ceiling
        } else {
            y = net.minecraft.util.Mth.clamp(y, toLevel.getMinBuildHeight() + 8, toLevel.getMaxBuildHeight() - 16);
        }
        return new BlockPos(x, y, z);
    }

    /** Plan the twin as close to {@code linked} as possible — small steps only, so the portal stays in linking range. */
    private static IslandPlan placeTwinNear(ServerLevel level, IslandTheme theme, BlockPos linked) {
        final List<Vec3> players = level.players().stream().map(p -> p.position()).toList();
        final BlockPos.MutableBlockPos probe = new BlockPos.MutableBlockPos();
        final IslandPlacement.Occupancy occupied = (x, y, z) -> {
            final BlockState s = level.getBlockState(probe.set(x, y, z));
            return !s.isAir() && !s.canBeReplaced();
        };
        for (BlockPos c : twinSearchSpots(linked)) {
            final IslandPlan candidate = planAt(level, theme, c);
            if (IslandPlacement.check(candidate, players, occupied).ok()) {
                return candidate;
            }
        }
        // No clear spot close by — grow it at the linked coordinate anyway; sitting on the link is the whole point.
        return planAt(level, theme, linked);
    }

    /** The linked spot first, then a tight ring (small horizontal steps), then a couple of small vertical lifts. */
    private static List<BlockPos> twinSearchSpots(BlockPos linked) {
        final java.util.List<BlockPos> spots = new java.util.ArrayList<>();
        spots.add(linked);
        for (int d = 3; d <= 9; d += 3) {
            spots.add(linked.offset(d, 0, 0));
            spots.add(linked.offset(-d, 0, 0));
            spots.add(linked.offset(0, 0, d));
            spots.add(linked.offset(0, 0, -d));
            spots.add(linked.offset(d, 0, d));
            spots.add(linked.offset(-d, 0, -d));
        }
        for (int lift : new int[] { 6, -6, 12, -12 }) {
            spots.add(linked.above(lift));
        }
        return spots;
    }

    /**
     * Finds a spot the island can grow without interpenetrating existing blocks or burying a player. Tries the base
     * first; on a collision it nudges <em>horizontally</em> away from the centroid of whatever is in the way (so
     * islands end up touching, never stacked), out to {@link #MAX_H_DIST}; only if there's no horizontal room does it
     * fall back to {@link #V_FALLBACK} lifts. Re-plans per position (RNG keyed by centre) and moves the entity to the
     * chosen spot so the germination effects play there. Returns {@code null} if nothing is clear.
     */
    private IslandPlan findClearSpot(ServerLevel level, IslandTheme theme, BlockPos base,
                                     List<Vec3> players, IslandPlacement.Occupancy occupied) {
        BlockPos cursor = base;
        for (int attempt = 0; attempt < MAX_H_ATTEMPTS; attempt++) {
            final IslandPlan candidate = planAt(level, theme, cursor);
            final IslandPlacement.Fit fit = IslandPlacement.check(candidate, players, occupied);
            if (fit.ok()) {
                this.setPos(cursor.getX() + 0.5, cursor.getY() + 0.5, cursor.getZ() + 0.5);
                return candidate;
            }
            // Push the island off whatever it would swallow, horizontally, away from the blocked centroid.
            final double dx = cursor.getX() - fit.blockedX();
            final double dz = cursor.getZ() - fit.blockedZ();
            final double len = Math.sqrt(dx * dx + dz * dz);
            final int sx = len < 1.0e-3 ? H_STEP : (int) Math.round(dx / len * H_STEP);
            final int sz = len < 1.0e-3 ? 0 : (int) Math.round(dz / len * H_STEP);
            cursor = cursor.offset(sx, 0, sz);
            final int ddx = cursor.getX() - base.getX();
            final int ddz = cursor.getZ() - base.getZ();
            if (ddx * ddx + ddz * ddz > MAX_H_DIST * MAX_H_DIST) {
                break; // no horizontal room within a decent distance — fall back to up/down
            }
        }
        for (int lift : V_FALLBACK) {
            final BlockPos c = base.above(lift);
            final IslandPlan candidate = planAt(level, theme, c);
            if (IslandPlacement.check(candidate, players, occupied).ok()) {
                this.setPos(c.getX() + 0.5, c.getY() + 0.5, c.getZ() + 0.5);
                return candidate;
            }
        }
        return null;
    }

    /** Plan the island at {@code c}; RNG is decorrelated per island via {@code worldSeed ^ centre}. */
    private static IslandPlan planAt(ServerLevel level, IslandTheme theme, BlockPos c) {
        final RandomSource random = RandomSource.create(level.getSeed() ^ c.asLong());
        // Island look can vary with the biome it lands in (README → Configuration → Biome overrides).
        return IslandGenerator.planIsland(level, c, theme, level.getBiome(c), random);
    }

    /** Failed germination: a puff of smoke, a fizzle, and the seed dropped back so it isn't wasted. */
    private void fizzle(ServerLevel level) {
        level.sendParticles(ParticleTypes.SMOKE, this.getX(), this.getY(), this.getZ(), 20, 0.3, 0.3, 0.3, 0.02);
        level.playSound(null, this.getX(), this.getY(), this.getZ(),
                SoundEvents.FIRE_EXTINGUISH, SoundSource.BLOCKS, 0.7F, 1.4F);
        ItemStack drop = this.getItem().copy();
        if (!drop.isEmpty()) {
            ItemEntity item = new ItemEntity(level, this.getX(), this.getY(), this.getZ(), drop);
            item.setDefaultPickUpDelay();
            level.addFreshEntity(item);
        }
    }

    /**
     * Resolve this seed's theme from the datapack registry; fall back to forest, then to any theme.
     * @return the resolved theme, or {@code null} only if no themes are loaded at all.
     */
    private IslandTheme resolveTheme(ServerLevel level) {
        Registry<IslandTheme> themes = level.registryAccess().registryOrThrow(SkyseedRegistries.THEME);
        ResourceLocation id = getTheme();
        IslandTheme theme = (id != null) ? themes.get(id) : null;
        if (theme == null) {
            ResourceLocation forest = ResourceLocation.fromNamespaceAndPath(Skyseed.MODID, "forest");
            theme = themes.get(forest);
            if (theme != null && id != null) {
                Skyseed.LOGGER.warn("[skyseed] unknown theme '{}' — falling back to {}", id, forest);
            } else if (theme == null && !themes.holders().findAny().isEmpty()) {
                theme = themes.holders().findFirst().map(h -> h.value()).orElse(null);
            }
        }
        return theme;
    }

    @Override
    public void addAdditionalSaveData(CompoundTag tag) {
        super.addAdditionalSaveData(tag);
        tag.putInt("ArmTicks", this.armTicks);
        ResourceLocation theme = getTheme();
        if (theme != null) {
            tag.putString("Theme", theme.toString());
        }
        tag.putBoolean("Precise", this.precise);
        if (this.precise) {
            tag.putDouble("TX", this.targetX);
            tag.putDouble("TY", this.targetY);
            tag.putDouble("TZ", this.targetZ);
        }
    }

    @Override
    public void readAdditionalSaveData(CompoundTag tag) {
        super.readAdditionalSaveData(tag);
        this.armTicks = tag.getInt("ArmTicks");
        if (tag.contains("Theme")) {
            setTheme(ResourceLocation.tryParse(tag.getString("Theme")));
        }
        this.precise = tag.getBoolean("Precise");
        if (this.precise) {
            this.targetX = tag.getDouble("TX");
            this.targetY = tag.getDouble("TY");
            this.targetZ = tag.getDouble("TZ");
        }
    }
}
