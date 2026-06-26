package dev.gemberkoekje.skyseed.event;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.command.SkyseedCommands;
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
        MinecraftServer server = player.getServer();
        if (server == null) {
            return;
        }
        // World-level SavedData (not the player's persistent data) so "first join" survives relogs reliably.
        ServerLevel overworld = server.overworld();
        SkyseedWorldData world = overworld.getDataStorage()
                .computeIfAbsent(SkyseedWorldData.factory(), SkyseedWorldData.NAME);

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
            player.teleportTo(overworld, spawn.getX() + 0.5, spawn.getY(), spawn.getZ() + 0.5,
                    player.getYRot(), player.getXRot());
            world.markSpawned(player.getUUID());
        }

        // Pin the death-respawn to the start island for anyone without their own spawn point (no bed
        // yet). Without this, respawning runs vanilla's area-search around the world spawn, which can
        // drop the player onto a different, nearby island they've built. Sleeping in a bed (or an
        // anchor) sets its own respawn and overrides this.
        if (player.getRespawnPosition() == null) {
            player.setRespawnPosition(overworld.dimension(), spawn, 0.0F, true, false);
        }

        // A world made before the Nether/End were voided still has them as vanilla terrain; a world voided before
        // v0.109.0 has a void End with no central island (no dragon arena / exit fountain). Point the player at the
        // matching one-time in-place fix — each safely wipes/regenerates only what it must on the next reload.
        boolean netherFix = SkyseedCommands.netherNeedsFix(server);
        boolean endFix = SkyseedCommands.endNeedsFix(server);
        if (netherFix || endFix) {
            StringBuilder sb = new StringBuilder("[Skyseed] One-time world fix available (needs cheats/op):");
            if (netherFix) {
                sb.append(" your Nether is still the vanilla dimension — run /emptynether to make it the empty Skyseed Nether.");
            }
            if (endFix) {
                sb.append(" your End is missing its Skyseed centre (the dragon's island + exit portal) — run /emptyend to grow it back.");
            }
            sb.append(" Each regenerates only what it must on the next reload (your overworld is untouched). NOTE: these fix "
                    + "commands will be REMOVED in Skyseed 1.0 — run them before updating, or an un-updated old world can't be "
                    + "repaired this way afterward.");
            player.sendSystemMessage(Component.literal(sb.toString()).withStyle(ChatFormatting.GOLD));
        }
    }
}
