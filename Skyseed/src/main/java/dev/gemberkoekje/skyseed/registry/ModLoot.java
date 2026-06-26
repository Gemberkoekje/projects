package dev.gemberkoekje.skyseed.registry;

import com.mojang.serialization.MapCodec;
import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.loot.AddDropModifier;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.neoforge.common.loot.IGlobalLootModifier;
import net.neoforged.neoforge.registries.DeferredRegister;
import net.neoforged.neoforge.registries.NeoForgeRegistries;

import java.util.function.Supplier;

/** Global loot modifiers — the {@code skyseed:add_drop} serializer used by the End-chapter collect-a-thon drops. */
public final class ModLoot {
    public static final DeferredRegister<MapCodec<? extends IGlobalLootModifier>> MODIFIERS =
            DeferredRegister.create(NeoForgeRegistries.Keys.GLOBAL_LOOT_MODIFIER_SERIALIZERS, Skyseed.MODID);

    public static final Supplier<MapCodec<AddDropModifier>> ADD_DROP =
            MODIFIERS.register("add_drop", () -> AddDropModifier.CODEC);

    private ModLoot() {}

    public static void register(IEventBus modEventBus) {
        MODIFIERS.register(modEventBus);
    }
}
