package dev.gemberkoekje.skyseed.compat;

import net.minecraft.network.chat.Component;
//? if >=26.1.2 {
/*import net.minecraft.server.level.ServerPlayer;*/
//?}
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
}
