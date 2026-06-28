package dev.gemberkoekje.skyseed.gametest_26_1_2;

import dev.gemberkoekje.skyseed.Skyseed;
import dev.gemberkoekje.skyseed.compat.Id;
import dev.gemberkoekje.skyseed.compat.Ids;
import dev.gemberkoekje.skyseed.compat.Lookup;
import dev.gemberkoekje.skyseed.registry.SkyseedRegistries;
import dev.gemberkoekje.skyseed.worldgen.IslandGenerator;
import dev.gemberkoekje.skyseed.worldgen.IslandPlan;
import dev.gemberkoekje.skyseed.worldgen.theme.IslandTheme;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.Registry;
import net.minecraft.gametest.framework.GameTestHelper;
import net.minecraft.gametest.framework.TestData;
import net.minecraft.gametest.framework.TestEnvironmentDefinition;
import net.minecraft.resources.Identifier;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.Rotation;
import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.fml.common.EventBusSubscriber;
import net.neoforged.neoforge.event.RegisterGameTestsEvent;

import java.util.function.Consumer;

/**
 * Skyseed's gametest suite for the <b>26.1.2+ node</b>, on the new {@link net.minecraft.gametest.framework.GameTestInstance}
 * framework (the {@code @GameTest}/{@code @GameTestHolder} annotation API was removed in 1.21.5+). This suite is fully
 * isolated from the 1.21.1 suite ({@code dev.gemberkoekje.skyseed.gametest}) by package + a per-version build.gradle
 * exclude, so the 1.21.1 suite stays the unchanging golden-master witness. Tests register in code via
 * {@link RegisterGameTestsEvent}; bodies are ported from the 1.21.1 suite. Coverage target + rollout in GAMETESTPLAN.md.
 *
 * <p>All tests use the empty {@code skyseed:gametest/region} (16×24×16) / {@code big_region} templates — the same
 * committed {@code .nbt} resources the 1.21.1 suite uses (version-independent, so they ship unchanged on 26.1.2).
 */
@EventBusSubscriber(modid = Skyseed.MODID)
public final class SkyseedTests {
    private SkyseedTests() {}

    private static final Identifier REGION = Ids.mod("gametest/region");
    private static final Identifier BIG_REGION = Ids.mod("gametest/big_region");

    private static Holder<TestEnvironmentDefinition<?>> env;

    @SubscribeEvent
    static void onRegisterGameTests(RegisterGameTestsEvent event) {
        env = event.registerEnvironment(Ids.mod("default")); // one empty AllOf() batch — no special setup

        // --- generation invariants (guard the IslandGenerator split) ---
        reg(event, "island_generates_blocks", REGION, SkyseedTests::islandGeneratesBlocks);
    }

    /** Build the standard per-test config and register it under {@code skyseed:<name>}. */
    private static void reg(RegisterGameTestsEvent event, String name, Identifier structure, Consumer<GameTestHelper> body) {
        final TestData<Holder<TestEnvironmentDefinition<?>>> data = new TestData<>(
                env, structure, /*maxTicks*/ 100, /*setupTicks*/ 0, /*required*/ true,
                Rotation.NONE, /*manualOnly*/ false, /*maxAttempts*/ 1, /*requiredSuccesses*/ 1,
                /*skyAccess*/ false, /*padding*/ 0);
        event.registerTest(Ids.mod(name), new SkyseedTest(data, body));
    }

    // ===== shared helpers (ported from the 1.21.1 suite) =====

    private static IslandTheme theme(ServerLevel level, String name) {
        final Registry<IslandTheme> reg = Lookup.registry(level.registryAccess(), SkyseedRegistries.THEME);
        final IslandTheme t = Lookup.byId(reg, Id.of(Ids.mod(name).toString()));
        if (t == null) {
            throw new IllegalStateException("theme '" + name + "' is not loaded");
        }
        return t;
    }

    /** Plan an island for {@code themeName} around the test region, with a fixed seed for reproducibility. */
    private static IslandPlan plan(GameTestHelper helper, String themeName, long seed) {
        final ServerLevel level = helper.getLevel();
        final BlockPos center = helper.absolutePos(new BlockPos(8, 8, 8));
        return IslandGenerator.planIsland(level, center, theme(level, themeName),
                level.getBiome(center), RandomSource.create(seed));
    }

    // ===== tests =====

    /** Spike (Phase 0): the simplest generation invariant — a rocky island plans a non-trivial block set. */
    static void islandGeneratesBlocks(GameTestHelper helper) {
        final IslandPlan p = plan(helper, "rocky", 1L);
        helper.assertTrue(!p.blocks().isEmpty(), "planIsland produced no blocks for 'rocky'");
        helper.assertTrue(p.blocks().size() > 100, "a rocky island should be more than 100 blocks");
        helper.succeed();
    }
}
