package dev.gemberkoekje.skyseed.entity;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.registry.ModEntities;
import dev.gemberkoekje.skyseed.registry.ModItems;
import dev.gemberkoekje.skyseed.registry.SkyseedRegistries;
import dev.gemberkoekje.skyseed.worldgen.GenerationJob;
import dev.gemberkoekje.skyseed.worldgen.IslandGenerator;
import dev.gemberkoekje.skyseed.worldgen.IslandGrowth;
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
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.Vec3;

/**
 * The thrown Skyseed. It arms for {@link #ARM_DURATION} ticks (~3 s) while flying — or resting, if it
 * lands first — then {@link #germinate()}s: particles, a sound, and (for now) a placeholder stone
 * platform. The real procedural island replaces the placeholder in milestone 4; per-theme content
 * keys off {@link #getTheme()} from milestone 6 on.
 */
public class IslandSeedEntity extends net.minecraft.world.entity.projectile.ThrowableItemProjectile {
    private static final EntityDataAccessor<String> DATA_THEME =
            SynchedEntityData.defineId(IslandSeedEntity.class, EntityDataSerializers.STRING);

    /** Ticks from spawn to germination (~2 s at 20 tps). */
    public static final int ARM_DURATION = 40;

    /** Upward lifts to try if the rest point's volume is already occupied (overlap safety, §5). */
    private static final int[] NUDGE_STEPS = { 0, 8, 16, 24 };

    private int armTicks = 0;

    public IslandSeedEntity(EntityType<? extends IslandSeedEntity> type, Level level) {
        super(type, level);
    }

    /** Spawned from the item's use(). */
    public IslandSeedEntity(Level level, LivingEntity thrower) {
        super(ModEntities.ISLAND_SEED.get(), thrower, level);
    }

    @Override
    protected Item getDefaultItem() {
        return ModItems.ISLAND_SEED.get();
    }

    @Override
    protected void defineSynchedData(SynchedEntityData.Builder builder) {
        super.defineSynchedData(builder); // defines the projectile's item-stack data; required
        builder.define(DATA_THEME, "");
    }

    public void setTheme(ResourceLocation theme) {
        this.entityData.set(DATA_THEME, theme == null ? "" : theme.toString());
    }

    public ResourceLocation getTheme() {
        String s = this.entityData.get(DATA_THEME);
        return s.isEmpty() ? null : ResourceLocation.tryParse(s);
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

        // Overlap safety: try the rest point, then a few lifts, until the volume is clear enough.
        // RNG decorrelated per island (worldSeed ^ center); throwCount folds in later (plan §5).
        final BlockPos base = this.blockPosition();
        IslandPlan plan = null;
        for (int lift : NUDGE_STEPS) {
            BlockPos c = base.above(lift);
            RandomSource random = RandomSource.create(level.getSeed() ^ c.asLong());
            // Island look can vary with the biome it lands in (plan: per-biome overrides).
            IslandPlan candidate = IslandGenerator.planIsland(level, c, theme, level.getBiome(c), random);
            if (!isTooCrowded(level, candidate)) {
                plan = candidate;
                break;
            }
        }
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

        // Tick-budgeted placement: the scheduler grows the island in over the next ticks (plan §5).
        IslandGrowth.enqueue(new GenerationJob(level, plan));

        this.discard();
    }

    /** True if too much of the planned volume is already solid — used to avoid islands growing into each other. */
    private boolean isTooCrowded(ServerLevel level, IslandPlan plan) {
        final int threshold = Math.max(8, plan.blocks().size() / 20); // tolerate a ~5% graze, reject real overlaps
        int hits = 0;
        for (IslandPlan.BlockPlacement bp : plan.blocks()) {
            BlockState state = level.getBlockState(bp.pos());
            if (!state.isAir() && !state.canBeReplaced()) {
                if (++hits > threshold) {
                    return true;
                }
            }
        }
        return false;
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

    /** Resolve this seed's theme from the datapack registry; fall back to forest, then to any theme. */
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
    }

    @Override
    public void readAdditionalSaveData(CompoundTag tag) {
        super.readAdditionalSaveData(tag);
        this.armTicks = tag.getInt("ArmTicks");
        if (tag.contains("Theme")) {
            setTheme(ResourceLocation.tryParse(tag.getString("Theme")));
        }
    }
}
