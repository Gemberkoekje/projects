package dev.gemberkoekje.skyseed;

import net.neoforged.neoforge.common.ModConfigSpec;

/**
 * Client config. Holds the player's chosen Skyseed throw mode, persisted across sessions in
 * {@code config/skyseed-client.toml}. Toggled by the throw-mode keybind; read (client-side only) when
 * a seed is thrown to decide whether to send a Classic or Precise throw to the server.
 */
public final class SkyseedClientConfig {
    public static final ModConfigSpec SPEC;
    public static final ModConfigSpec.BooleanValue PRECISE_THROW;

    static {
        ModConfigSpec.Builder builder = new ModConfigSpec.Builder();
        PRECISE_THROW = builder
                .comment("Skyseed throw mode. true = Precise (direct placement along your look vector);",
                        "false = Classic (charged physics arc). Toggle in-game with the throw-mode key.")
                .define("preciseThrowMode", false);
        SPEC = builder.build();
    }

    private SkyseedClientConfig() {}
}
