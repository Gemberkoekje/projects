package dev.gemberkoekje.skyseed.item;

import dev.gemberkoekje.skyseed.SkyseedClientConfig;
import dev.gemberkoekje.skyseed.entity.IslandSeedEntity;
import dev.gemberkoekje.skyseed.network.ThrowSeedPayload;
import net.minecraft.world.InteractionHand;
//? if >=26.1.2 {
/*import net.minecraft.world.InteractionResult;*/
//?} else {
import net.minecraft.world.InteractionResultHolder;
//?}
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.player.Player;
import dev.gemberkoekje.skyseed.compat.Id;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
//? if >=26.1.2 {
/*import net.minecraft.world.item.ItemUseAnimation;*/
//?} else {
import net.minecraft.world.item.UseAnim;
//?}
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.Vec3;
import net.neoforged.neoforge.network.PacketDistributor;

/**
 * The Skyseed item. Hold right-click to wind up, release to throw. Two throw modes (a player keybind
 * preference, see {@link SkyseedClientConfig}): <b>Classic</b> launches a charged physics arc;
 * <b>Precise</b> places the island directly along the look vector at a charge-scaled distance. On
 * release the client sends a {@link ThrowSeedPayload}; the server spawns the entity.
 * <p>Each theme is its own item (better JEI/REI visibility, and add-on mods can register more); the
 * island it germinates into is fixed per item via {@link #theme()}. Add-ons should also add their seed
 * to the {@code skyseed:skyseeds} item tag so it converts into the guide.
 */
public class IslandSeedItem extends Item {
    public static final int CHARGE_TICKS = 25;      // hold time to reach full power (~1.25 s)
    public static final float MIN_VELOCITY = 0.5F;  // Classic: a quick tap — lands close
    public static final float MAX_VELOCITY = 2.5F;  // Classic: fully wound up — flies far
    public static final double MIN_DISTANCE = 5.0;  // Precise: a tap places the island this near
    public static final double MAX_DISTANCE = 40.0; // Precise: a full charge places it this far

    private final Id theme;
    private final Id forcedBiome;
    private final int forcedRareIndex;
    private final boolean forcedWaterfall;
    private final String debugLabel;

    public IslandSeedItem(Properties properties, Id theme) {
        this(properties, theme, null);
    }

    public IslandSeedItem(Properties properties, Id theme, Id forcedBiome) {
        this(properties, theme, forcedBiome, -1, false, null);
    }

    /**
     * Full constructor for an auto-generated debug seed: {@code forcedRareIndex} (an index into the theme's
     * {@code rare_structures}, or -1) forces a chance-gated structure, {@code forcedWaterfall} forces the ladder
     * shaft's waterfall variant, and {@code debugLabel} (non-null) gives it a composed, lang-free display name. See
     * {@link dev.gemberkoekje.skyseed.registry.ThemeScanner}.
     */
    public IslandSeedItem(Properties properties, Id theme, Id forcedBiome,
                          int forcedRareIndex, boolean forcedWaterfall, String debugLabel) {
        super(properties);
        this.theme = theme;
        this.forcedBiome = forcedBiome;
        this.forcedRareIndex = forcedRareIndex;
        this.forcedWaterfall = forcedWaterfall;
        this.debugLabel = debugLabel;
    }

    /** The island theme this seed germinates into (a {@code skyseed:theme} datapack-registry id). */
    public Id theme() {
        return theme;
    }

    /** A biome this (debug) seed forces its island to germinate as, ignoring where it's thrown; null = normal. */
    public Id forcedBiome() {
        return forcedBiome;
    }

    /** A rare-structure index this (debug) seed forces its island to germinate, bypassing the chance roll; -1 = none. */
    public int forcedRareIndex() {
        return forcedRareIndex;
    }

    /** Whether this (debug) seed forces the ladder shaft's waterfall variant (bypassing its chance). */
    public boolean forcedWaterfall() {
        return forcedWaterfall;
    }

    @Override
    public net.minecraft.network.chat.Component getName(ItemStack stack) {
        return debugLabel != null
                ? net.minecraft.network.chat.Component.literal("Debug: " + debugLabel)
                : super.getName(stack);
    }

    //? if >=26.1.2 {
    /*@Override
    public InteractionResult use(Level level, Player player, InteractionHand hand) {
        player.startUsingItem(hand);
        return InteractionResult.CONSUME;
    }*/
    //?} else {
    @Override
    public InteractionResultHolder<ItemStack> use(Level level, Player player, InteractionHand hand) {
        player.startUsingItem(hand);
        return InteractionResultHolder.consume(player.getItemInHand(hand));
    }
    //?}

    @Override
    public int getUseDuration(ItemStack stack, LivingEntity entity) {
        return 72000; // hold as long as you like; the throw happens on release
    }

    //? if >=26.1.2 {
    /*@Override
    public ItemUseAnimation getUseAnimation(ItemStack stack) {
        return ItemUseAnimation.SPEAR; // raise-to-throw, like a trident
    }*/
    //?} else {
    @Override
    public UseAnim getUseAnimation(ItemStack stack) {
        return UseAnim.SPEAR; // raise-to-throw, like a trident
    }
    //?}

    @Override
    public void releaseUsing(ItemStack stack, Level level, LivingEntity entity, int timeLeft) {
        if (!(entity instanceof Player player) || !level.isClientSide()) {
            return; // the server spawns the seed from the packet the client sends below
        }
        final int heldTicks = getUseDuration(stack, entity) - timeLeft;
        final boolean precise = SkyseedClientConfig.PRECISE_THROW.get();
        final InteractionHand hand = player.getMainHandItem() == stack ? InteractionHand.MAIN_HAND : InteractionHand.OFF_HAND;
        final Vec3 target = precise ? preciseTarget(player, heldTicks) : Vec3.ZERO;
        PacketDistributor.sendToServer(new ThrowSeedPayload(
                precise, heldTicks, target.x, target.y, target.z, hand == InteractionHand.OFF_HAND ? 1 : 0));
    }

    /** Precise mode: the germination point along the player's look, at a charge-scaled distance. */
    public static Vec3 preciseTarget(Player player, int heldTicks) {
        final double power = Math.min(1.0, heldTicks / (double) CHARGE_TICKS);
        final double distance = MIN_DISTANCE + power * (MAX_DISTANCE - MIN_DISTANCE);
        return player.getEyePosition().add(player.getLookAngle().scale(distance));
    }

    /**
     * Precise mode: an initial velocity that lobs the seed toward {@code target} so it arrives there
     * after the arm time under normal gravity (visual only — germination snaps to the exact target).
     * Capped so near-vertical throws don't fling the seed absurdly far.
     */
    public static Vec3 requiredVelocity(Vec3 spawn, Vec3 target) {
        final double flight = IslandSeedEntity.ARM_DURATION;
        final double gravity = 0.03; // ThrowableItemProjectile default
        final Vec3 toTarget = target.subtract(spawn);
        final Vec3 gravityCorrection = new Vec3(0.0, gravity * flight * flight / 2.0, 0.0);
        Vec3 v = toTarget.add(gravityCorrection).scale(1.0 / flight);
        final double cap = 4.0;
        if (v.length() > cap) {
            v = v.normalize().scale(cap);
        }
        return v;
    }
}
