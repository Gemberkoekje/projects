using UnityEngine;

namespace IronFlag.Levels
{
    /// <summary>
    /// The only moving part of the sea: how many seconds of water have gone by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>RF_Water.shader</c> takes its time from a global rather than from Unity's built-in
    /// <c>_Time</c>, and this is the one thing that writes it. That is not a preference. A
    /// shader reading <c>_Time</c> is a shader whose output depends on how long the editor
    /// has been open, and this project renders stills of the sandbox and of the map from a
    /// batch process and commits them: two renders of an unchanged map have to be the same
    /// image, or every unrelated change comes with a picture of the sea in it. Nothing drives
    /// this outside play mode, so a still is always a flat calm.
    /// </para>
    /// <para>
    /// Accumulated from <see cref="Time.deltaTime"/> rather than read off
    /// <see cref="Time.time"/>, which is what makes the water stop when
    /// <see cref="IronFlag.Menu.PauseMenu"/> stops the clock. A frozen match with a sea still
    /// rolling through it is a worse pause than no pause.
    /// </para>
    /// <para>
    /// It does not wrap. The phase is seconds times a wavenumber of about half a radian a
    /// metre, so an hour of play is a couple of thousand radians, which a float still carries
    /// to four decimal places - and a match is minutes.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Water Clock")]
    public sealed class WaterClock : MonoBehaviour
    {
        /// <summary>Name of the global the water shader reads its time from.</summary>
        public const string Global = "_RF_WaterTime";

        private static readonly int Elapsed = Shader.PropertyToID(Global);

        private float seconds;

        /// <summary>Seconds of water this clock has counted.</summary>
        public float Seconds => seconds;

        /// <summary>
        /// Stops the sea and puts it back where it started.
        /// </summary>
        /// <remarks>
        /// Called on the way in and on the way out, because the global outlives the level
        /// that set it: a batch process that plays a scene and then renders a still of a
        /// second one would otherwise photograph the first one's swell.
        /// </remarks>
        public static void Still() => Shader.SetGlobalFloat(Elapsed, 0.0f);

        private void OnEnable()
        {
            seconds = 0.0f;
            Still();
        }

        private void OnDisable() => Still();

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            seconds += Time.deltaTime;
            Shader.SetGlobalFloat(Elapsed, seconds);
        }
    }
}
