package dev.gemberkoekje.skyseed.worldgen;

/**
 * Debug-seed forcing flags threaded into island generation so an auto debug seed can pin an otherwise chance-based
 * variant: {@code rareIndex} &ge; 0 forces the rare structure at that index into the theme's {@code rare_structures}
 * (bypassing the chance roll), and {@code waterfall} forces the ladder shaft's water-column variant
 * ({@code ladder_shaft.waterfall_chance}). {@link #NONE} is the no-forcing default for ordinary seeds. Add a field
 * here for each new chance-based variant the debug scan ({@link dev.gemberkoekje.skyseed.registry.ThemeScanner})
 * learns to cover.
 */
public record DebugForce(int rareIndex, boolean waterfall) {
    public static final DebugForce NONE = new DebugForce(-1, false);

    /** Force the rare structure at {@code index} (and nothing else). */
    public static DebugForce rare(int index) {
        return new DebugForce(index, false);
    }
}
