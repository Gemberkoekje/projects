package dev.gemberkoekje.skyseed;

import com.mojang.logging.LogUtils;
import dev.gemberkoekje.skyseed.registry.ModCreativeTabs;
import dev.gemberkoekje.skyseed.registry.ModDataComponents;
import dev.gemberkoekje.skyseed.registry.ModEntities;
import dev.gemberkoekje.skyseed.registry.ModItems;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.fml.ModContainer;
import net.neoforged.fml.common.Mod;
import org.slf4j.Logger;

// The value here must match an entry in the META-INF/neoforge.mods.toml file.
@Mod(Skyseed.MODID)
public class Skyseed {
    // The mod id, referenced everywhere (registries, themes, the skyseed:theme component).
    public static final String MODID = "skyseed";
    public static final Logger LOGGER = LogUtils.getLogger();

    // FML passes in the mod event bus and container automatically.
    public Skyseed(IEventBus modEventBus, ModContainer modContainer) {
        ModDataComponents.register(modEventBus);
        ModItems.register(modEventBus);
        ModEntities.register(modEventBus);
        ModCreativeTabs.register(modEventBus);

        LOGGER.info("Skyseed loaded — item, theme component, and throwable entity registered.");
    }
}
