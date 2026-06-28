package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.Skyseed;
//? if >=26.1.2 {
/*import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import dev.gemberkoekje.skyseed.compat.Ids;
import net.minecraft.core.UUIDUtil;
import net.minecraft.world.level.saveddata.SavedDataType;*/
//?}
import net.minecraft.core.BlockPos;
import net.minecraft.core.HolderLookup;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.nbt.StringTag;
import net.minecraft.nbt.Tag;
import net.minecraft.world.level.saveddata.SavedData;
import org.jetbrains.annotations.Nullable;

import java.util.HashSet;
import java.util.Optional;
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
    @Nullable
    private String createdVersion = null;
    private final Set<UUID> guided = new HashSet<>();
    private final Set<UUID> spawned = new HashSet<>();

    /** No-arg constructor used for a fresh instance (and by the codec's {@code SavedDataType} supplier on 26.1.2). */
    public SkyseedWorldData() {
    }

    // 26.1.2 replaced SavedData's save(CompoundTag)/load model with a Codec registered through a SavedDataType.
    //? if >=26.1.2 {
    /*public static final Codec<SkyseedWorldData> CODEC = RecordCodecBuilder.create(i -> i.group(
            Codec.BOOL.optionalFieldOf("StartPlaced", false).forGetter(d -> d.startPlaced),
            BlockPos.CODEC.optionalFieldOf("StartSpawn").forGetter(d -> Optional.ofNullable(d.startSpawn)),
            Codec.STRING.optionalFieldOf("CreatedVersion").forGetter(d -> Optional.ofNullable(d.createdVersion)),
            UUIDUtil.CODEC_SET.optionalFieldOf("Guided", new HashSet<>()).forGetter(d -> d.guided),
            UUIDUtil.CODEC_SET.optionalFieldOf("Spawned", new HashSet<>()).forGetter(d -> d.spawned)
    ).apply(i, SkyseedWorldData::new));

    public static final SavedDataType<SkyseedWorldData> TYPE =
            new SavedDataType<>(Ids.mod("world"), SkyseedWorldData::new, CODEC);

    private SkyseedWorldData(boolean startPlaced, Optional<BlockPos> startSpawn, Optional<String> createdVersion,
                             Set<UUID> guided, Set<UUID> spawned) {
        this.startPlaced = startPlaced;
        this.startSpawn = startSpawn.orElse(null);
        this.createdVersion = createdVersion.orElse(null);
        this.guided.addAll(guided);
        this.spawned.addAll(spawned);
    }
    *///?} else {
    public static SavedData.Factory<SkyseedWorldData> factory() {
        return new SavedData.Factory<>(SkyseedWorldData::new, SkyseedWorldData::load);
    }

    private static SkyseedWorldData load(CompoundTag tag, HolderLookup.Provider provider) {
        SkyseedWorldData data = new SkyseedWorldData();
        data.startPlaced = tag.getBoolean("StartPlaced");
        if (tag.contains("SpawnX")) {
            data.startSpawn = new BlockPos(tag.getInt("SpawnX"), tag.getInt("SpawnY"), tag.getInt("SpawnZ"));
        }
        if (tag.contains("CreatedVersion")) {
            data.createdVersion = tag.getString("CreatedVersion");
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
        if (createdVersion != null) {
            tag.putString("CreatedVersion", createdVersion);
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
    //?}

    public boolean isStartPlaced() {
        return startPlaced;
    }

    @Nullable
    public BlockPos getStartSpawn() {
        return startSpawn;
    }

    /** The Skyseed version this world was created on, or null for worlds made before stamping (pre-0.35.2). */
    @Nullable
    public String getCreatedVersion() {
        return createdVersion;
    }

    public void setCreatedVersion(String version) {
        this.createdVersion = version;
        setDirty();
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
