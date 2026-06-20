package dev.gemberkoekje.skyseed.entity;

import dev.gemberkoekje.skyseed.registry.ModEntities;
import dev.gemberkoekje.skyseed.registry.ModItems;
import net.minecraft.core.particles.ItemParticleOption;
import net.minecraft.core.particles.ParticleTypes;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.network.syncher.EntityDataAccessor;
import net.minecraft.network.syncher.EntityDataSerializers;
import net.minecraft.network.syncher.SynchedEntityData;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.item.Item;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.HitResult;

/**
 * The thrown Skyseed. Milestone 2: it flies and despawns like a snowball, carrying its theme id.
 * Germination (timer + island generation) arrives in later milestones — for now {@link #onHit}
 * just puffs and discards.
 */
public class IslandSeedEntity extends net.minecraft.world.entity.projectile.ThrowableItemProjectile {
    private static final EntityDataAccessor<String> DATA_THEME =
            SynchedEntityData.defineId(IslandSeedEntity.class, EntityDataSerializers.STRING);

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
    protected void onHit(HitResult result) {
        super.onHit(result);
        if (!this.level().isClientSide) {
            // Milestone 2: no generation yet — a small puff of dirt particles, then despawn.
            this.level().broadcastEntityEvent(this, (byte) 3);
            this.discard();
        }
    }

    @Override
    public void handleEntityEvent(byte id) {
        if (id == 3) {
            ItemParticleOption particle = new ItemParticleOption(ParticleTypes.ITEM, this.getItem());
            for (int i = 0; i < 8; i++) {
                this.level().addParticle(particle,
                        this.getX(), this.getY(), this.getZ(),
                        (this.random.nextDouble() - 0.5) * 0.2,
                        (this.random.nextDouble() - 0.5) * 0.2,
                        (this.random.nextDouble() - 0.5) * 0.2);
            }
        } else {
            super.handleEntityEvent(id);
        }
    }

    @Override
    public void addAdditionalSaveData(CompoundTag tag) {
        super.addAdditionalSaveData(tag);
        ResourceLocation theme = getTheme();
        if (theme != null) {
            tag.putString("Theme", theme.toString());
        }
    }

    @Override
    public void readAdditionalSaveData(CompoundTag tag) {
        super.readAdditionalSaveData(tag);
        if (tag.contains("Theme")) {
            setTheme(ResourceLocation.tryParse(tag.getString("Theme")));
        }
    }
}
