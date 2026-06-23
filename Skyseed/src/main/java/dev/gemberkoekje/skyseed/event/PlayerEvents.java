package dev.gemberkoekje.skyseed.event;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.item.SkyseedGuide;
import dev.gemberkoekje.skyseed.worldgen.SkyseedWorldData;
import dev.gemberkoekje.skyseed.worldgen.WorldSetupEvents;
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

        // Skyseed world made before the Nether/End were voided (pre-0.35.x): those dimensions are baked
        // into the save as vanilla terrain. Offer the in-place /emptynether|/emptyend conversion as a genuine
        // equal to starting fresh — the reset is hardened against interruption (only level.dat + that
        // dimension's chunks are touched), so it's a normal way to fix an old world, not a last resort.
        if (WorldSetupEvents.hasLegacyDimensions(server)) {
            player.sendSystemMessage(Component.literal(
                    "[Skyseed] This world predates the empty Nether and End, so those two are still the vanilla "
                  + "dimensions (your overworld is unaffected). Two one-time fixes, both fine to use: start a new "
                  + "world, or keep this one and convert it in place with /emptynether and /emptyend (needs "
                  + "cheats/op) — that safely wipes and regenerates just that dimension on the next reload, "
                  + "costing only what you've built there.")
                  .withStyle(ChatFormatting.GOLD));
        }
    }
}
