package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.BlockPos;
import net.minecraft.core.HolderLookup;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.saveddata.SavedData;
import org.jetbrains.annotations.Nullable;

/** Per-world persisted flags: whether the curated start island was placed, and where the player spawns on it. */
public final class SkyseedWorldData extends SavedData {
    public static final String NAME = Skyseed.MODID + "_world";

    private boolean startPlaced = false;
    @Nullable
    private BlockPos startSpawn = null;

    public static SavedData.Factory<SkyseedWorldData> factory() {
        return new SavedData.Factory<>(SkyseedWorldData::new, SkyseedWorldData::load);
    }

    private static SkyseedWorldData load(CompoundTag tag, HolderLookup.Provider provider) {
        SkyseedWorldData data = new SkyseedWorldData();
        data.startPlaced = tag.getBoolean("StartPlaced");
        if (tag.contains("SpawnX")) {
            data.startSpawn = new BlockPos(tag.getInt("SpawnX"), tag.getInt("SpawnY"), tag.getInt("SpawnZ"));
        }
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
        return tag;
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
}
