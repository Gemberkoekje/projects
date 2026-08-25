using IronFlag.Core;

namespace IronFlag.Editor.ArtPipeline
{
    /// <summary>
    /// Lights the open scene for one condition, using the generated sky that goes with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one line every scene builder here calls, and the only place that knows a
    /// <see cref="LightingMood"/> has a sky material named after it. Splitting it out of the
    /// sandbox builder is what stops the level editor, the map overview and the art preview
    /// from each having to reach into a gameplay tool to be lit.
    /// </para>
    /// <para>
    /// Editor-side because the sky it hands over comes out of the asset database. Everything
    /// that is actually about lighting lives in <see cref="LightingRig"/>, in the runtime
    /// assembly, so a level that one day names its own condition - a dusk map, a night
    /// operation - can be lit as it loads without any of this.
    /// </para>
    /// </remarks>
    internal static class SceneLighting
    {
        /// <summary>
        /// Lights the open scene for a condition, exactly as its table describes it.
        /// </summary>
        /// <param name="mood">The condition to light for.</param>
        public static void Apply(LightingMood mood) => Apply(mood, LightingTuning.For(mood));

        /// <summary>
        /// Lights the open scene for a condition, with the table stamped and edited.
        /// </summary>
        /// <param name="mood">The condition whose sky to hang.</param>
        /// <param name="lighting">
        /// The lighting to apply, usually <see cref="LightingTuning.For"/> for the same mood
        /// with one value changed. The two overhead views drop the fog this way.
        /// </param>
        public static void Apply(LightingMood mood, LightingTuning lighting)
            => LightingRig.Apply(lighting, GeneratedMaterials.LoadSky(mood));
    }
}
