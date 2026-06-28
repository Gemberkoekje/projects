package dev.gemberkoekje.skyseed.compat;

import net.minecraft.network.chat.Component;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.player.Player;

/**
 * Version-volatile player messaging. {@code Player.displayClientMessage(Component, boolean)} was removed in 26.1.2 in
 * favour of {@code ServerPlayer.sendSystemMessage(Component, boolean overlay)}; the action-bar swap lives here.
 */
public final class Players {
    private Players() {}

    /** Show {@code msg} on the player's action bar (the seed-fizzle notice). A no-op for a non-server player on 26.1.2. */
    public static void actionBar(Player player, Component msg) {
        //? if >=26.1.2 {
        /*if (player instanceof ServerPlayer sp) {
            sp.sendSystemMessage(msg, true);
        }*/
        //?} else {
        player.displayClientMessage(msg, true);
        //?}
    }

    /** Teleport a player within a level. 26.1.2's {@code teleportTo} gained a {@code Set<Relative>} + a
     *  {@code resetCamera} flag (an absolute teleport with no relatives, no camera reset). */
    public static void teleport(ServerPlayer player, ServerLevel level, double x, double y, double z, float yRot, float xRot) {
        //? if >=26.1.2 {
        /*player.teleportTo(level, x, y, z, java.util.Set.of(), yRot, xRot, false);*/
        //?} else {
        player.teleportTo(level, x, y, z, yRot, xRot);
        //?}
    }
}
