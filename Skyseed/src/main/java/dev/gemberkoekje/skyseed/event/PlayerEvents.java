package dev.gemberkoekje.skyseed.event;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.command.SkyseedCommands;
import dev.gemberkoekje.skyseed.compat.Players;
import dev.gemberkoekje.skyseed.item.SkyseedGuide;
import dev.gemberkoekje.skyseed.worldgen.SkyseedWorldData;
import net.minecraft.ChatFormatting;
import net.minecraft.core.BlockPos;
import net.minecraft.network.chat.Component;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.item.ItemStack;
import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.fml.common.EventBusSubscriber;
import net.neoforged.neoforge.event.entity.player.PlayerEvent;

/** First-join setup: grant the guide book once, and put the player on the start island (not in the tree). */
@EventBusSubscriber(modid = Skyseed.MODID)
public final class PlayerEvents {
    private PlayerEvents() {}

    @SubscribeEvent
    static void onLogin(PlayerEvent.PlayerLoggedInEvent event) {
        if (!(event.getEntity() instanceof ServerPlayer player)) {
            return;
        }
        MinecraftServer server = player.level().getServer(); // Entity.getServer() was removed in 26.1.2
        if (server == null) {
            return;
        }
        // World-level SavedData (not the player's persistent data) so "first join" survives relogs reliably.
        ServerLevel overworld = server.overworld();
        //? if >=26.1.2 {
        /*SkyseedWorldData world = overworld.getDataStorage().computeIfAbsent(SkyseedWorldData.TYPE);*/
        //?} else {
        SkyseedWorldData world = overworld.getDataStorage()
                .computeIfAbsent(SkyseedWorldData.factory(), SkyseedWorldData.NAME);
        //?}

        if (!world.hasGuided(player.getUUID())) {
            ItemStack guide = SkyseedGuide.book();
            if (!player.getInventory().add(guide)) {
                player.drop(guide, false);
            }
            world.markGuided(player.getUUID());
        }

        BlockPos spawn = world.getStartSpawn();
        if (spawn == null) {
            return; // existing / non-Skyseed world: leave spawning to vanilla
        }

        // On first join, drop the player squarely on the island's grass centre, bypassing vanilla's
        // spawn-fudge (which can land them on top of the tree).
        if (!world.hasSpawned(player.getUUID())) {
            Players.teleport(player, overworld, spawn.getX() + 0.5, spawn.getY(), spawn.getZ() + 0.5,
                    player.getYRot(), player.getXRot());
            world.markSpawned(player.getUUID());
        }

        // Pin the death-respawn to the start island for anyone without their own spawn point (no bed
        // yet). Without this, respawning runs vanilla's area-search around the world spawn, which can
        // drop the player onto a different, nearby island they've built. Sleeping in a bed (or an
        // anchor) sets its own respawn and overrides this.
        //? if >=26.1.2 {
        /*if (player.getRespawnConfig() == null) {
            player.setRespawnPosition(new net.minecraft.server.level.ServerPlayer.RespawnConfig(
                    net.minecraft.world.level.storage.LevelData.RespawnData.of(overworld.dimension(), spawn, 0.0F, 0.0F), true), false);
        }*/
        //?} else {
        if (player.getRespawnPosition() == null) {
            player.setRespawnPosition(overworld.dimension(), spawn, 0.0F, true, false);
        }
        //?}

        // A world made before the Nether/End were voided still has them as vanilla terrain — offer the matching
        // in-place conversion (safely wipes/regenerates only that dimension on the next reload). Per-dimension so the
        // player sees only the fix they actually need.
        boolean netherConvert = SkyseedCommands.netherNeedsConvert(server);
        boolean endConvert = SkyseedCommands.endNeedsConvert(server);
        if (netherConvert || endConvert) {
            StringBuilder sb = new StringBuilder("[Skyseed] One-time conversion available (needs cheats/op):");
            if (netherConvert) {
                sb.append(" your Nether is still the vanilla dimension — run /emptynether to make it the empty Skyseed Nether.");
            }
            if (endConvert) {
                sb.append(" your End is still the vanilla dimension — run /emptyend to make it the empty Skyseed End.");
            }
            sb.append(" Each regenerates only that dimension on the next reload (your overworld is untouched). NOTE: these "
                    + "conversion commands will be REMOVED in Skyseed 1.0 — run them before updating, or an un-updated old "
                    + "world can't be converted this way afterward.");
            player.sendSystemMessage(Component.literal(sb.toString()).withStyle(ChatFormatting.GOLD));
        }

        // A void End generated before v0.109.0 has no central island, so the dragon's exit fountain never forms and the
        // four-crystal respawn has nothing to attach to: that dragon can only be fought once. New worlds are fine.
        if (SkyseedCommands.endIsOneShotDragon(server)) {
            player.sendSystemMessage(Component.literal(
                    "[Skyseed] Heads up: this world's End was generated before its central island existed, so its Ender "
                  + "Dragon can be fought only ONCE — the respawn needs the central fountain, which this End is missing. "
                  + "If you'd like to repeat the dragon fight, start a new world (new Ends grow the island and a "
                  + "refightable dragon).")
                  .withStyle(ChatFormatting.GOLD));
        }
    }
}
