package dev.gemberkoekje.skyseed.compat;

import net.minecraft.core.Holder;
import net.minecraft.core.Registry;
import net.minecraft.core.RegistryAccess;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.ResourceKey;
//? if >=26.1.2 {
/*import net.minecraft.resources.Identifier;*/
//?} else {
import net.minecraft.resources.ResourceLocation;
//?}
import net.minecraft.tags.TagKey;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.biome.Biome;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.levelgen.feature.ConfiguredFeature;
import net.minecraft.world.level.levelgen.structure.pools.StructureTemplatePool;

import java.util.Optional;
import java.util.stream.Stream;

/**
 * Version-volatile registry access, isolated behind stable signatures.
 *
 * <p>Both the static {@link BuiltInRegistries} accessors and the dynamic {@link RegistryAccess} shape have moved
 * across recent versions (e.g. {@code registryOrThrow} → {@code lookupOrThrow}). The algorithm and planners look
 * things up through here so that churn lands in one file. The data carries the version-agnostic {@link Id}, so every
 * facade method below resolves it with {@code Ids.parse(...)} and never names the Minecraft id type itself — the only
 * method that does is the one raw {@code templatePool} overload at the bottom (reached by gametests via
 * {@code Ids.mod(...)}), behind a {@code //?} directive for the 26.1.2 {@code ResourceLocation}→{@code Identifier}
 * rename. Behaviour-preserving. See {@code REFACTORPLAN.md} §2.6.
 */
public final class Lookup {

    private Lookup() {
    }

    // --- Blocks -------------------------------------------------------------------------------------------------

    /** Whether a block is registered under {@code id} (a {@code null}/unparseable id → false). */
    public static boolean hasBlock(Id id) {
        final var rl = id == null ? null : Ids.parse(id.value());
        return rl != null && BuiltInRegistries.BLOCK.containsKey(rl);
    }

    /** The block under {@code id} (vanilla returns AIR for an unknown id; callers gate with {@link #hasBlock(Id)}). */
    public static Block block(Id id) {
        return byId(BuiltInRegistries.BLOCK, id);
    }

    /** Convenience: the default {@link BlockState} of the block under {@code id}. */
    public static BlockState blockState(Id id) {
        return block(id).defaultBlockState();
    }

    /** The registry id of a block, as a {@code namespace:path} string. */
    public static String blockId(Block block) {
        return BuiltInRegistries.BLOCK.getKey(block).toString();
    }

    // --- Entity types -------------------------------------------------------------------------------------------

    /** Whether an entity type is registered under {@code id} (a {@code null}/unparseable id → false). */
    public static boolean hasEntityType(Id id) {
        final var rl = id == null ? null : Ids.parse(id.value());
        return rl != null && BuiltInRegistries.ENTITY_TYPE.containsKey(rl);
    }

    /** The entity type under {@code id} (callers gate with {@link #hasEntityType(Id)}). */
    public static EntityType<?> entityType(Id id) {
        return byId(BuiltInRegistries.ENTITY_TYPE, id);
    }

    // --- Biomes -------------------------------------------------------------------------------------------------

    /**
     * Whether {@code biome} matches a single theme biome-filter {@code entry}: a {@code #namespace:path} entry tests a
     * biome tag, a plain {@code namespace:path} entry tests the biome id. An unparseable entry matches nothing.
     */
    public static boolean biomeMatches(Holder<Biome> biome, String entry) {
        if (entry.startsWith("#")) {
            final var tagId = Ids.parse(entry.substring(1));
            return tagId != null && biome.is(TagKey.create(Registries.BIOME, tagId));
        }
        final var id = Ids.parse(entry);
        return id != null && biome.is(id);
    }

    // --- Dynamic registries (via RegistryAccess) ----------------------------------------------------------------

    /** A registry — vanilla or one of this mod's datapack registries (e.g. the theme registry) — by its key. The
     *  accessor was renamed {@code registryOrThrow} → {@code lookupOrThrow} (both return a {@code Registry}) in 26.1.2;
     *  on 1.21.1 {@code lookupOrThrow} returns a holder-lookup instead, so the two are NOT interchangeable — hence the
     *  directive. Route ALL registry access through here so the swap lives in one place. */
    public static <T> Registry<T> registry(RegistryAccess access, ResourceKey<? extends Registry<? extends T>> key) {
        //? if >=26.1.2 {
        /*return access.lookupOrThrow(key);*/
        //?} else {
        return access.registryOrThrow(key);
        //?}
    }

    /** The id string of a dimension key, e.g. {@code "minecraft:the_nether"}. {@code ResourceKey.location()} was
     *  renamed {@code identifier()} in 26.1.2, so the dim-id extraction lives here. */
    public static String dimensionId(ResourceKey<Level> dim) {
        //? if >=26.1.2 {
        /*return dim.identifier().toString();*/
        //?} else {
        return dim.location().toString();
        //?}
    }

    /** A registry's elements as a stream of holders ({@code Registry.holders()} was renamed {@code listElements()} in 26.1.2). */
    public static <T> Stream<Holder.Reference<T>> elements(Registry<T> registry) {
        //? if >=26.1.2 {
        /*return registry.listElements();*/
        //?} else {
        return registry.holders();
        //?}
    }

    /** The holder for a biome {@code id}, or {@code null} if absent/unparseable. {@code Registry.getHolder(ResourceKey)}
     *  became {@code get(Identifier)} (returning an {@code Optional<Holder>}) in 26.1.2. */
    public static Holder<Biome> biomeHolder(RegistryAccess access, Id id) {
        final var rl = id == null ? null : Ids.parse(id.value());
        if (rl == null) {
            return null;
        }
        final Registry<Biome> reg = registry(access, Registries.BIOME);
        //? if >=26.1.2 {
        /*return reg.get(rl).<Holder<Biome>>map(ref -> ref).orElse(null);*/
        //?} else {
        return reg.getHolder(ResourceKey.create(Registries.BIOME, rl)).<Holder<Biome>>map(ref -> ref).orElse(null);
        //?}
    }

    /** A value from {@code registry} under {@code id} — {@code null} if the id is absent or unparseable. The direct
     *  value accessor was renamed {@code get} → {@code getValue} in 26.1.2 (there {@code get} returns an
     *  {@code Optional<Holder>} instead), so this is the one place block/entity/theme value lookups resolve. */
    public static <T> T byId(Registry<T> registry, Id id) {
        final var rl = id == null ? null : Ids.parse(id.value());
        if (rl == null) {
            return null;
        }
        //? if >=26.1.2 {
        /*return registry.getValue(rl);*/
        //?} else {
        return registry.get(rl);
        //?}
    }

    /** Whether a jigsaw template pool is registered under {@code id} (a {@code null}/unparseable id → false). */
    public static boolean hasTemplatePool(RegistryAccess access, Id id) {
        final var rl = id == null ? null : Ids.parse(id.value());
        return rl != null && registry(access, Registries.TEMPLATE_POOL).containsKey(rl);
    }

    /** Whether a jigsaw template pool is registered under a raw {@code namespace:path} pool id (e.g. a {@code _void} variant). */
    public static boolean hasTemplatePool(RegistryAccess access, String id) {
        final var rl = Ids.parse(id);
        return rl != null && registry(access, Registries.TEMPLATE_POOL).containsKey(rl);
    }

    /** The template-pool holder for {@code id} (callers gate with {@link #hasTemplatePool(RegistryAccess, Id)}). */
    public static Holder<StructureTemplatePool> templatePool(RegistryAccess access, Id id) {
        return templatePool(access, Ids.parse(id.value()));
    }

    /** The template-pool holder for a raw {@code namespace:path} pool id (e.g. a cap-filler pool string). */
    public static Holder<StructureTemplatePool> templatePool(RegistryAccess access, String id) {
        return templatePool(access, Ids.parse(id));
    }

    /** The configured feature under {@code id} (a {@code null}/unparseable id → empty). */
    public static Optional<ConfiguredFeature<?, ?>> configuredFeature(RegistryAccess access, Id id) {
        final var rl = id == null ? null : Ids.parse(id.value());
        return rl == null ? Optional.empty()
                : registry(access, Registries.CONFIGURED_FEATURE).getOptional(rl);
    }

    // --- The one method that names the Minecraft id type: the raw templatePool gametests reach via Ids.mod(...) ---
    //? if >=26.1.2 {
    /*public static Holder<StructureTemplatePool> templatePool(RegistryAccess access, Identifier id) {
        return access.lookupOrThrow(Registries.TEMPLATE_POOL)
                .getOrThrow(ResourceKey.create(Registries.TEMPLATE_POOL, id));
    }*/
    //?} else {
    public static Holder<StructureTemplatePool> templatePool(RegistryAccess access, ResourceLocation id) {
        return access.lookupOrThrow(Registries.TEMPLATE_POOL)
                .getOrThrow(ResourceKey.create(Registries.TEMPLATE_POOL, id));
    }
    //?}
}
