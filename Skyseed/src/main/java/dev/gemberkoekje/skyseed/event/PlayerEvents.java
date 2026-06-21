package dev.gemberkoekje.skyseed.event;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.registry.ModItems;
import dev.gemberkoekje.skyseed.worldgen.SkyseedWorldData;
import net.minecraft.core.BlockPos;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.item.ItemStack;
import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.fml.common.EventBusSubscriber;
import net.neoforged.neoforge.event.entity.player.PlayerEvent;

/** First-join setup: grant the guide book, and put the player on the start island (not in the tree). */
@EventBusSubscriber(modid = Skyseed.MODID)
public final class PlayerEvents {
    private static final String GUIDE_GIVEN = "skyseed:guide_given";
    private static final String START_SPAWNED = "skyseed:start_spawned";

    private PlayerEvents() {}

    @SubscribeEvent
    static void onLogin(PlayerEvent.PlayerLoggedInEvent event) {
        if (!(event.getEntity() instanceof ServerPlayer player)) {
            return;
        }
        CompoundTag data = player.getPersistentData(); // persists across relogs

        if (!data.getBoolean(GUIDE_GIVEN)) {
            ItemStack guide = new ItemStack(ModItems.GUIDE.get());
            if (!player.getInventory().add(guide)) {
                player.drop(guide, false);
            }
            data.putBoolean(GUIDE_GIVEN, true);
        }

        // Resolve the curated start spawn (recorded when the world was first created).
        MinecraftServer server = player.getServer();
        if (server == null) {
            return;
        }
        ServerLevel overworld = server.overworld();
        SkyseedWorldData world = overworld.getDataStorage()
                .computeIfAbsent(SkyseedWorldData.factory(), SkyseedWorldData.NAME);
        BlockPos spawn = world.getStartSpawn();
        if (spawn == null) {
            return; // existing / non-Skyseed world: leave spawning to vanilla
        }

        // On first join, drop the player squarely on the island's grass centre, bypassing vanilla's
        // spawn-fudge (which can land them on top of the tree).
        if (!data.getBoolean(START_SPAWNED)) {
            player.teleportTo(overworld, spawn.getX() + 0.5, spawn.getY(), spawn.getZ() + 0.5,
                    player.getYRot(), player.getXRot());
            data.putBoolean(START_SPAWNED, true);
        }

        // Pin the death-respawn to the start island for anyone without their own spawn point (no bed
        // yet). Without this, respawning runs vanilla's area-search around the world spawn, which can
        // drop the player onto a different, nearby island they've built. Sleeping in a bed (or an
        // anchor) sets its own respawn and overrides this.
        if (player.getRespawnPosition() == null) {
            player.setRespawnPosition(overworld.dimension(), spawn, 0.0F, true, false);
        }
    }
}
