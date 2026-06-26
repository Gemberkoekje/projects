package dev.gemberkoekje.skyseed;

import com.mojang.logging.LogUtils;
import dev.gemberkoekje.skyseed.network.SkyseedNetwork;
import dev.gemberkoekje.skyseed.registry.ModCreativeTabs;
import dev.gemberkoekje.skyseed.registry.ModEntities;
import dev.gemberkoekje.skyseed.registry.ModFeatures;
import dev.gemberkoekje.skyseed.registry.ModItems;
import dev.gemberkoekje.skyseed.registry.ModLoot;
import dev.gemberkoekje.skyseed.registry.ModRecipes;
import dev.gemberkoekje.skyseed.registry.SkyseedRegistries;
import dev.gemberkoekje.skyseed.worldgen.structure.DevStructureGenerator;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.fml.ModContainer;
import net.neoforged.fml.common.Mod;
import net.neoforged.fml.config.ModConfig;
import org.slf4j.Logger;

// The value here must match an entry in the META-INF/neoforge.mods.toml file.
@Mod(Skyseed.MODID)
public class Skyseed {
    // The mod id, referenced everywhere (registries, themes, item/entity ids).
    public static final String MODID = "skyseed";
    public static final Logger LOGGER = LogUtils.getLogger();
    /** This build's mod version (e.g. "0.35.2"); stamped into new worlds and logged on load. */
    public static String VERSION = "unknown";

    // FML passes in the mod event bus and container automatically.
    public Skyseed(IEventBus modEventBus, ModContainer modContainer) {
        VERSION = modContainer.getModInfo().getVersion().toString();
        ModItems.register(modEventBus);
        ModLoot.register(modEventBus);
        ModEntities.register(modEventBus);
        ModFeatures.register(modEventBus);
        ModRecipes.register(modEventBus);
        ModCreativeTabs.register(modEventBus);
        modEventBus.addListener(SkyseedRegistries::onNewDataPackRegistry);
        SkyseedNetwork.register(modEventBus);
        modEventBus.addListener(DevStructureGenerator::onCommonSetup); // dev-only: emits building .nbt templates
        modContainer.registerConfig(ModConfig.Type.CLIENT, SkyseedClientConfig.SPEC);

        LOGGER.info("Skyseed loaded — {} seed items, throwable entity, and theme registry registered.", ModItems.SEED_THEMES.size());
    }
}
