package dev.gemberkoekje.skyseed.client;

import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.KeyMapping;
import org.lwjgl.glfw.GLFW;

/** Client keybinds. {@link #TOGGLE_THROW_MODE} cycles Classic <-> Precise throw mode (default: V). */
public final class SkyseedKeyMappings {
    public static final String CATEGORY = "key.category.skyseed";

    // 26.1.2 made the keybind category a KeyMapping.Category (a registry record) instead of a String key — use the
    // built-in GAMEPLAY group (registering a custom category is a separate enhancement).
    //? if >=26.1.2 {
    /*public static final KeyMapping TOGGLE_THROW_MODE = new KeyMapping(
            "key.skyseed.toggle_throw_mode", InputConstants.Type.KEYSYM, GLFW.GLFW_KEY_V, KeyMapping.Category.GAMEPLAY);*/
    //?} else {
    public static final KeyMapping TOGGLE_THROW_MODE = new KeyMapping(
            "key.skyseed.toggle_throw_mode", InputConstants.Type.KEYSYM, GLFW.GLFW_KEY_V, CATEGORY);
    //?}

    private SkyseedKeyMappings() {}
}
