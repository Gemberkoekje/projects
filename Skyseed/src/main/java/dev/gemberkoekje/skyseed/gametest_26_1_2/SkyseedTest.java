package dev.gemberkoekje.skyseed.gametest_26_1_2;

import com.mojang.serialization.MapCodec;
import net.minecraft.core.Holder;
import net.minecraft.gametest.framework.GameTestHelper;
import net.minecraft.gametest.framework.GameTestInstance;
import net.minecraft.gametest.framework.TestData;
import net.minecraft.gametest.framework.TestEnvironmentDefinition;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.MutableComponent;

import java.util.function.Consumer;

/**
 * Minimal {@link GameTestInstance} that runs a plain {@code Consumer<GameTestHelper>} body. Skyseed's 26.1.2 gametests
 * are registered in code via {@link net.neoforged.neoforge.event.RegisterGameTestsEvent} (RegistrationInfo.BUILT_IN),
 * so they are never serialized — the {@link #codec()} contract is satisfied by a unit codec that is never invoked
 * (see {@code GAMETESTPLAN.md}; if a run path ever does serialize built-in instances, the fallback is
 * {@code FunctionGameTestInstance} + a registered {@code TEST_INSTANCE_TYPE} codec).
 */
final class SkyseedTest extends GameTestInstance {
    static final MapCodec<SkyseedTest> CODEC = MapCodec.<SkyseedTest>unit(() -> {
        throw new UnsupportedOperationException("Skyseed gametests are code-registered, not serialized");
    });

    private final Consumer<GameTestHelper> body;

    SkyseedTest(TestData<Holder<TestEnvironmentDefinition<?>>> data, Consumer<GameTestHelper> body) {
        super(data);
        this.body = body;
    }

    @Override
    public void run(GameTestHelper helper) {
        body.accept(helper);
    }

    @Override
    public MapCodec<? extends GameTestInstance> codec() {
        return CODEC;
    }

    @Override
    protected MutableComponent typeDescription() {
        return Component.literal("Skyseed gametest");
    }
}
