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

        // On first join of a skyblock world, drop the player squarely on the island's grass centre,
        // bypassing vanilla's spawn-fudge (which can land them on top of the tree).
        if (!data.getBoolean(START_SPAWNED)) {
            MinecraftServer server = player.getServer();
            if (server != null) {
                ServerLevel overworld = server.overworld();
                SkyseedWorldData world = overworld.getDataStorage()
                        .computeIfAbsent(SkyseedWorldData.factory(), SkyseedWorldData.NAME);
                BlockPos spawn = world.getStartSpawn();
                if (spawn != null) {
                    player.teleportTo(overworld, spawn.getX() + 0.5, spawn.getY(), spawn.getZ() + 0.5,
                            player.getYRot(), player.getXRot());
                    data.putBoolean(START_SPAWNED, true);
                }
            }
        }
    }
}
