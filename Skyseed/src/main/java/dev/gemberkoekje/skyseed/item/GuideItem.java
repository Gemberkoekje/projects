package dev.gemberkoekje.skyseed.item;

import dev.gemberkoekje.skyseed.client.SkyseedGuideClient;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResultHolder;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.Level;

/** The Skyfarer's Almanac — a guide book. Right-click opens it (client-side). */
public class GuideItem extends Item {
    public GuideItem(Properties properties) {
        super(properties);
    }

    @Override
    public InteractionResultHolder<ItemStack> use(Level level, Player player, InteractionHand hand) {
        ItemStack stack = player.getItemInHand(hand);
        if (level.isClientSide) {
            // Only reached on the client, so the client-only screen class loads lazily and safely here.
            SkyseedGuideClient.open();
        }
        return InteractionResultHolder.sidedSuccess(stack, level.isClientSide());
    }
}
