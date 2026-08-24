using UnityEngine;

namespace IronFlag.Levels
{
    /// <summary>
    /// The wobble that turns a drawn coastline into one that wanders, and the rules about
    /// how far it is allowed to wander.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Hashed, never random.</strong> The copy of a map baked into
    /// <c>Sandbox.unity</c> and the copy <see cref="LevelLoader"/> builds from the file on
    /// the first frame are compared prop for prop by the wiring tests, and a coastline drawn
    /// from <see cref="UnityEngine.Random"/> would make those two different maps - and make
    /// them different <em>intermittently</em>, which is worse than making them different
    /// every time. Everything here is a function of a position and
    /// <see cref="LevelDefinition.Seed"/> and of nothing else, so one map has one coastline
    /// forever and two maps have two.
    /// </para>
    /// <para>
    /// Value noise rather than anything cleverer: a random height at every lattice point,
    /// smoothly interpolated between them, two octaves. It is a few lines, it has no
    /// gradients to store, and what it is being asked for is a coast that is not a ruler -
    /// not a landscape.
    /// </para>
    /// <para>
    /// The three numbers below are the whole of the look, and they are read against the
    /// margins the rest of the game already measures rather than chosen for their own sake.
    /// See each one.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// float wobble = SurfaceNoise.At(x, z, level.Seed) * SurfaceNoise.Amplitude;
    /// </code>
    /// </example>
    public static class SurfaceNoise
    {
        /// <summary>How far the coastline may move, in metres.</summary>
        /// <remarks>
        /// <para>
        /// Measured off a render rather than reasoned about, and it took two goes. The first
        /// number here was 1.5, chosen to stay inside
        /// <see cref="LevelValidation.ShoreMargin"/> - and it came out as a coast you had to
        /// look for. The reason is worth knowing before anybody tunes this again:
        /// <strong>value noise only reaches its full height at a lattice point</strong>, so
        /// the coast typically moves about half of whatever is written here and reaches the
        /// whole of it perhaps seven times along a shore. Twice the number is twice the
        /// wander, but the number is not the wander.
        /// </para>
        /// <para>
        /// Three metres is past the 2.5 m that every prop on the map is required to keep
        /// between itself and the water, which sounds worse than it is: since
        /// <see cref="LevelValidation"/> measures that against <see cref="SurfaceField"/>
        /// rather than against the rectangles, a prop the coast wandered up to is caught and
        /// named rather than quietly left standing in the sea. What this number really costs
        /// is that a map has less room to place things near a shore, and the shipped map has
        /// nothing close enough to notice: the tightest thing on it is a tree drawn 3 m
        /// inland, which the realised coast leaves standing 3.5 m from the water.
        /// </para>
        /// <para>
        /// It is still a cap rather than a dial. Somewhere past here the wobble stops being a
        /// coastline and starts being a way of losing an argument with a level file.
        /// </para>
        /// </remarks>
        public const float Amplitude = 3.0f;

        /// <summary>How far apart the wobbles are, in metres.</summary>
        /// <remarks>
        /// Roughly seven waves along the 164 m shore of the shipped map, which is a coast
        /// that reads as drawn by hand rather than one that reads as noisy. Much shorter and
        /// the coast frays at the cell size and stops being a shape; much longer and a whole
        /// shore drifts one way, which is a rectangle in a different place rather than a
        /// coastline.
        /// </remarks>
        public const float Wavelength = 24.0f;

        /// <summary>Metres either side of a built edge that are kept exactly as drawn.</summary>
        /// <remarks>
        /// A causeway is 12 m wide because somebody typed 12, and each bridgehead leaves
        /// 13 m of narrows because a test computed the jeep's ballistic jump and 13 is what
        /// clears it. Noise that reached those would make both of them approximately true,
        /// which is the one thing a built surface exists not to be. So the wobble is held at
        /// zero within this of anything <see cref="SurfaceTuning.NaturalEdge"/> says was
        /// built, on both sides of it: the road's own edge and the water in front of it.
        /// </remarks>
        public const float Guard = 2.0f;

        /// <summary>Metres over which the wobble comes back in past the guard.</summary>
        /// <remarks>
        /// Without it a shore would step by the full amplitude where it stops being guarded,
        /// and a step is a notch - the one shape on a coastline that reads as a mistake
        /// rather than as a bay. Wide enough to swallow the amplitude at a gentle angle, and
        /// narrow enough that a crossing does not flatten the shore it lands on.
        /// </remarks>
        public const float Blend = 6.0f;

        /// <summary>How much of the wobble the second, finer octave is.</summary>
        private const float Detail = 0.4f;

        /// <summary>
        /// Returns the wobble at a place on the map.
        /// </summary>
        /// <param name="x">Where across the map, in metres.</param>
        /// <param name="z">Where up the map, in metres.</param>
        /// <param name="seed">The level's seed.</param>
        /// <returns>A number from -1 to 1, smooth in both directions.</returns>
        /// <remarks>
        /// Multiply by <see cref="Amplitude"/> to get metres. Two octaves, the second at
        /// half the wavelength and <see cref="Detail"/> of the height, normalised so the
        /// result still fills -1 to 1 - so changing the detail changes how ragged the coast
        /// is and not how far it moves.
        /// </remarks>
        public static float At(float x, float z, int seed)
        {
            float coarse = Octave(x / Wavelength, z / Wavelength, seed);
            float fine = Octave(x * 2.0f / Wavelength, z * 2.0f / Wavelength, seed + 1);
            return ((coarse + (fine * Detail)) / (1.0f + Detail));
        }

        /// <summary>
        /// Returns how much of the wobble applies this far from something built.
        /// </summary>
        /// <param name="toBuilt">
        /// Metres to the nearest built edge, positive or negative - only how far, not which
        /// side, because a road's own edge and the water in front of it both have to stay
        /// where they were drawn.
        /// </param>
        /// <returns>Zero inside the guard, one past the blend, and smooth between them.</returns>
        public static float Weight(float toBuilt)
            => Mathf.SmoothStep(0.0f, 1.0f, (Mathf.Abs(toBuilt) - Guard) / Blend);

        /// <summary>
        /// Runs one octave of value noise.
        /// </summary>
        /// <param name="x">Position across, in lattice units.</param>
        /// <param name="z">Position up, in lattice units.</param>
        /// <param name="seed">The level's seed, shifted per octave.</param>
        /// <returns>A number from -1 to 1.</returns>
        private static float Octave(float x, float z, int seed)
        {
            int west = Mathf.FloorToInt(x);
            int south = Mathf.FloorToInt(z);

            // Smoothstep rather than a straight lerp between lattice points. A linear
            // interpolation leaves a crease along every lattice line, and a coastline drawn
            // from it comes out as a run of straight segments meeting at angles - which is
            // what the rectangles it is meant to soften already looked like.
            float acrossTo = Ease(x - west);
            float upTo = Ease(z - south);

            float south0 = Mathf.Lerp(Lattice(west, south, seed), Lattice(west + 1, south, seed), acrossTo);
            float north0 = Mathf.Lerp(
                Lattice(west, south + 1, seed), Lattice(west + 1, south + 1, seed), acrossTo);

            return Mathf.Lerp(south0, north0, upTo);
        }

        /// <summary>
        /// Eases a fraction so the interpolation has no corner at either end.
        /// </summary>
        /// <param name="along">How far between two lattice points, from zero to one.</param>
        /// <returns>The eased fraction.</returns>
        private static float Ease(float along) => Mathf.SmoothStep(0.0f, 1.0f, along);

        /// <summary>
        /// Returns the height of one lattice point.
        /// </summary>
        /// <param name="x">Lattice column.</param>
        /// <param name="z">Lattice row.</param>
        /// <param name="seed">The level's seed.</param>
        /// <returns>A number from -1 to 1, the same one every time.</returns>
        /// <remarks>
        /// Integer arithmetic all the way to the last line, so the answer does not depend on
        /// the order a compiler folded anything in: two builds of this game have to agree
        /// about where a coastline is, and a float that differs in its last bit is a cell
        /// that differs in its surface.
        /// </remarks>
        private static float Lattice(int x, int z, int seed)
        {
            unchecked
            {
                uint mixed = (uint)(x * 374761393) + (uint)(z * 668265263) + ((uint)seed * 2246822519u);
                mixed = (mixed ^ (mixed >> 13)) * 1274126177u;
                mixed ^= mixed >> 16;
                return ((mixed & 0xFFFFFF) / (float)0xFFFFFF * 2.0f) - 1.0f;
            }
        }
    }
}
