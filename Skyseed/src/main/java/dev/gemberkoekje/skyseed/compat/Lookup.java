package dev.gemberkoekje.skyseed.compat;

import net.minecraft.core.Holder;
import net.minecraft.core.Registry;
import net.minecraft.core.RegistryAccess;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.ResourceKey;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.levelgen.feature.ConfiguredFeature;
import net.minecraft.world.level.levelgen.structure.pools.StructureTemplatePool;

import java.util.Optional;

/**
 * Version-volatile registry access, isolated behind stable signatures.
 *
 * <p>Both the static {@link BuiltInRegistries} accessors and the dynamic {@link RegistryAccess} shape have moved
 * across recent versions (e.g. {@code registryOrThrow} → {@code lookupOrThrow}, {@code get} returning a value vs an
 * {@code Optional}/{@code Holder}). The algorithm and planners look things up through here so that churn lands in
 * one file. Behaviour-preserving: each method reproduces exactly what its former call sites did. See
 * {@code REFACTORPLAN.md}.
 */
public final class Lookup {

    private Lookup() {
    }

    // --- Blocks (static registry) -------------------------------------------------------------------------------

    /** Whether a block is registered under {@code id}. */
    public static boolean hasBlock(ResourceLocation id) {
        return BuiltInRegistries.BLOCK.containsKey(id);
    }

    /** The block registered under {@code id} (vanilla returns AIR for an unknown id). */
    public static Block block(ResourceLocation id) {
        return BuiltInRegistries.BLOCK.get(id);
    }

    /** Convenience: the default {@link BlockState} of the block registered under {@code id}. */
    public static BlockState blockState(ResourceLocation id) {
        return BuiltInRegistries.BLOCK.get(id).defaultBlockState();
    }

    /** The registry id of a block. */
    public static ResourceLocation blockId(Block block) {
        return BuiltInRegistries.BLOCK.getKey(block);
    }

    // --- Blocks, by version-agnostic Id (the data carries Id; the Id -> Minecraft id type parse happens here) -----

    /** Whether a block is registered under {@code id} (a {@code null}/unparseable id → false). */
    public static boolean hasBlock(Id id) {
        final ResourceLocation rl = id == null ? null : Ids.parse(id.value());
        return rl != null && hasBlock(rl);
    }

    /** The block under {@code id} (callers gate with {@link #hasBlock(Id)}; vanilla returns AIR for an unknown id). */
    public static Block block(Id id) {
        return block(Ids.parse(id.value()));
    }

    /** Convenience: the default {@link BlockState} of the block registered under {@code id}. */
    public static BlockState blockState(Id id) {
        return block(id).defaultBlockState();
    }

    // --- Entity types (static registry) -------------------------------------------------------------------------

    /** Whether an entity type is registered under {@code id}. */
    public static boolean hasEntityType(ResourceLocation id) {
        return BuiltInRegistries.ENTITY_TYPE.containsKey(id);
    }

    /** The entity type registered under {@code id}. */
    public static EntityType<?> entityType(ResourceLocation id) {
        return BuiltInRegistries.ENTITY_TYPE.get(id);
    }

    /** Whether an entity type is registered under {@code id} (a {@code null}/unparseable id → false). */
    public static boolean hasEntityType(Id id) {
        final ResourceLocation rl = id == null ? null : Ids.parse(id.value());
        return rl != null && hasEntityType(rl);
    }

    /** The entity type under {@code id} (callers gate with {@link #hasEntityType(Id)}). */
    public static EntityType<?> entityType(Id id) {
        return entityType(Ids.parse(id.value()));
    }

    // --- Dynamic registries (via RegistryAccess) ----------------------------------------------------------------

    /** A registry — vanilla or one of this mod's datapack registries (e.g. the theme registry) — by its key. */
    public static <T> Registry<T> registry(RegistryAccess access, ResourceKey<? extends Registry<? extends T>> key) {
        return access.registryOrThrow(key);
    }

    /** Whether a jigsaw {@link StructureTemplatePool} is registered under {@code id}. */
    public static boolean hasTemplatePool(RegistryAccess access, ResourceLocation id) {
        return access.registryOrThrow(Registries.TEMPLATE_POOL).containsKey(id);
    }

    /** The template-pool holder for {@code id} (throws if absent — callers gate with {@link #hasTemplatePool}). */
    public static Holder<StructureTemplatePool> templatePool(RegistryAccess access, ResourceLocation id) {
        return access.lookupOrThrow(Registries.TEMPLATE_POOL)
                .getOrThrow(ResourceKey.create(Registries.TEMPLATE_POOL, id));
    }

    /** The configured feature registered under {@code id}, if present. */
    public static Optional<ConfiguredFeature<?, ?>> configuredFeature(RegistryAccess access, ResourceLocation id) {
        return access.registryOrThrow(Registries.CONFIGURED_FEATURE).getOptional(id);
    }
}
