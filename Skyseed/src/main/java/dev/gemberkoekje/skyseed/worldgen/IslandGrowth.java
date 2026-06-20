package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.Skyseed;
import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.fml.common.EventBusSubscriber;
import net.neoforged.neoforge.event.server.ServerStoppingEvent;
import net.neoforged.neoforge.event.tick.ServerTickEvent;

import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;

/**
 * Server-side scheduler for tick-budgeted island placement. Germinating seeds enqueue a
 * {@link GenerationJob}; each server tick every active job places its next budget of blocks until done.
 */
@EventBusSubscriber(modid = Skyseed.MODID)
public final class IslandGrowth {
    private static final List<GenerationJob> JOBS = new ArrayList<>();

    private IslandGrowth() {}

    public static void enqueue(GenerationJob job) {
        JOBS.add(job);
    }

    @SubscribeEvent
    static void onServerTick(ServerTickEvent.Post event) {
        if (JOBS.isEmpty()) {
            return;
        }
        Iterator<GenerationJob> it = JOBS.iterator();
        while (it.hasNext()) {
            if (it.next().tick()) {
                it.remove();
            }
        }
    }

    @SubscribeEvent
    static void onServerStopping(ServerStoppingEvent event) {
        JOBS.clear(); // don't carry jobs into a later server (e.g. another singleplayer world)
    }
}
