package dev.gemberkoekje.skyseed.entity;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.compat.Ids;
import dev.gemberkoekje.skyseed.compat.Lookup;
import dev.gemberkoekje.skyseed.registry.ModEntities;
import dev.gemberkoekje.skyseed.registry.ModItems;
import dev.gemberkoekje.skyseed.registry.SkyseedRegistries;
import dev.gemberkoekje.skyseed.worldgen.DebugForce;
import dev.gemberkoekje.skyseed.worldgen.GenerationJob;
import dev.gemberkoekje.skyseed.worldgen.IslandGenerator;
import dev.gemberkoekje.skyseed.worldgen.IslandGrowth;
import dev.gemberkoekje.skyseed.worldgen.IslandPlacement;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan;
import dev.gemberkoekje.skyseed.worldgen.TwinPlacer;
import dev.gemberkoekje.skyseed.worldgen.theme.FizzleRule;
import dev.gemberkoekje.skyseed.worldgen.theme.IslandTheme;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.ResourceKey;
import net.minecraft.core.particles.ParticleTypes;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.network.chat.Component;
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
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.entity.projectile.ThrowableItemProjectile;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.biome.Biome;
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
    /** Optional: a biome id a debug seed forces the island to germinate as, in place of the planting biome. */
    private static final EntityDataAccessor<String> DATA_FORCED_BIOME =
            SynchedEntityData.defineId(IslandSeedEntity.class, EntityDataSerializers.STRING);

    private static final EntityDataAccessor<Integer> DATA_FORCED_RARE =
            SynchedEntityData.defineId(IslandSeedEntity.class, EntityDataSerializers.INT);

    private static final EntityDataAccessor<Boolean> DATA_FORCED_WATERFALL =
            SynchedEntityData.defineId(IslandSeedEntity.class, EntityDataSerializers.BOOLEAN);

    /** Ticks from spawn to germination (~2 s at 20 tps). */
    public static final int ARM_DURATION = 40;

    /** Horizontal search: nudge off a collision at most this many times, this far each, before giving up sideways. */
    private static final int MAX_H_ATTEMPTS = 10;
    private static final int H_STEP = 4;
    private static final int MAX_H_DIST = 48; // a "decent" horizontal distance to look before falling back to up/down
    /**
     * Vertical fall-back nudges, only tried when there's no horizontal room: a small, CONTAINED up/down step — never
     * an extreme lift. If even these don't clear it, the seed fizzles back to the thrower rather than stacking high.
     */
    private static final int[] V_FALLBACK = { 8, -8 };

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
        builder.define(DATA_FORCED_BIOME, "");
        builder.define(DATA_FORCED_RARE, -1);
        builder.define(DATA_FORCED_WATERFALL, false);
    }

    public void setTheme(ResourceLocation theme) {
        this.entityData.set(DATA_THEME, theme == null ? "" : theme.toString());
    }

    /** @return this seed's theme id, or {@code null} if none was set (a bare/legacy seed). */
    public ResourceLocation getTheme() {
        String s = this.entityData.get(DATA_THEME);
        return s.isEmpty() ? null : Ids.parse(s);
    }

    /** Force the germinating island to read as this biome (debug seeds only); {@code null} = use the planting biome. */
    public void setForcedBiome(ResourceLocation biome) {
        this.entityData.set(DATA_FORCED_BIOME, biome == null ? "" : biome.toString());
    }

    /** @return the forced biome id, or {@code null} for the normal planting-biome behaviour. */
    public ResourceLocation getForcedBiome() {
        String s = this.entityData.get(DATA_FORCED_BIOME);
        return s.isEmpty() ? null : Ids.parse(s);
    }

    /** Force the rare structure at this index into the theme's {@code rare_structures} (debug seeds); -1 = normal roll. */
    public void setForcedRare(int index) {
        this.entityData.set(DATA_FORCED_RARE, index);
    }

    /** @return the forced rare-structure index, or -1 for the normal chance roll. */
    public int getForcedRare() {
        return this.entityData.get(DATA_FORCED_RARE);
    }

    /** Force the ladder shaft's waterfall variant (debug seeds); false = the normal waterfall-chance roll. */
    public void setForcedWaterfall(boolean force) {
        this.entityData.set(DATA_FORCED_WATERFALL, force);
    }

    /** @return whether the ladder shaft's waterfall variant is forced. */
    public boolean getForcedWaterfall() {
        return this.entityData.get(DATA_FORCED_WATERFALL);
    }

    /** Bundle the debug forcing flags for {@link IslandGenerator#planIsland}. */
    private DebugForce debugForce() {
        return new DebugForce(getForcedRare(), getForcedWaterfall());
    }

    /** A debug seed's forced biome resolved to a holder, or {@code null} for the normal planting-biome behaviour. */
    private Holder<Biome> forcedBiomeHolder(ServerLevel level) {
        ResourceLocation forced = getForcedBiome();
        if (forced == null) {
            return null;
        }
        return level.registryAccess().registryOrThrow(Registries.BIOME)
                .getHolder(ResourceKey.create(Registries.BIOME, forced))
                .<Holder<Biome>>map(ref -> ref)
                .orElse(null);
    }

    /** The biome the island should generate as at {@code pos}: a debug seed's forced biome, else the planting biome. */
    private Holder<Biome> biomeAt(ServerLevel level, BlockPos pos) {
        Holder<Biome> forced = forcedBiomeHolder(level);
        return forced != null ? forced : level.getBiome(pos);
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
        final Holder<Biome> biome = biomeAt(level, base);
        // Dimension gate: a seed only grows where it has an implementation (its base dimensions, or a dimension-keyed
        // override). Thrown into a dimension it doesn't implement — an overworld seed in the Nether, say — it fizzles
        // rather than growing the wrong, foreign base island here. A `fizzle` biome rule also excludes specific biomes
        // (the Bastion never forms in the basalt deltas), showing the thrower its own action-bar message.
        if (!IslandGenerator.formValidFor(theme, biome, base.getY(), level.dimension().location())) {
            if (theme.fizzlesIn(biome) && this.getOwner() instanceof Player thrower) {
                theme.fizzle().flatMap(FizzleRule::message)
                        .ifPresent(key -> thrower.displayClientMessage(Component.translatable(key), true));
            }
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

        // Cross-dimension twin (the Ruined Portal): grow the plan's twin theme at the vanilla-linked coordinate in
        // the other dimension. The plan carries it whether it came from the seed's own theme (the dedicated portal
        // seed) or a rolled rare structure (a portal that surfaced on a big island). Spawned directly here, not via
        // another thrown seed, so it never spawns a twin of its own.
        if (plan.twinTheme().isPresent()) {
            final IslandTheme twinTheme = Lookup.byId(
                    Lookup.registry(level.registryAccess(), SkyseedRegistries.THEME), plan.twinTheme().get());
            if (twinTheme != null) {
                TwinPlacer.spawnTwin(level, this.blockPosition(), twinTheme);
            }
        }

        this.discard();
    }

    /**
     * Finds a spot the island can grow without interpenetrating existing blocks or burying a player. Tries the base
     * first; on a collision it nudges <em>horizontally</em> away from the centroid of whatever is in the way (so
     * islands end up touching, never stacked), out to {@link #MAX_H_DIST}; only if there's no horizontal room does it
     * try a contained {@link #V_FALLBACK} nudge (a few blocks up or down — never an extreme lift). Re-plans per
     * position (RNG keyed by centre) and moves the entity to the chosen spot so the germination effects play there.
     * Returns {@code null} if nothing is clear within those margins — the caller then fizzles the seed back to the
     * thrower instead of shoving the island high.
     */
    private IslandPlan findClearSpot(ServerLevel level, IslandTheme theme, BlockPos base,
                                     List<Vec3> players, IslandPlacement.Occupancy occupied) {
        BlockPos cursor = base;
        for (int attempt = 0; attempt < MAX_H_ATTEMPTS; attempt++) {
            final IslandPlan candidate = planAt(level, theme, cursor, forcedBiomeHolder(level), debugForce());
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
            final IslandPlan candidate = planAt(level, theme, c, forcedBiomeHolder(level), debugForce());
            if (IslandPlacement.check(candidate, players, occupied).ok()) {
                this.setPos(c.getX() + 0.5, c.getY() + 0.5, c.getZ() + 0.5);
                return candidate;
            }
        }
        return null;
    }

    /** Plan the island at {@code c} as {@code forced} (a debug seed's biome) or, when null, the planting biome at
     *  {@code c}; RNG is decorrelated per island via {@code worldSeed ^ centre}. */
    private static IslandPlan planAt(ServerLevel level, IslandTheme theme, BlockPos c, Holder<Biome> forced, DebugForce force) {
        final RandomSource random = RandomSource.create(level.getSeed() ^ c.asLong());
        return IslandGenerator.planIsland(level, c, theme, forced != null ? forced : level.getBiome(c), random, force);
    }

    /**
     * Failed germination: a puff of smoke, a fizzle, and the seed handed back to the thrower so it isn't wasted.
     *
     * <p>Almost every fizzle happens over the void (you're throwing into empty sky), where a dropped item would just
     * tumble out of reach — so we return the seed to whoever threw it: into their inventory, or at their feet if it's
     * full. Only a seed with no player thrower (e.g. a dispenser) falls back to dropping where it failed.
     */
    private void fizzle(ServerLevel level) {
        level.sendParticles(ParticleTypes.SMOKE, this.getX(), this.getY(), this.getZ(), 20, 0.3, 0.3, 0.3, 0.02);
        level.playSound(null, this.getX(), this.getY(), this.getZ(),
                SoundEvents.FIRE_EXTINGUISH, SoundSource.BLOCKS, 0.7F, 1.4F);
        final ItemStack drop = this.getItem().copy();
        if (drop.isEmpty()) {
            return;
        }
        if (this.getOwner() instanceof Player thrower) {
            if (thrower.getInventory().add(drop)) {
                // a soft "bloop" at the thrower so they notice the seed came back to them
                level.playSound(null, thrower.getX(), thrower.getY(), thrower.getZ(),
                        SoundEvents.ITEM_PICKUP, SoundSource.PLAYERS, 0.6F, 1.6F);
            } else {
                // inventory full — drop at their feet (reachable) rather than into the void
                dropSeed(level, thrower.getX(), thrower.getY(), thrower.getZ(), drop);
            }
            return;
        }
        // No player thrower (e.g. a dispenser): best-effort drop where it fizzled.
        dropSeed(level, this.getX(), this.getY(), this.getZ(), drop);
    }

    private static void dropSeed(ServerLevel level, double x, double y, double z, ItemStack stack) {
        final ItemEntity item = new ItemEntity(level, x, y, z, stack);
        item.setDefaultPickUpDelay();
        level.addFreshEntity(item);
    }

    /**
     * Resolve this seed's theme from the datapack registry; fall back to forest, then to any theme.
     * @return the resolved theme, or {@code null} only if no themes are loaded at all.
     */
    private IslandTheme resolveTheme(ServerLevel level) {
        Registry<IslandTheme> themes = Lookup.registry(level.registryAccess(), SkyseedRegistries.THEME);
        ResourceLocation id = getTheme();
        IslandTheme theme = (id != null) ? themes.get(id) : null;
        if (theme == null) {
            ResourceLocation forest = Ids.mod("forest");
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
        ResourceLocation forced = getForcedBiome();
        if (forced != null) {
            tag.putString("ForcedBiome", forced.toString());
        }
        if (getForcedRare() >= 0) {
            tag.putInt("ForcedRare", getForcedRare());
        }
        if (getForcedWaterfall()) {
            tag.putBoolean("ForcedWaterfall", true);
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
            setTheme(Ids.parse(tag.getString("Theme")));
        }
        if (tag.contains("ForcedBiome")) {
            setForcedBiome(Ids.parse(tag.getString("ForcedBiome")));
        }
        if (tag.contains("ForcedRare")) {
            setForcedRare(tag.getInt("ForcedRare"));
        }
        if (tag.contains("ForcedWaterfall")) {
            setForcedWaterfall(tag.getBoolean("ForcedWaterfall"));
        }
        this.precise = tag.getBoolean("Precise");
        if (this.precise) {
            this.targetX = tag.getDouble("TX");
            this.targetY = tag.getDouble("TY");
            this.targetZ = tag.getDouble("TZ");
        }
    }
}
