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
import dev.gemberkoekje.skyseed.worldgen.SkyseedWorldData;
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

    /** Upward lifts to try if the rest point's volume is already occupied (overlap safety — see README → Generation algorithm). */
    private static final int[] NUDGE_STEPS = { 0, 8, 16, 24 };

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

        // Overlap safety: try the rest point, then a few lifts, until the spot is clear of other islands and players.
        // RNG decorrelated per island (worldSeed ^ center) — see README → Generation algorithm.
        final SkyseedWorldData data = level.getDataStorage()
                .computeIfAbsent(SkyseedWorldData.factory(), SkyseedWorldData.NAME);
        final BlockPos base = this.blockPosition();
        final List<Vec3> players = level.players().stream().map(p -> p.position()).toList();
        IslandPlan plan = null;
        IslandPlacement.Island footprint = null;
        for (int lift : NUDGE_STEPS) {
            BlockPos c = base.above(lift);
            RandomSource random = RandomSource.create(level.getSeed() ^ c.asLong());
            // Island look can vary with the biome it lands in (README → Configuration → Biome overrides).
            IslandPlan candidate = IslandGenerator.planIsland(level, c, theme, level.getBiome(c), random);
            IslandPlacement.Island candidateFootprint = IslandPlacement.footprint(candidate, c);
            if (!IslandPlacement.tooCrowded(candidateFootprint, data.islands(), players)) {
                plan = candidate;
                footprint = candidateFootprint;
                break;
            }
        }
        if (plan == null) {
            fizzle(level); // nowhere clear to grow — give the seed back rather than carve into things
            this.discard();
            return;
        }
        data.addIsland(footprint); // remember it so later throws keep their distance

        level.sendParticles(ParticleTypes.HAPPY_VILLAGER, this.getX(), this.getY(), this.getZ(),
                50, 1.2, 1.2, 1.2, 0.25);
        level.sendParticles(ParticleTypes.POOF, this.getX(), this.getY(), this.getZ(),
                25, 0.7, 0.7, 0.7, 0.05);
        level.playSound(null, this.getX(), this.getY(), this.getZ(),
                SoundEvents.BEACON_ACTIVATE, SoundSource.BLOCKS, 1.0F, 1.2F);

        // Tick-budgeted placement: the scheduler grows the island in over the next ticks (README → Generation algorithm).
        IslandGrowth.enqueue(new GenerationJob(level, plan));

        this.discard();
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
