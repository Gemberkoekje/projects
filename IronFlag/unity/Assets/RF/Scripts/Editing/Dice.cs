using UnityEngine;

namespace IronFlag.Editing
{
    /// <summary>
    /// The dice a map generator rolls: a stream of numbers that reads as arbitrary and is
    /// nothing of the kind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Hashed, never random</strong>, for the same reason
    /// <see cref="IronFlag.Levels.SurfaceNoise"/> is: a map has to be the same map twice. A
    /// generated level writes the seed it was drawn from into
    /// <see cref="IronFlag.Levels.LevelDefinition.Seed"/>, so the coastline that seed picks
    /// and the layout it rolled are one map rather than two - and a seed somebody liked can
    /// be typed back in, shared, or put in a bug report. None of that survives
    /// <see cref="UnityEngine.Random"/>, which is also global mutable state that a generator
    /// running inside an editor would be stealing from whatever else is drawing from it.
    /// </para>
    /// <para>
    /// A class rather than a struct, and that is not an oversight. The counter moves with
    /// every roll, and a struct handed to a method would be handed a copy - so a helper that
    /// rolled three numbers would leave the caller's dice exactly where it found them, and
    /// every branch of a generator would draw the same numbers as its neighbour. That bug is
    /// invisible in a level file and obvious in a picture of one.
    /// </para>
    /// <para>
    /// It is not a general-purpose generator and does not try to be one. There is no
    /// distribution here beyond "flat", because everything asking is asking where to put a
    /// tree.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var dice = new Dice(level.Seed);
    /// float acrossHalf = dice.Between(24.0f, 40.0f);
    /// bool second = dice.Chance(0.5f);
    /// </code>
    /// </example>
    public sealed class Dice
    {
        private readonly int seed;
        private int rolled;

        /// <summary>
        /// Makes a stream of numbers from a seed.
        /// </summary>
        /// <param name="seed">The seed. Zero is a seed like any other.</param>
        public Dice(int seed) => this.seed = seed;

        /// <summary>The seed this stream was made from.</summary>
        public int Seed => seed;

        /// <summary>How many numbers have been taken out of it.</summary>
        /// <remarks>
        /// Worth having in a test: a generator that draws a different <em>count</em> of
        /// numbers down two branches is one whose maps shift wholesale when an unrelated
        /// detail changes, which is exactly what <see cref="Branch"/> exists to stop.
        /// </remarks>
        public int Rolled => rolled;

        /// <summary>
        /// Takes the next number, from zero up to but not including one.
        /// </summary>
        /// <returns>A fraction.</returns>
        public float Unit()
        {
            uint mixed = Mix(seed, rolled);
            rolled++;
            return (mixed & 0xFFFFFF) / (float)0x1000000;
        }

        /// <summary>
        /// Takes the next number, somewhere between two others.
        /// </summary>
        /// <param name="least">One end.</param>
        /// <param name="most">The other end.</param>
        /// <returns>A number between them.</returns>
        public float Between(float least, float most) => Mathf.Lerp(least, most, Unit());

        /// <summary>
        /// Takes the next number as an offset either side of nothing.
        /// </summary>
        /// <param name="spread">How far either way.</param>
        /// <returns>A number from minus the spread to plus it.</returns>
        /// <remarks>
        /// The shape almost every jitter in a generator wants, written once so that "a bit
        /// off centre" does not get spelled three different ways.
        /// </remarks>
        public float Spread(float spread) => Between(-spread, spread);

        /// <summary>
        /// Takes the next number as one of a run of choices.
        /// </summary>
        /// <param name="count">How many there are.</param>
        /// <returns>A choice from zero to one less than the count, or zero when there is one.</returns>
        public int Upto(int count)
            => count <= 1 ? 0 : Mathf.Min(count - 1, Mathf.FloorToInt(Unit() * count));

        /// <summary>
        /// Takes the next number and calls it.
        /// </summary>
        /// <param name="odds">How often it should come up, from zero to one.</param>
        /// <returns><c>true</c> that often.</returns>
        public bool Chance(float odds) => Unit() < odds;

        /// <summary>
        /// Makes a second stream that will not disturb this one.
        /// </summary>
        /// <param name="which">Which branch, so two branches differ.</param>
        /// <returns>A stream of its own.</returns>
        /// <remarks>
        /// <para>
        /// What keeps an asymmetrical map's two halves independent in the way that matters:
        /// each side draws from its own stream, so adding a roll to one side's generation
        /// does not silently redraw the other side's. Without it, every future change to
        /// half the generator moves the whole map for every seed ever noted down.
        /// </para>
        /// <para>
        /// Branch numbers are hashed into the negative half of the counter's range, so a
        /// branch can never collide with an ordinary roll off the parent however long the
        /// parent runs.
        /// </para>
        /// </remarks>
        public Dice Branch(int which)
        {
            unchecked
            {
                return new Dice((int)Mix(seed, -1 - Mathf.Abs(which)));
            }
        }

        /// <summary>
        /// Hashes a seed and a step into a number.
        /// </summary>
        /// <param name="seed">The stream's seed.</param>
        /// <param name="step">Which number in the stream.</param>
        /// <returns>Well-spread bits.</returns>
        /// <remarks>
        /// Integer arithmetic all the way, exactly as
        /// <c>SurfaceNoise.Lattice</c> is and for the same reason: two builds of this game
        /// have to agree about where a map's trees are, and a float that differs in its last
        /// bit is a tree in a different place.
        /// </remarks>
        private static uint Mix(int seed, int step)
        {
            unchecked
            {
                uint mixed = ((uint)seed * 2246822519u) + ((uint)step * 3266489917u) + 374761393u;
                mixed = (mixed ^ (mixed >> 15)) * 2246822519u;
                mixed = (mixed ^ (mixed >> 13)) * 3266489917u;
                return mixed ^ (mixed >> 16);
            }
        }
    }
}
