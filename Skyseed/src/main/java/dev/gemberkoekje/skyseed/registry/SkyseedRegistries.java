package dev.gemberkoekje.skyseed.registry;

import dev.gemberkoekje.skyseed.compat.Ids;
import dev.gemberkoekje.skyseed.worldgen.theme.IslandTheme;
import net.minecraft.core.Registry;
import net.minecraft.resources.ResourceKey;
import net.neoforged.neoforge.registries.DataPackRegistryEvent;

/**
 * Custom datapack registries. {@link #THEME} holds {@link IslandTheme}s loaded from
 * {@code data/<namespace>/skyseed/theme/*.json}. Server-side only (generation is server-side — see README → Design decisions),
 * so no network codec is supplied.
 */
public final class SkyseedRegistries {
    public static final ResourceKey<Registry<IslandTheme>> THEME =
            ResourceKey.createRegistryKey(Ids.mod("theme"));

    private SkyseedRegistries() {}

    public static void onNewDataPackRegistry(DataPackRegistryEvent.NewRegistry event) {
        event.dataPackRegistry(THEME, IslandTheme.CODEC);
    }
}
