package dev.gemberkoekje.skyseed.item;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResultHolder;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.Level;
import vazkii.patchouli.api.PatchouliAPI;

/** The Skyfarer's Almanac — right-click opens the Patchouli guide book. */
public class GuideItem extends Item {
    private static final ResourceLocation BOOK = ResourceLocation.fromNamespaceAndPath(Skyseed.MODID, "guide");

    public GuideItem(Properties properties) {
        super(properties);
    }

    @Override
    public InteractionResultHolder<ItemStack> use(Level level, Player player, InteractionHand hand) {
        ItemStack stack = player.getItemInHand(hand);
        if (player instanceof ServerPlayer serverPlayer) {
            PatchouliAPI.get().openBookGUI(serverPlayer, BOOK);
        }
        return InteractionResultHolder.sidedSuccess(stack, level.isClientSide());
    }
}
