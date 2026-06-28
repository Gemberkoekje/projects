package dev.gemberkoekje.skyseed.client;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.SkyseedClientConfig;
import dev.gemberkoekje.skyseed.compat.Ids;
import dev.gemberkoekje.skyseed.registry.ModEntities;
import dev.gemberkoekje.skyseed.registry.ModItems;
import net.minecraft.client.Minecraft;
//? if <26.1.2 {
import net.minecraft.client.resources.model.ModelResourceLocation;
//?}
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.worldselection.CreateWorldScreen;
import net.minecraft.client.gui.screens.worldselection.WorldCreationUiState;
import net.minecraft.client.renderer.entity.ThrownItemRenderer;
import net.minecraft.core.registries.Registries;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.ResourceKey;
import net.minecraft.world.level.levelgen.presets.WorldPreset;
import net.neoforged.api.distmarker.Dist;
import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.fml.common.EventBusSubscriber;
import net.neoforged.neoforge.client.event.ClientTickEvent;
import net.neoforged.neoforge.client.event.EntityRenderersEvent;
import net.neoforged.neoforge.client.event.ModelEvent;
import net.neoforged.neoforge.client.event.RegisterKeyMappingsEvent;
import net.neoforged.neoforge.client.event.ScreenEvent;

import java.lang.ref.WeakReference;

/** Client-only event handlers (NeoForge auto-routes each to the mod or game bus by event type). */
@EventBusSubscriber(modid = Skyseed.MODID, value = Dist.CLIENT)
public final class SkyseedClientEvents {
    private static final ResourceKey<WorldPreset> SKYBLOCK_PRESET =
            ResourceKey.create(Registries.WORLD_PRESET, Ids.mod("skyblock"));

    // Track the last screen we defaulted, so we set the type once per screen (not on every resize).
    private static WeakReference<Screen> defaulted = new WeakReference<>(null);

    private SkyseedClientEvents() {}

    @SubscribeEvent
    static void onModifyBakingResult(ModelEvent.ModifyBakingResult event) {
        // Auto debug seeds ship no model file; point each at its base theme's seed model so the icon reuses that texture.
        // 26.1.2 reworked the model bake: getModels() (a Map<ModelResourceLocation, BakedModel>) became
        // getBakingResult().itemStackModels(), a mutable Map keyed by the item's plain Identifier — ModelResourceLocation
        // is gone, and inventory item models are now looked up by item id directly.
        //? if >=26.1.2 {
        /*final var itemModels = event.getBakingResult().itemStackModels();
        ModItems.AUTO_DEBUG_BASE.forEach((itemId, baseTheme) -> {
            final var baseModel = itemModels.get(Ids.mod(baseTheme + "_skyseed"));
            if (baseModel != null) {
                itemModels.put(Ids.mod(itemId), baseModel);
            }
        });*/
        //?} else {
        final var models = event.getModels();
        ModItems.AUTO_DEBUG_BASE.forEach((itemId, baseTheme) -> {
            final var baked = models.get(ModelResourceLocation.inventory(Ids.mod(baseTheme + "_skyseed")));
            if (baked != null) {
                models.put(ModelResourceLocation.inventory(Ids.mod(itemId)), baked);
            }
        });
        //?}
    }

    @SubscribeEvent
    static void onRegisterRenderers(EntityRenderersEvent.RegisterRenderers event) {
        // ThrownItemRenderer draws the projectile as its item (the dirt-ball icon), like a snowball.
        event.registerEntityRenderer(ModEntities.ISLAND_SEED.get(), ThrownItemRenderer::new);
    }

    @SubscribeEvent
    static void onRegisterKeyMappings(RegisterKeyMappingsEvent event) {
        event.register(SkyseedKeyMappings.TOGGLE_THROW_MODE);
    }

    /** Toggle Classic <-> Precise throw mode on keypress, persist it, and confirm via the actionbar. */
    @SubscribeEvent
    static void onClientTick(ClientTickEvent.Post event) {
        while (SkyseedKeyMappings.TOGGLE_THROW_MODE.consumeClick()) {
            final boolean precise = !SkyseedClientConfig.PRECISE_THROW.get();
            SkyseedClientConfig.PRECISE_THROW.set(precise); // persisted by the config system
            final Minecraft mc = Minecraft.getInstance();
            if (mc.player != null) {
                // displayClientMessage was removed from Player in 26.1.2; the Gui overlay (action bar) works on both.
                mc.gui.setOverlayMessage(
                        Component.translatable(precise ? "skyseed.throw_mode.precise" : "skyseed.throw_mode.classic"), false);
            }
        }
    }

    /** Pre-select the Skyblock world type when the create-world screen first opens (still switchable). */
    @SubscribeEvent
    static void onCreateWorldScreen(ScreenEvent.Init.Post event) {
        if (!(event.getScreen() instanceof CreateWorldScreen screen) || defaulted.get() == screen) {
            return;
        }
        defaulted = new WeakReference<>(screen);
        WorldCreationUiState ui = screen.getUiState();
        for (WorldCreationUiState.WorldTypeEntry entry : ui.getNormalPresetList()) {
            if (entry.preset().is(SKYBLOCK_PRESET)) {
                ui.setWorldType(entry);
                ui.setGenerateStructures(false); // a skyblock void shouldn't have structures floating in it
                break;
            }
        }
    }
}
