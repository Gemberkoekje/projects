package dev.gemberkoekje.skyseed.registry;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.entity.IslandSeedEntity;
//? if >=26.1.2 {
/*import dev.gemberkoekje.skyseed.compat.Ids;
import net.minecraft.resources.ResourceKey;*/
//?}
import net.minecraft.core.registries.Registries;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.MobCategory;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.neoforge.registries.DeferredHolder;
import net.neoforged.neoforge.registries.DeferredRegister;

/** Entity types. The thrown Skyseed projectile. */
public final class ModEntities {
    public static final DeferredRegister<EntityType<?>> ENTITY_TYPES =
            DeferredRegister.create(Registries.ENTITY_TYPE, Skyseed.MODID);

    public static final DeferredHolder<EntityType<?>, EntityType<IslandSeedEntity>> ISLAND_SEED =
            ENTITY_TYPES.register("island_seed", () -> EntityType.Builder.<IslandSeedEntity>of(IslandSeedEntity::new, MobCategory.MISC)
                    .sized(0.25f, 0.25f)
                    .clientTrackingRange(4)
                    .updateInterval(10)
                    //? if >=26.1.2 {
                    /*.build(ResourceKey.create(Registries.ENTITY_TYPE, Ids.mod("island_seed"))));*/
                    //?} else {
                    .build("island_seed"));
                    //?}

    private ModEntities() {}

    public static void register(IEventBus modEventBus) {
        ENTITY_TYPES.register(modEventBus);
    }
}
