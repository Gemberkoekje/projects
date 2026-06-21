package dev.gemberkoekje.skyseed.worldgen.structure;

import dev.gemberkoekje.skyseed.Skyseed;
import net.neoforged.fml.event.lifecycle.FMLCommonSetupEvent;
import net.neoforged.fml.loading.FMLEnvironment;

import java.nio.file.Path;

/**
 * Dev-only: writes Skyseed's code-authored building templates ({@link HamletTemplates}) into the source
 * {@code data/skyseed/structure/} tree on startup, so they are committed and shipped as real {@code .nbt}.
 * Never runs in production (the templates load from the jar there) and never overwrites an existing file,
 * so a structure-block-authored replacement is safe. Registered on the mod event bus in {@link Skyseed}.
 */
public final class DevStructureGenerator {
    private DevStructureGenerator() {}

    public static void onCommonSetup(FMLCommonSetupEvent event) {
        if (FMLEnvironment.production) {
            return;
        }
        try {
            // runServer/runClient working dir is the `run/` folder, a sibling of `src/`.
            final Path base = Path.of(System.getProperty("user.dir"))
                    .resolveSibling("src").resolve("main/resources/data/skyseed/structure");
            HamletTemplates.generateInto(base.resolve("hamlet"));
            TradePostTemplates.generateInto(base.resolve("trade_post"));
        } catch (Exception e) {
            Skyseed.LOGGER.warn("[skyseed] dev structure template generation skipped: {}", e.toString());
        }
    }
}
