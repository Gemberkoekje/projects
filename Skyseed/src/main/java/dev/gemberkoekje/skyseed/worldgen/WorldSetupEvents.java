package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.fml.common.EventBusSubscriber;
import net.neoforged.neoforge.event.server.ServerStartedEvent;

/** Places the curated starting island once, when a brand-new world is first created (plan §8). */
@EventBusSubscriber(modid = Skyseed.MODID)
public final class WorldSetupEvents {
    private static final BlockPos START_CENTER = new BlockPos(8, 100, 8);

    private WorldSetupEvents() {}

    @SubscribeEvent
    static void onServerStarted(ServerStartedEvent event) {
        ServerLevel overworld = event.getServer().overworld();
        SkyseedWorldData data = overworld.getDataStorage()
                .computeIfAbsent(SkyseedWorldData.factory(), SkyseedWorldData.NAME);
        if (data.isStartPlaced()) {
            return;
        }
        // Only a brand-new world (no ticks elapsed yet) — never retrofit an existing save.
        if (overworld.getGameTime() == 0L) {
            BlockPos spawn = StartIsland.build(overworld, START_CENTER);
            overworld.setDefaultSpawnPos(spawn, 0.0F);
            Skyseed.LOGGER.info("[skyseed] placed curated starting island; spawn set to {}", spawn);
        } else {
            Skyseed.LOGGER.info("[skyseed] existing world (gameTime={}), leaving spawn untouched", overworld.getGameTime());
        }
        data.markStartPlaced();
    }
}
