package dev.gemberkoekje.skyseed.network;

import dev.gemberkoekje.skyseed.compat.Ids;
import io.netty.buffer.ByteBuf;
import net.minecraft.network.codec.ByteBufCodecs;
import net.minecraft.network.codec.StreamCodec;
import net.minecraft.network.protocol.common.custom.CustomPacketPayload;

/**
 * Sent client -> server when a Skyseed is released. Carries the chosen throw mode, the charge (held
 * ticks), the precise-mode target point (the client computes it from look + charge), and which hand
 * the seed is in. The server validates and spawns the entity. {@code tx/ty/tz} are unused for Classic.
 */
public record ThrowSeedPayload(boolean precise, int heldTicks, double tx, double ty, double tz, int hand)
        implements CustomPacketPayload {

    public static final Type<ThrowSeedPayload> TYPE =
            new Type<>(Ids.mod("throw_seed"));

    public static final StreamCodec<ByteBuf, ThrowSeedPayload> STREAM_CODEC = StreamCodec.composite(
            ByteBufCodecs.BOOL, ThrowSeedPayload::precise,
            ByteBufCodecs.VAR_INT, ThrowSeedPayload::heldTicks,
            ByteBufCodecs.DOUBLE, ThrowSeedPayload::tx,
            ByteBufCodecs.DOUBLE, ThrowSeedPayload::ty,
            ByteBufCodecs.DOUBLE, ThrowSeedPayload::tz,
            ByteBufCodecs.VAR_INT, ThrowSeedPayload::hand,
            ThrowSeedPayload::new);

    @Override
    public Type<ThrowSeedPayload> type() {
        return TYPE;
    }
}
