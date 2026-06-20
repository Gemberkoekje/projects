package dev.gemberkoekje.skyseed.client;

import net.minecraft.ChatFormatting;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.inventory.BookViewScreen;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.MutableComponent;

import java.util.List;

/** Builds and opens the Skyfarer's Almanac as a vanilla book screen. Client-only. */
public final class SkyseedGuideClient {
    private SkyseedGuideClient() {}

    public static void open() {
        Minecraft.getInstance().setScreen(new BookViewScreen(new BookViewScreen.BookAccess(pages())));
    }

    private static List<Component> pages() {
        return List.of(
                page("Skyfarer's Almanac", ChatFormatting.DARK_GREEN,
                        "Throw a Skyseed into open air and a new floating island grows where it settles.\n\n"
                                + "Craft seeds, harvest islands, craft better seeds — and rise."),
                page("Throwing", ChatFormatting.DARK_AQUA,
                        "Hold right-click to wind up, then let go.\n\n"
                                + "A quick tap drops the isle in close. A long hold flings it far.\n\n"
                                + "It arms for ~2s, then grows in — and never into another island."),
                page("Forest Skyseed", ChatFormatting.DARK_GREEN,
                        "[P][D]\n[D][P]\n\nP = Planks\nD = Dirt\n\nA grassy isle with oak trees, coal and iron."),
                page("Rocky Skyseed", ChatFormatting.DARK_GRAY,
                        "[S][C]\n[C][S]\n\nS = Stone\nC = Cobblestone\n\nA stone isle with iron, gold and rare diamond."),
                page("Large Forest Skyseed", ChatFormatting.DARK_GREEN,
                        "[L][D][L]\n[D][D][D]\n[L][D][L]\n\nL = Oak Log\nD = Dirt\n\nA big forest isle. Costs more to craft."),
                page("This Almanac", ChatFormatting.GOLD,
                        "[Book] + [Skyseed]\n\nLost it? Craft a vanilla book together with any Skyseed.\n\n"
                                + "Tip: aim at open sky so your islands have room to grow.")
        );
    }

    private static Component page(String title, ChatFormatting titleColor, String body) {
        MutableComponent c = Component.empty();
        c.append(Component.literal(title + "\n\n").withStyle(titleColor, ChatFormatting.BOLD));
        c.append(Component.literal(body));
        return c;
    }
}
