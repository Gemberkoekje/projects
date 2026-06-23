package dev.gemberkoekje.skyseed.command;

import com.mojang.brigadier.CommandDispatcher;
import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.ChatFormatting;
import net.minecraft.commands.CommandSourceStack;
import net.minecraft.commands.Commands;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.Registries;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.NbtAccounter;
import net.minecraft.nbt.NbtIo;
import net.minecraft.nbt.Tag;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.ResourceKey;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.levelgen.NoiseBasedChunkGenerator;
import net.minecraft.world.level.storage.LevelResource;
import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.fml.common.EventBusSubscriber;
import net.neoforged.neoforge.event.RegisterCommandsEvent;
import net.neoforged.neoforge.event.server.ServerStoppedEvent;
import org.jetbrains.annotations.Nullable;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Comparator;
import java.util.stream.Stream;

/**
 * Debug/rescue commands {@code /emptynether} and {@code /emptyend}: convert a world created before the void
 * dimensions (v0.35.x) by wiping the old Nether/End and regenerating them with the Skyseed void generation.
 *
 * <p>A dimension's generator is baked into {@code level.dat} at creation, so this can't be done live without
 * risking corruption. Instead the command <em>arms</em> the reset and the work happens at {@link ServerStoppedEvent}
 * — after the world's final save, with the dimension's chunk IO already closed — where it is safe to rewrite the
 * one {@code settings} string in {@code level.dat} and delete the dimension's region folders. On the next load MC
 * builds the void dimension natively and the wiped chunks regenerate empty. The reset therefore applies when the
 * player exits to the title screen (or the server restarts), which the confirmation message makes clear.
 */
@EventBusSubscriber(modid = Skyseed.MODID)
public final class SkyseedCommands {
    private SkyseedCommands() {}

    /** A resettable dimension: its level key, the level.dat dimensions-map key, on-disk folder, and void settings id. */
    private record Target(ResourceKey<Level> dim, String dimKey, String folder, String voidPath, String label) {
        String voidSettingsId() {
            return Skyseed.MODID + ":" + voidPath;
        }

        ResourceKey<net.minecraft.world.level.levelgen.NoiseGeneratorSettings> voidSettingsKey() {
            return ResourceKey.create(Registries.NOISE_SETTINGS, ResourceLocation.fromNamespaceAndPath(Skyseed.MODID, voidPath));
        }
    }

    private static final Target NETHER = new Target(Level.NETHER, "minecraft:the_nether", "DIM-1", "void_nether", "Nether");
    private static final Target END = new Target(Level.END, "minecraft:the_end", "DIM1", "void_end", "End");

    // Armed by the command, applied (and cleared) at shutdown. Paths are captured while the server is live.
    private static volatile boolean armedNether = false;
    private static volatile boolean armedEnd = false;
    @Nullable
    private static volatile Path levelDatPath = null;
    @Nullable
    private static volatile Path worldRootPath = null;

    @SubscribeEvent
    static void onRegisterCommands(RegisterCommandsEvent event) {
        register(event.getDispatcher(), "emptynether", NETHER);
        register(event.getDispatcher(), "emptyend", END);
    }

    private static void register(CommandDispatcher<CommandSourceStack> dispatcher, String name, Target target) {
        dispatcher.register(Commands.literal(name)
                .requires(src -> src.hasPermission(2))
                .executes(ctx -> warn(ctx.getSource(), name, target))
                .then(Commands.literal("force").executes(ctx -> arm(ctx.getSource(), name, target))));
    }

    private static int warn(CommandSourceStack source, String name, Target target) {
        MinecraftServer server = source.getServer();
        if (alreadyVoid(server, target)) {
            source.sendSuccess(() -> Component.literal("[Skyseed] Your " + target.label
                    + " is already the empty Skyseed dimension — nothing to do.").withStyle(ChatFormatting.GREEN), false);
            return 0;
        }
        source.sendSuccess(() -> Component.literal(
                "⚠ This will PERMANENTLY DESTROY this world's " + target.label + " and regenerate it as the empty "
              + "Skyseed " + target.label + ". Everything you have built or stored there will be lost. If you are sure, run:")
                .withStyle(ChatFormatting.RED), false);
        source.sendSuccess(() -> Component.literal("/" + name + " force").withStyle(ChatFormatting.YELLOW), false);
        return 1;
    }

    private static int arm(CommandSourceStack source, String name, Target target) {
        MinecraftServer server = source.getServer();
        if (alreadyVoid(server, target)) {
            source.sendSuccess(() -> Component.literal("[Skyseed] Your " + target.label
                    + " is already the empty Skyseed dimension — nothing to do.").withStyle(ChatFormatting.GREEN), false);
            return 0;
        }

        // Capture the world paths now, while the storage is live; the shutdown handler reuses them.
        worldRootPath = server.getWorldPath(LevelResource.ROOT);
        levelDatPath = server.getWorldPath(LevelResource.LEVEL_DATA_FILE);
        if (target == NETHER) {
            armedNether = true;
        } else {
            armedEnd = true;
        }

        // Get any players out of the doomed dimension so they don't reload into wiped (void) chunks and fall.
        int moved = evacuate(server, target.dim());

        source.sendSuccess(() -> Component.literal(
                "[Skyseed] " + target.label + " reset armed. It will be wiped and regenerated as the empty Skyseed "
              + target.label + " the next time this world loads. Exit to the title screen (or restart the server) now "
              + "to apply." + (moved > 0 ? " Moved " + moved + " player(s) out of the " + target.label + "." : ""))
                .withStyle(ChatFormatting.GOLD), false);
        Skyseed.LOGGER.info("[skyseed] {} reset armed via /{} force", target.label, name);
        return 1;
    }

    /** Teleport every player currently in {@code dim} to the overworld spawn. Returns how many were moved. */
    private static int evacuate(MinecraftServer server, ResourceKey<Level> dim) {
        ServerLevel overworld = server.overworld();
        BlockPos spawn = overworld.getSharedSpawnPos();
        int moved = 0;
        for (ServerPlayer player : server.getPlayerList().getPlayers()) {
            if (player.level().dimension().equals(dim)) {
                player.teleportTo(overworld, spawn.getX() + 0.5, spawn.getY(), spawn.getZ() + 0.5,
                        player.getYRot(), player.getXRot());
                moved++;
            }
        }
        return moved;
    }

    private static boolean alreadyVoid(MinecraftServer server, Target target) {
        ServerLevel level = server.getLevel(target.dim());
        return level != null
                && level.getChunkSource().getGenerator() instanceof NoiseBasedChunkGenerator gen
                && gen.generatorSettings().is(target.voidSettingsKey());
    }

    @SubscribeEvent
    static void onServerStopped(ServerStoppedEvent event) {
        Path levelDat = levelDatPath;
        Path worldRoot = worldRootPath;
        if (levelDat != null && worldRoot != null) {
            if (armedNether) {
                applyReset(levelDat, worldRoot, NETHER);
            }
            if (armedEnd) {
                applyReset(levelDat, worldRoot, END);
            }
        }
        armedNether = false;
        armedEnd = false;
        levelDatPath = null;
        worldRootPath = null;
    }

    private static void applyReset(Path levelDat, Path worldRoot, Target target) {
        try {
            CompoundTag root = NbtIo.readCompressed(levelDat, NbtAccounter.unlimitedHeap());
            if (!swapDimensionSettings(root, target.dimKey(), target.voidSettingsId())) {
                Skyseed.LOGGER.warn("[skyseed] {} reset skipped: level.dat had no {} generator to switch (or it was already void)",
                        target.label, target.dimKey());
                return;
            }
            NbtIo.writeCompressed(root, levelDat);
            Path dim = worldRoot.resolve(target.folder());
            deleteRecursively(dim.resolve("region"));
            deleteRecursively(dim.resolve("entities"));
            deleteRecursively(dim.resolve("poi"));
            Skyseed.LOGGER.info("[skyseed] {} reset applied: level.dat now uses {} and the old chunks were wiped",
                    target.label, target.voidSettingsId());
        } catch (IOException e) {
            Skyseed.LOGGER.error("[skyseed] {} reset failed; the dimension was left untouched", target.label, e);
        }
    }

    /**
     * Rewrite {@code Data.WorldGenSettings.dimensions.<dimKey>.generator.settings} to {@code newSettingsId} in a
     * loaded {@code level.dat} root tag. Returns true only if the value was actually changed — false if the path is
     * absent (so we never wipe chunks we couldn't repoint) or it was already the target value. Package-visible and
     * pure so it can be unit-tested against a synthetic tag.
     */
    public static boolean swapDimensionSettings(CompoundTag root, String dimKey, String newSettingsId) {
        if (!root.contains("Data", Tag.TAG_COMPOUND)) return false;
        CompoundTag data = root.getCompound("Data");
        if (!data.contains("WorldGenSettings", Tag.TAG_COMPOUND)) return false;
        CompoundTag worldGen = data.getCompound("WorldGenSettings");
        if (!worldGen.contains("dimensions", Tag.TAG_COMPOUND)) return false;
        CompoundTag dimensions = worldGen.getCompound("dimensions");
        if (!dimensions.contains(dimKey, Tag.TAG_COMPOUND)) return false;
        CompoundTag dimension = dimensions.getCompound(dimKey);
        if (!dimension.contains("generator", Tag.TAG_COMPOUND)) return false;
        CompoundTag generator = dimension.getCompound("generator");
        if (!generator.contains("settings", Tag.TAG_STRING)) return false;
        if (generator.getString("settings").equals(newSettingsId)) return false;
        generator.putString("settings", newSettingsId);
        return true;
    }

    private static void deleteRecursively(Path dir) throws IOException {
        if (!Files.exists(dir)) {
            return;
        }
        try (Stream<Path> walk = Files.walk(dir)) {
            walk.sorted(Comparator.reverseOrder()).forEach(path -> {
                try {
                    Files.delete(path);
                } catch (IOException e) {
                    Skyseed.LOGGER.warn("[skyseed] could not delete {} during dimension reset", path, e);
                }
            });
        }
    }
}
