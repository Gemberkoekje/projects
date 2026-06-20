package dev.gemberkoekje.skyseed.item;

import dev.gemberkoekje.skyseed.entity.IslandSeedEntity;
import dev.gemberkoekje.skyseed.registry.ModDataComponents;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.sounds.SoundEvents;
import net.minecraft.sounds.SoundSource;
import net.minecraft.stats.Stats;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResultHolder;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.UseAnim;
import net.minecraft.world.level.Level;

/**
 * The Skyseed item. Hold right-click to wind up, release to throw — a quick tap lobs the island in
 * close, a full charge launches it far. The thrown {@link IslandSeedEntity} carries this seed's theme.
 */
public class IslandSeedItem extends Item {
    private static final int CHARGE_TICKS = 25;     // hold time to reach full power (~1.25 s)
    private static final float MIN_VELOCITY = 0.5F; // a quick tap — lands close
    private static final float MAX_VELOCITY = 2.5F; // fully wound up — flies far

    public IslandSeedItem(Properties properties) {
        super(properties);
    }

    @Override
    public InteractionResultHolder<ItemStack> use(Level level, Player player, InteractionHand hand) {
        player.startUsingItem(hand);
        return InteractionResultHolder.consume(player.getItemInHand(hand));
    }

    @Override
    public int getUseDuration(ItemStack stack, LivingEntity entity) {
        return 72000; // hold as long as you like; the throw happens on release
    }

    @Override
    public UseAnim getUseAnimation(ItemStack stack) {
        return UseAnim.SPEAR; // raise-to-throw, like a trident
    }

    @Override
    public void releaseUsing(ItemStack stack, Level level, LivingEntity entity, int timeLeft) {
        if (!(entity instanceof Player player)) {
            return;
        }
        int heldTicks = getUseDuration(stack, entity) - timeLeft;
        float power = Math.min(1.0F, heldTicks / (float) CHARGE_TICKS);
        float velocity = MIN_VELOCITY + power * (MAX_VELOCITY - MIN_VELOCITY);

        level.playSound(null, player.getX(), player.getY(), player.getZ(),
                SoundEvents.SNOWBALL_THROW, SoundSource.NEUTRAL, 0.6F, 0.7F + power * 0.6F);

        if (!level.isClientSide) {
            IslandSeedEntity seed = new IslandSeedEntity(level, player);
            seed.setItem(stack);
            ResourceLocation theme = stack.get(ModDataComponents.THEME.get());
            if (theme != null) {
                seed.setTheme(theme);
            }
            seed.shootFromRotation(player, player.getXRot(), player.getYRot(), 0.0F, velocity, 1.0F);
            level.addFreshEntity(seed);
        }

        player.awardStat(Stats.ITEM_USED.get(this));
        stack.consume(1, player);
    }
}
