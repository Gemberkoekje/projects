package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.HolderLookup;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.nbt.StringTag;
import net.minecraft.nbt.Tag;
import net.minecraft.world.level.saveddata.SavedData;
import org.jetbrains.annotations.Nullable;

import java.util.HashSet;
import java.util.Set;
import java.util.UUID;

/**
 * Per-world persisted flags: whether the curated start island was placed and where the player spawns on it,
 * plus which players have already been handled on join (given the guide, dropped on the start island). The
 * join flags live here rather than on the player's persistent data so they reliably survive relogs.
 */
public final class SkyseedWorldData extends SavedData {
    public static final String NAME = Skyseed.MODID + "_world";

    private boolean startPlaced = false;
    @Nullable
    private BlockPos startSpawn = null;
    private final Set<UUID> guided = new HashSet<>();
    private final Set<UUID> spawned = new HashSet<>();

    public static SavedData.Factory<SkyseedWorldData> factory() {
        return new SavedData.Factory<>(SkyseedWorldData::new, SkyseedWorldData::load);
    }

    private static SkyseedWorldData load(CompoundTag tag, HolderLookup.Provider provider) {
        SkyseedWorldData data = new SkyseedWorldData();
        data.startPlaced = tag.getBoolean("StartPlaced");
        if (tag.contains("SpawnX")) {
            data.startSpawn = new BlockPos(tag.getInt("SpawnX"), tag.getInt("SpawnY"), tag.getInt("SpawnZ"));
        }
        readUuids(tag, "Guided", data.guided);
        readUuids(tag, "Spawned", data.spawned);
        return data;
    }

    @Override
    public CompoundTag save(CompoundTag tag, HolderLookup.Provider provider) {
        tag.putBoolean("StartPlaced", startPlaced);
        if (startSpawn != null) {
            tag.putInt("SpawnX", startSpawn.getX());
            tag.putInt("SpawnY", startSpawn.getY());
            tag.putInt("SpawnZ", startSpawn.getZ());
        }
        tag.put("Guided", writeUuids(guided));
        tag.put("Spawned", writeUuids(spawned));
        return tag;
    }

    private static void readUuids(CompoundTag tag, String key, Set<UUID> into) {
        for (Tag t : tag.getList(key, Tag.TAG_STRING)) {
            try {
                into.add(UUID.fromString(t.getAsString()));
            } catch (IllegalArgumentException ignored) {
                // skip a malformed id rather than fail the whole load
            }
        }
    }

    private static ListTag writeUuids(Set<UUID> from) {
        ListTag list = new ListTag();
        for (UUID id : from) {
            list.add(StringTag.valueOf(id.toString()));
        }
        return list;
    }

    public boolean isStartPlaced() {
        return startPlaced;
    }

    @Nullable
    public BlockPos getStartSpawn() {
        return startSpawn;
    }

    /** Mark the start island as handled for this world; {@code spawn} is null for existing worlds (no island). */
    public void markStartPlaced(@Nullable BlockPos spawn) {
        this.startPlaced = true;
        this.startSpawn = spawn;
        setDirty();
    }

    /** Whether this player has already received the guide book (granted once, on first join). */
    public boolean hasGuided(UUID player) {
        return guided.contains(player);
    }

    public void markGuided(UUID player) {
        if (guided.add(player)) {
            setDirty();
        }
    }

    /** Whether this player has already been dropped on the start island (done once, on first join). */
    public boolean hasSpawned(UUID player) {
        return spawned.contains(player);
    }

    public void markSpawned(UUID player) {
        if (spawned.add(player)) {
            setDirty();
        }
    }
}
