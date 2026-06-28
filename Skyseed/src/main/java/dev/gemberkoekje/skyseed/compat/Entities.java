package dev.gemberkoekje.skyseed.compat;

import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntityType;
import org.jetbrains.annotations.Nullable;

/**
 * Version-volatile entity creation/placement, isolated behind stable signatures. 26.1.2 renamed
 * {@code Entity.moveTo} → {@code snapTo}, and moved the spawn reason into {@code EntityType.create} (it gained a
 * required {@code EntitySpawnReason} argument). Both swaps live here so the generator never names either. See
 * {@code REFACTORPLAN.md} §2.7.
 */
public final class Entities {
    private Entities() {}

    /** Create an entity for direct placement (the spawn reason became a required {@code create} arg in 26.1.2). */
    @Nullable
    public static <T extends Entity> T create(EntityType<T> type, ServerLevel level) {
        //? if >=26.1.2 {
        /*return type.create(level, net.minecraft.world.entity.EntitySpawnReason.SPAWNER);*/
        //?} else {
        return type.create(level);
        //?}
    }

    /** Snap {@code e} to a position + rotation ({@code Entity.moveTo} was renamed {@code snapTo} in 26.1.2). */
    public static void place(Entity e, double x, double y, double z, float yRot, float xRot) {
        //? if >=26.1.2 {
        /*e.snapTo(x, y, z, yRot, xRot);*/
        //?} else {
        e.moveTo(x, y, z, yRot, xRot);
        //?}
    }
}
