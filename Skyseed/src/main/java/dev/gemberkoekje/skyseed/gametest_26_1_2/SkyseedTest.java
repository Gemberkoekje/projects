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
 * are registered in code via {@link net.neoforged.neoforge.event.RegisterGameTestsEvent}, but that does NOT make them
 * exempt from serialization: {@code test_instance} is a network-synced registry, so the client handshake
 * ({@code RegistrySynchronization.packRegistry}) encodes every entry — a throwing codec hangs {@code runClient} on
 * "Loading terrain". So {@link #CODEC} is a real type codec, registered in {@code TEST_INSTANCE_TYPE} as
 * {@code skyseed:gametest} (see {@link SkyseedTests#onRegisterTestInstanceType}). The {@code Consumer} body can't
 * round-trip, but only the encode side matters for the handshake; decode yields a no-op test (the decoding side — the
 * client — never runs gametests). The server keeps the real body because tests run from the in-memory instance, not a
 * decoded one.
 */
final class SkyseedTest extends GameTestInstance {
    static final MapCodec<SkyseedTest> CODEC = TestData.CODEC.xmap(
            data -> new SkyseedTest(data, helper -> {}), SkyseedTest::info);

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
