package dev.gemberkoekje.skyseed.client;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.registry.ModEntities;
import net.minecraft.client.renderer.entity.ThrownItemRenderer;
import net.neoforged.api.distmarker.Dist;
import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.fml.common.EventBusSubscriber;
import net.neoforged.neoforge.client.event.EntityRenderersEvent;

/** Client-only mod-bus event handlers. Registers the renderer for the thrown Skyseed.
 *  (RegisterRenderers is an IModBusEvent, so NeoForge auto-routes it to the mod bus.) */
@EventBusSubscriber(modid = Skyseed.MODID, value = Dist.CLIENT)
public final class SkyseedClientEvents {
    private SkyseedClientEvents() {}

    @SubscribeEvent
    static void onRegisterRenderers(EntityRenderersEvent.RegisterRenderers event) {
        // ThrownItemRenderer draws the projectile as its item (the dirt-ball icon), like a snowball.
        event.registerEntityRenderer(ModEntities.ISLAND_SEED.get(), ThrownItemRenderer::new);
    }
}
