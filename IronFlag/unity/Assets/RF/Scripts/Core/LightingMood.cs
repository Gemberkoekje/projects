using System;

namespace IronFlag.Core
{
    /// <summary>
    /// Which set of lighting conditions a scene is lit under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One member per condition, for the same reason <see cref="IronFlag.Levels.SurfaceKind"/>
    /// is an enum: a look balanced by being read against the others is something a diff can
    /// show. Adding a condition is one member here and one case in
    /// <see cref="LightingTuning.For"/> - nothing downstream needs to know it happened,
    /// because everything that lights a scene goes through <see cref="LightingRig.Apply"/>.
    /// </para>
    /// <para>
    /// There is deliberately only one <em>playable</em> condition so far. The interesting
    /// ones - a dusk map, a night operation where a vehicle sees only what its own head-lights
    /// reach - are a level property waiting to happen: a level file would name a mood, the
    /// loader would apply it, and the emissive head-light materials that already exist would
    /// finally be doing something. That is not built here. What is built here is the shape
    /// that makes it a new row rather than a rewrite, which is worth the enum on its own.
    /// </para>
    /// </remarks>
    [Serializable]
    public enum LightingMood
    {
        /// <summary>No condition named, which reads as <see cref="Daylight"/>.</summary>
        None = 0,

        /// <summary>
        /// Open midday sun, which is what every map is played under today.
        /// </summary>
        Daylight = 1,

        /// <summary>
        /// The art preview's stand: a slightly cooler, flatter version of
        /// <see cref="Daylight"/> that exists to photograph a model rather than to play a
        /// match, and so has no fog to hide the far edge of a backdrop that has no far edge.
        /// </summary>
        Studio = 2,
    }
}
