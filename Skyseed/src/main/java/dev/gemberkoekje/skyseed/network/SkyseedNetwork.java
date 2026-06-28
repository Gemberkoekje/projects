package dev.gemberkoekje.skyseed.network;

import dev.gemberkoekje.skyseed.entity.IslandSeedEntity;
import dev.gemberkoekje.skyseed.item.IslandSeedItem;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.sounds.SoundEvents;
import net.minecraft.sounds.SoundSource;
import net.minecraft.stats.Stats;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.phys.Vec3;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.neoforge.network.event.RegisterPayloadHandlersEvent;
import net.neoforged.neoforge.network.handling.IPayloadContext;
import net.neoforged.neoforge.network.registration.PayloadRegistrar;

/** Registers and handles the Skyseed throw packet. The server is authoritative: it spawns the seed. */
public final class SkyseedNetwork {
    private SkyseedNetwork() {}

    public static void register(IEventBus modEventBus) {
        modEventBus.addListener(SkyseedNetwork::onRegister);
    }

    private static void onRegister(RegisterPayloadHandlersEvent event) {
        PayloadRegistrar registrar = event.registrar("1");
        registrar.playToServer(ThrowSeedPayload.TYPE, ThrowSeedPayload.STREAM_CODEC, SkyseedNetwork::handleThrow);
    }

    private static void handleThrow(ThrowSeedPayload pkt, IPayloadContext ctx) {
        ctx.enqueueWork(() -> {
            if (!(ctx.player() instanceof ServerPlayer player) || !(player.level() instanceof ServerLevel level)) {
                return;
            }
            final InteractionHand hand = pkt.hand() == 1 ? InteractionHand.OFF_HAND : InteractionHand.MAIN_HAND;
            final ItemStack stack = player.getItemInHand(hand);
            if (!(stack.getItem() instanceof IslandSeedItem seedItem)) {
                return; // the player isn't holding a seed in that hand any more — ignore
            }
            final float power = Math.min(1.0F, pkt.heldTicks() / (float) IslandSeedItem.CHARGE_TICKS);

            final IslandSeedEntity seed = new IslandSeedEntity(level, player);
            seed.setItem(stack);
            seed.setTheme(seedItem.theme());
            seed.setForcedBiome(seedItem.forcedBiome());
            seed.setForcedRare(seedItem.forcedRareIndex());
            seed.setForcedWaterfall(seedItem.forcedWaterfall());

            if (pkt.precise()) {
                final Vec3 eye = player.getEyePosition();
                Vec3 target = new Vec3(pkt.tx(), pkt.ty(), pkt.tz());
                // Anti-cheat: never trust a target further than the max range, clamp along its direction.
                if (eye.distanceTo(target) > IslandSeedItem.MAX_DISTANCE + 2.0) {
                    target = eye.add(target.subtract(eye).normalize().scale(IslandSeedItem.MAX_DISTANCE));
                }
                seed.setPreciseTarget(target);
                seed.setDeltaMovement(IslandSeedItem.requiredVelocity(seed.position(), target));
                seed.hurtMarked = true; // force a velocity sync to clients (hasImpulse was removed in 26.1.2)
            } else {
                final float velocity = IslandSeedItem.MIN_VELOCITY + power * (IslandSeedItem.MAX_VELOCITY - IslandSeedItem.MIN_VELOCITY);
                seed.shootFromRotation(player, player.getXRot(), player.getYRot(), 0.0F, velocity, 1.0F);
            }

            level.addFreshEntity(seed);
            level.playSound(null, player.getX(), player.getY(), player.getZ(),
                    SoundEvents.SNOWBALL_THROW, SoundSource.NEUTRAL, 0.6F, 0.7F + power * 0.6F);
            player.awardStat(Stats.ITEM_USED.get(stack.getItem()));
            stack.consume(1, player);
        });
    }
}
