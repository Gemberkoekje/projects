package dev.gemberkoekje.skyseed.worldgen.structure;

import net.minecraft.SharedConstants;
import net.minecraft.core.BlockPos;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.IntTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.nbt.NbtIo;
import net.minecraft.nbt.NbtUtils;
import net.minecraft.world.level.block.state.BlockState;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * Serialises a relative block map (0-based positions) into a vanilla {@code StructureTemplate} {@code .nbt}
 * file — the same format Minecraft's structure blocks save, so the result loads through
 * {@link net.minecraft.world.level.levelgen.structure.templatesystem.StructureTemplateManager} and a
 * structure-block-authored {@code .nbt} would load equally well. Used as a dev-time generator to author
 * Skyseed's building templates in code (see {@link HamletTemplates}); not used at runtime.
 */
public final class StructureWriter {
    private StructureWriter() {}

    public static void write(Map<BlockPos, BlockState> blocks, Path file) throws IOException {
        int sx = 0, sy = 0, sz = 0;
        for (BlockPos p : blocks.keySet()) {
            sx = Math.max(sx, p.getX());
            sy = Math.max(sy, p.getY());
            sz = Math.max(sz, p.getZ());
        }

        final CompoundTag root = new CompoundTag();
        root.putInt("DataVersion", SharedConstants.getCurrentVersion().getDataVersion().getVersion());

        final ListTag size = new ListTag();
        size.add(IntTag.valueOf(sx + 1));
        size.add(IntTag.valueOf(sy + 1));
        size.add(IntTag.valueOf(sz + 1));
        root.put("size", size);

        final List<BlockState> palette = new ArrayList<>();
        final Map<BlockState, Integer> indexOf = new HashMap<>();
        final ListTag blocksTag = new ListTag();
        for (Map.Entry<BlockPos, BlockState> e : blocks.entrySet()) {
            final int idx = indexOf.computeIfAbsent(e.getValue(), st -> {
                palette.add(st);
                return palette.size() - 1;
            });
            final CompoundTag b = new CompoundTag();
            final ListTag pos = new ListTag();
            pos.add(IntTag.valueOf(e.getKey().getX()));
            pos.add(IntTag.valueOf(e.getKey().getY()));
            pos.add(IntTag.valueOf(e.getKey().getZ()));
            b.put("pos", pos);
            b.putInt("state", idx);
            blocksTag.add(b);
        }

        final ListTag paletteTag = new ListTag();
        for (BlockState st : palette) {
            paletteTag.add(NbtUtils.writeBlockState(st));
        }
        root.put("palette", paletteTag);
        root.put("blocks", blocksTag);
        root.put("entities", new ListTag());

        Files.createDirectories(file.getParent());
        NbtIo.writeCompressed(root, file);
    }
}
