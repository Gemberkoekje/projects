package dev.gemberkoekje.skyseed.worldgen.feature;

import com.mojang.serialization.Codec;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.WorldGenLevel;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.levelgen.feature.Feature;
import net.minecraft.world.level.levelgen.feature.FeaturePlaceContext;
import net.minecraft.world.level.levelgen.feature.configurations.NoneFeatureConfiguration;

/**
 * Grows the <b>central End island</b> back into Skyseed's void End. The void End has {@code final_density: 0.0} — no
 * terrain anywhere — so the island the Ender Dragon perches on (and the exit-portal fountain stands on) never
 * generated; only the bare bedrock egg-spike was placed, over the void. This feature lays a domed end-stone disk at the
 * world origin so the vanilla dragon fight, its exit fountain, and the four-crystal respawn all have a floor again.
 *
 * <p>Added to the {@code minecraft:the_end} biome at {@code raw_generation} via a NeoForge biome modifier, so it runs
 * during chunk generation (before a player ever enters and the fight initialises). It only places within {@link #RADIUS}
 * of origin — every other End chunk is left as void.
 */
public final class CentralEndIslandFeature extends Feature<NoneFeatureConfiguration> {
    private static final int RADIUS = 42;       // reaches the obsidian pillars (~43) so the healing crystals stay accessible
    private static final int SURFACE_Y = 63;    // the vanilla central-island surface height the fountain/spikes expect
    private static final BlockState END_STONE = Blocks.END_STONE.defaultBlockState();

    public CentralEndIslandFeature(Codec<NoneFeatureConfiguration> codec) {
        super(codec);
    }

    @Override
    public boolean place(FeaturePlaceContext<NoneFeatureConfiguration> ctx) {
        final WorldGenLevel level = ctx.level();
        final int baseX = ctx.origin().getX();
        final int baseZ = ctx.origin().getZ();
        if (Math.abs(baseX) - 16 > RADIUS || Math.abs(baseZ) - 16 > RADIUS) {
            return false;   // cheap reject: this chunk is wholly outside the central island
        }
        final BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
        boolean placed = false;
        for (int dx = 0; dx < 16; dx++) {
            for (int dz = 0; dz < 16; dz++) {
                final int x = baseX + dx;
                final int z = baseZ + dz;
                final int d2 = x * x + z * z;
                if (d2 > RADIUS * RADIUS) {
                    continue;
                }
                final int depth = 4 + (int) (12.0 * (1.0 - Math.sqrt(d2) / RADIUS)); // domed: ~16 deep centre, ~4 at the rim
                for (int y = SURFACE_Y; y > SURFACE_Y - depth; y--) {
                    pos.set(x, y, z);
                    setBlock(level, pos, END_STONE);
                }
                placed = true;
            }
        }
        return placed;
    }
}
