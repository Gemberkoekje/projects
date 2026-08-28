using UnityEngine;

namespace IronFlag.Audio
{
    /// <summary>
    /// How loud a thing is: the rule that turns a distance into a volume, and an engine's
    /// speed into a pitch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Distance decides loudness; nothing ever decides direction.</strong> That one
    /// sentence is this game's answer to the split-screen problem the plan's audio section
    /// raises. There is exactly one
    /// <see cref="AudioListener"/> in the whole rig - a hard constraint, enforced by
    /// <c>SandboxWiringTests</c>, because two of them is doubled sound - so true 3D audio
    /// would be positioned correctly for whichever seat holds it and wrong for the other.
    /// </para>
    /// <para>
    /// The half of positional audio that breaks under that constraint is <em>panning</em>:
    /// a shot to seat one's left is to seat two's right, and one pair of speakers cannot say
    /// both. The half that does not break is <em>attenuation</em>. A shell landing in the far
    /// corner of the map is quiet for everybody, whichever seat is looking at it, and that is
    /// true no matter where the listener sits. So every clip is played flat - Unity's
    /// <c>spatialBlend</c> stays at zero, no panning, no doppler - and the volume is computed
    /// here instead, from the distance to the nearest seat's view.
    /// </para>
    /// <para>
    /// Which is why this is a static class of pure functions rather than settings on an
    /// <see cref="AudioSource"/>: the mix is a rule the project owns and can test, not a
    /// rolloff curve drawn on a component nobody can assert against. <see cref="AudioDirector"/>
    /// measures the distances; everything about how that distance sounds is here.
    /// </para>
    /// </remarks>
    public static class AudioMixdown
    {
        /// <summary>Metres within which a sound plays at its full rendered level.</summary>
        /// <remarks>
        /// A seat's camera sits 34 metres back at a 58 degree tilt, so it takes in roughly
        /// this much ground either side of what it is following. Inside that circle a sound
        /// is happening on your screen, and things happening on your screen are not quiet.
        /// </remarks>
        public const float FullWithin = 24.0f;

        /// <summary>Metres beyond which a sound is not played at all.</summary>
        /// <remarks>
        /// Under half the width of a 240-metre map, so a firefight at the other end is
        /// silent rather than a layer of mush under the one you are in - but a fight just off
        /// the edge of your view is still audible, which is the warning the number exists for.
        /// </remarks>
        public const float SilentBeyond = 110.0f;

        /// <summary>How loud an idling engine is against one at full throttle, in 0..1.</summary>
        /// <remarks>
        /// A quarter. Enough that a vehicle sitting still on the field is present rather than
        /// switched off, quiet enough that pulling away is audibly pulling away. Stationary
        /// vehicles are not silent because they are quiet - they are silent because
        /// <see cref="EngineAudio"/> will not sound one that is still in its bunker.
        /// </remarks>
        public const float EngineIdleShare = 0.25f;

        /// <summary>Pitch an engine loop plays at when the vehicle is stopped.</summary>
        public const float EngineLowPitch = 0.78f;

        /// <summary>Pitch an engine loop plays at when the vehicle is flat out.</summary>
        public const float EngineHighPitch = 1.24f;

        /// <summary>
        /// Returns how loud something is, given how far it is from the nearest view.
        /// </summary>
        /// <param name="metres">Distance from the closest seat's focus, in metres.</param>
        /// <returns>A volume scale in 0..1: one up close, zero past earshot.</returns>
        /// <remarks>
        /// Flat out to <see cref="FullWithin"/> and then squared down to nothing at
        /// <see cref="SilentBeyond"/>. Squared rather than straight so the drop is gentle
        /// where the action is and steep where it is not: a linear ramp makes the far half
        /// of the map audible at a third of full volume, which is exactly the mush this is
        /// meant to prevent.
        /// </remarks>
        /// <example>
        /// <code>
        /// AudioMixdown.Loudness(0.0f);    // 1
        /// AudioMixdown.Loudness(24.0f);   // 1  - still inside the view
        /// AudioMixdown.Loudness(110.0f);  // 0  - off the far end of the map
        /// </code>
        /// </example>
        public static float Loudness(float metres)
        {
            if (metres <= FullWithin)
            {
                return 1.0f;
            }

            if (metres >= SilentBeyond)
            {
                return 0.0f;
            }

            float past = (metres - FullWithin) / (SilentBeyond - FullWithin);
            float left = 1.0f - past;
            return left * left;
        }

        /// <summary>
        /// Returns how loud an engine is running.
        /// </summary>
        /// <param name="speed">How fast the vehicle is going, in metres per second.</param>
        /// <param name="topSpeed">The vehicle's top speed, in metres per second.</param>
        /// <param name="throttle">How hard the driver is on the pedal, in -1..1.</param>
        /// <param name="idleShare">How loud this engine is with the throttle shut, in 0..1.</param>
        /// <returns>A volume scale in 0..1.</returns>
        /// <remarks>
        /// <para>
        /// Speed and throttle both, taking whichever is larger. Speed alone would leave a
        /// tank straining against a wall silent, which is the one moment a driver most needs
        /// to hear that the engine is doing something; throttle alone would cut out the
        /// moment a jeep crested a hill and coasted.
        /// </para>
        /// <para>
        /// <paramref name="idleShare"/> is a number per vehicle rather than a constant
        /// because the helicopter is not an engine, it is a rotor: it is at full song the
        /// whole time it is in the air, and flying faster barely changes that. Giving it a
        /// high floor is how one component covers both without asking what it is bolted to -
        /// the same trick <see cref="IronFlag.Combat.VehicleWeapon"/> plays with its muzzle.
        /// </para>
        /// </remarks>
        public static float EngineLoudness(
            float speed, float topSpeed, float throttle, float idleShare)
        {
            float floor = Mathf.Clamp01(idleShare);
            float pace = topSpeed <= 0.0f ? 0.0f : Mathf.Clamp01(Mathf.Abs(speed) / topSpeed);
            float effort = Mathf.Max(pace, Mathf.Clamp01(Mathf.Abs(throttle)));
            return floor + ((1.0f - floor) * effort);
        }

        /// <summary>
        /// Returns the pitch an engine loop plays at.
        /// </summary>
        /// <param name="speed">How fast the vehicle is going, in metres per second.</param>
        /// <param name="topSpeed">The vehicle's top speed, in metres per second.</param>
        /// <returns>A pitch multiplier between <see cref="EngineLowPitch"/> and <see cref="EngineHighPitch"/>.</returns>
        /// <remarks>
        /// Speed alone, unlike <see cref="EngineLoudness"/>. Pitch is what the wheels are
        /// doing and volume is what the engine is doing, so a vehicle shoving against a wall
        /// should be loud and low rather than loud and screaming.
        /// </remarks>
        public static float EnginePitch(float speed, float topSpeed)
        {
            float pace = topSpeed <= 0.0f ? 0.0f : Mathf.Clamp01(Mathf.Abs(speed) / topSpeed);
            return Mathf.Lerp(EngineLowPitch, EngineHighPitch, pace);
        }
    }
}
