package dev.gemberkoekje.skyseed.worldgen;

import dev.gemberkoekje.skyseed.Skyseed;
import net.minecraft.core.HolderLookup;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.saveddata.SavedData;

/** Per-world persisted flags. Currently just whether the curated starting island has been placed. */
public final class SkyseedWorldData extends SavedData {
    public static final String NAME = Skyseed.MODID + "_world";

    private boolean startPlaced = false;

    public static SavedData.Factory<SkyseedWorldData> factory() {
        return new SavedData.Factory<>(SkyseedWorldData::new, SkyseedWorldData::load);
    }

    private static SkyseedWorldData load(CompoundTag tag, HolderLookup.Provider provider) {
        SkyseedWorldData data = new SkyseedWorldData();
        data.startPlaced = tag.getBoolean("StartPlaced");
        return data;
    }

    @Override
    public CompoundTag save(CompoundTag tag, HolderLookup.Provider provider) {
        tag.putBoolean("StartPlaced", startPlaced);
        return tag;
    }

    public boolean isStartPlaced() {
        return startPlaced;
    }

    public void markStartPlaced() {
        this.startPlaced = true;
        setDirty();
    }
}
