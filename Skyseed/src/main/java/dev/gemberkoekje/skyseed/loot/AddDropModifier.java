package dev.gemberkoekje.skyseed.loot;

import com.mojang.serialization.Codec;
import com.mojang.serialization.MapCodec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import it.unimi.dsi.fastutil.objects.ObjectArrayList;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.storage.loot.LootContext;
import net.minecraft.world.level.storage.loot.predicates.LootItemCondition;
import net.neoforged.neoforge.common.loot.IGlobalLootModifier;
import net.neoforged.neoforge.common.loot.LootModifier;

/**
 * A global loot modifier that adds {@code item} to a rolled loot table's drops with probability {@code chance}.
 * The End-chapter collect-a-thon (SKYENDPLAN Phase 1) uses it to seed the Portal Frame Shard + the structure relics
 * into the vanilla structure loot tables, gated to one table each by a {@code neoforge:loot_table_id} condition in the
 * data file — so a re-grown structure island is a re-rollable source ("rare but farmable").
 */
public class AddDropModifier extends LootModifier {
    public static final MapCodec<AddDropModifier> CODEC = RecordCodecBuilder.mapCodec(inst ->
            codecStart(inst).and(inst.group(
                    BuiltInRegistries.ITEM.byNameCodec().fieldOf("item").forGetter(m -> m.item),
                    Codec.FLOAT.fieldOf("chance").forGetter(m -> m.chance)
            )).apply(inst, AddDropModifier::new));

    private final Item item;
    private final float chance;

    // 26.1.2 added a priority to LootModifier (the ctor + codecStart now carry an int); the CODEC's apply(::new)
    // resolves to whichever ctor arity matches the active version, so only the constructor needs the directive.
    //? if >=26.1.2 {
    /*public AddDropModifier(LootItemCondition[] conditions, int priority, Item item, float chance) {
        super(conditions, priority);
        this.item = item;
        this.chance = chance;
    }*/
    //?} else {
    public AddDropModifier(LootItemCondition[] conditions, Item item, float chance) {
        super(conditions);
        this.item = item;
        this.chance = chance;
    }
    //?}

    @Override
    protected ObjectArrayList<ItemStack> doApply(ObjectArrayList<ItemStack> generatedLoot, LootContext context) {
        if (context.getRandom().nextFloat() < chance) {
            generatedLoot.add(new ItemStack(item));
        }
        return generatedLoot;
    }

    @Override
    public MapCodec<? extends IGlobalLootModifier> codec() {
        return CODEC;
    }
}
