package dev.gemberkoekje.skyseed;

import com.mojang.logging.LogUtils;
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
        LOGGER.info("Skyseed loaded (milestone 0 scaffold) — nothing registered yet.");
    }
}
