package dev.gemberkoekje.skyseed.worldgen.structure;

import static dev.gemberkoekje.skyseed.worldgen.structure.StructureParts.*;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * The <b>End City</b> (SKYENDPLAN Phase 3 — the chapter's flagship structure seed). A purpur tower with a cantilevered
 * <b>End ship</b>, the End's parallel to the Mansion / Bastion. The tower (7×7, corner purpur-pillars, end-rod lit,
 * climbed by an internal ladder) carries a ground-floor {@code end_city_treasure} chest; the ship — a tapering purpur
 * hull, a mast amidships and a dragon head at the bow — holds the chapter's reward in its chest: a guaranteed
 * <b>elytra</b> via the custom {@code skyseed:chests/end_ship} table. Shulkers spawn on the island (theme mobs) for
 * shells. End-only; grown from the End City Seed.
 */
public final class EndCityTemplates {
    private EndCityTemplates() {}

    private static final BlockState PURPUR = Blocks.PURPUR_BLOCK.defaultBlockState();
    private static final BlockState PILLAR = Blocks.PURPUR_PILLAR.defaultBlockState();
    private static final BlockState ROD = Blocks.END_ROD.defaultBlockState();   // FACING up by default

    public static void generateInto(Path dir) throws IOException {
        writeIfAbsent(dir.resolve("city.nbt"), city());
    }

    private static Built city() {
        final Map<BlockPos, BlockState> m = new HashMap<>();
        final Map<BlockPos, CompoundTag> bes = new HashMap<>();

        // === Tower: 7×7 (x,z 0..6). Floor y1, walls y2..y9, top floor y10. ===
        for (int x = 0; x <= 6; x++)
            for (int z = 0; z <= 6; z++)
                m.put(new BlockPos(x, 1, z), PURPUR);                       // floor

        for (int y = 2; y <= 9; y++) {
            for (int x = 0; x <= 6; x++) {
                for (int z = 0; z <= 6; z++) {
                    if (!(x == 0 || x == 6 || z == 0 || z == 6)) continue;  // perimeter only
                    final boolean corner = (x == 0 || x == 6) && (z == 0 || z == 6);
                    m.put(new BlockPos(x, y, z), corner ? PILLAR : PURPUR);
                }
            }
        }
        m.remove(new BlockPos(1, 2, 0)); m.remove(new BlockPos(1, 3, 0));   // doorway (south wall)
        for (final int[] w : new int[][]{{5, 6, 0}, {1, 6, 6}, {5, 6, 6}, {0, 6, 2}, {0, 6, 4}, {6, 6, 2}, {6, 6, 4}})
            m.remove(new BlockPos(w[0], w[1], w[2]));                       // window slits

        for (int x = 0; x <= 6; x++)
            for (int z = 0; z <= 6; z++)
                m.put(new BlockPos(x, 10, z), PURPUR);                      // top floor
        m.remove(new BlockPos(3, 10, 1));                                  // ladder hole

        final BlockState ladder = Blocks.LADDER.defaultBlockState()
                .setValue(BlockStateProperties.HORIZONTAL_FACING, Direction.SOUTH);   // attached to the z=0 wall
        for (int y = 2; y <= 10; y++) m.put(new BlockPos(3, y, 1), ladder);

        // Ground-floor loot (vanilla End City treasure), corner rods for light, rod-crowned corner pillars.
        m.put(new BlockPos(4, 2, 4), Blocks.CHEST.defaultBlockState().setValue(BlockStateProperties.HORIZONTAL_FACING, Direction.NORTH));
        bes.put(new BlockPos(4, 2, 4), lootChest("minecraft:chests/end_city_treasure"));
        for (final int[] c : new int[][]{{1, 2, 1}, {5, 2, 5}, {1, 2, 5}}) m.put(new BlockPos(c[0], c[1], c[2]), ROD);
        for (final int[] c : new int[][]{{0, 11, 0}, {6, 11, 0}, {0, 11, 6}, {6, 11, 6}}) m.put(new BlockPos(c[0], c[1], c[2]), ROD);

        // === Ship: cantilevered +x off the tower top. Hull bottom y10, deck stood on at y11. ===
        for (int x = 7; x <= 12; x++)
            for (int z = 2; z <= 4; z++)
                m.put(new BlockPos(x, 10, z), PURPUR);                      // hull body
        m.put(new BlockPos(13, 10, 3), PURPUR);                            // hull taper
        m.put(new BlockPos(14, 10, 3), PURPUR);                            // bow tip
        for (int x = 8; x <= 12; x++) { m.put(new BlockPos(x, 11, 2), PURPUR); m.put(new BlockPos(x, 11, 4), PURPUR); }  // gunwales
        m.put(new BlockPos(13, 11, 3), PURPUR);
        m.put(new BlockPos(14, 11, 3), PURPUR);                            // bow gunwale (stern x=7 left open = boarding gap)
        for (int y = 11; y <= 15; y++) m.put(new BlockPos(10, y, 3), PILLAR);   // mast
        m.put(new BlockPos(10, 16, 3), ROD);                              // masthead light
        m.put(new BlockPos(12, 11, 3), ROD);                             // deck light

        // Dragon head perched on the bow, facing out (+x ≈ rotation 12 of 16).
        m.put(new BlockPos(14, 12, 3), Blocks.DRAGON_HEAD.defaultBlockState().setValue(BlockStateProperties.ROTATION_16, 12));
        // The reward chest (guaranteed elytra) sits at the stern so it's reached the moment you board from the tower;
        // a brewing stand just past it is a ship detail (the mast amidships closes off the bow walkway).
        m.put(new BlockPos(8, 11, 3), Blocks.CHEST.defaultBlockState().setValue(BlockStateProperties.HORIZONTAL_FACING, Direction.WEST));
        bes.put(new BlockPos(8, 11, 3), lootChest("skyseed:chests/end_ship"));
        m.put(new BlockPos(9, 11, 3), Blocks.BREWING_STAND.defaultBlockState());

        StructureParts.anchor(m, bes, new BlockPos(3, 0, 3), "minecraft:end_stone");
        return new Built(m, bes);
    }
}
