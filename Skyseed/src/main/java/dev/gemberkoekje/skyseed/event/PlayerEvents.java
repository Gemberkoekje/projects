package dev.gemberkoekje.skyseed.event;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.registry.ModItems;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.item.ItemStack;
import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.fml.common.EventBusSubscriber;
import net.neoforged.neoforge.event.entity.player.PlayerEvent;

/** Gives every player the Skyfarer's Almanac the first time they join a world. */
@EventBusSubscriber(modid = Skyseed.MODID)
public final class PlayerEvents {
    private static final String GUIDE_GIVEN = "skyseed:guide_given";

    private PlayerEvents() {}

    @SubscribeEvent
    static void onLogin(PlayerEvent.PlayerLoggedInEvent event) {
        if (!(event.getEntity() instanceof ServerPlayer player)) {
            return;
        }
        CompoundTag data = player.getPersistentData(); // persists across relogs
        if (data.getBoolean(GUIDE_GIVEN)) {
            return;
        }
        ItemStack guide = new ItemStack(ModItems.GUIDE.get());
        if (!player.getInventory().add(guide)) {
            player.drop(guide, false);
        }
        data.putBoolean(GUIDE_GIVEN, true);
    }
}
