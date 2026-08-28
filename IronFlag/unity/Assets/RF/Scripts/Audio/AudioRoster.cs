using System;
using System.Collections.Generic;
using IronFlag.Combat;
using IronFlag.Destruction;
using IronFlag.Objective;
using IronFlag.Vehicles;

namespace IronFlag.Audio
{
    /// <summary>
    /// What is in the sound library, what each clip is called on disk, and which one a
    /// weapon, a vehicle or an event asks for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart of <see cref="WeaponTuning"/> and <see cref="StructureTuning"/>: a
    /// static table with no state and no scene behind it, so what the game is allowed to
    /// play can be checked without playing any of it.
    /// </para>
    /// <para>
    /// <strong>The asset names are computed, not listed.</strong> A clip is its enum value
    /// behind a prefix, which is the whole reason the pipeline validates the
    /// <c>RF_Sfx_</c> and <c>RF_Music_</c> naming rule before it renders anything: a recipe
    /// named wrongly fails the audio build instead of surfacing here as a catalog row that
    /// is quietly empty. A hand-written table would have been a third place for the same
    /// name to be spelled, and the two spellings that matter are already the recipe's and
    /// the enum's.
    /// </para>
    /// </remarks>
    public static class AudioRoster
    {
        /// <summary>What every sound effect's file name starts with.</summary>
        public const string SfxPrefix = "RF_Sfx_";

        /// <summary>What every music file's name starts with.</summary>
        public const string MusicPrefix = "RF_Music_";

        /// <summary>
        /// Returns the file name, without extension, one sound is rendered to.
        /// </summary>
        /// <param name="kind">Sound to name.</param>
        /// <returns>The asset name, or an empty string for <see cref="SfxKind.None"/>.</returns>
        /// <example>
        /// <code>
        /// AudioRoster.AssetNameOf(SfxKind.WeaponCannon); // "RF_Sfx_WeaponCannon"
        /// </code>
        /// </example>
        public static string AssetNameOf(SfxKind kind)
            => kind == SfxKind.None ? string.Empty : SfxPrefix + kind;

        /// <summary>
        /// Returns the file name, without extension, one piece of music is rendered to.
        /// </summary>
        /// <param name="kind">Music to name.</param>
        /// <returns>The asset name, or an empty string for <see cref="MusicKind.None"/>.</returns>
        public static string AssetNameOf(MusicKind kind)
            => kind == MusicKind.None ? string.Empty : MusicPrefix + kind;

        /// <summary>
        /// Lists every sound the game has, in declaration order.
        /// </summary>
        /// <returns>Every <see cref="SfxKind"/> except <see cref="SfxKind.None"/>.</returns>
        public static List<SfxKind> Sounds()
        {
            var found = new List<SfxKind>();
            foreach (SfxKind kind in (SfxKind[])Enum.GetValues(typeof(SfxKind)))
            {
                if (kind != SfxKind.None)
                {
                    found.Add(kind);
                }
            }

            return found;
        }

        /// <summary>
        /// Lists every piece of music the game has, in declaration order.
        /// </summary>
        /// <returns>Every <see cref="MusicKind"/> except <see cref="MusicKind.None"/>.</returns>
        public static List<MusicKind> Themes()
        {
            var found = new List<MusicKind>();
            foreach (MusicKind kind in (MusicKind[])Enum.GetValues(typeof(MusicKind)))
            {
                if (kind != MusicKind.None)
                {
                    found.Add(kind);
                }
            }

            return found;
        }

        /// <summary>
        /// Returns the noise one weapon makes when it fires.
        /// </summary>
        /// <param name="kind">The weapon.</param>
        /// <returns>Its shot, or <see cref="SfxKind.None"/> for a mount with no gun.</returns>
        /// <remarks>
        /// One per <see cref="WeaponKind"/> and no fallback, deliberately. A weapon added to
        /// the table without a sound should be a silent gun that a roster test names, not a
        /// gun that borrows the cannon's voice and sounds subtly wrong forever.
        /// </remarks>
        public static SfxKind ShotOf(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Grenade:
                    return SfxKind.WeaponGrenade;
                case WeaponKind.Cannon:
                    return SfxKind.WeaponCannon;
                case WeaponKind.Rocket:
                    return SfxKind.WeaponRocket;
                case WeaponKind.Chaingun:
                    return SfxKind.WeaponChaingun;
                case WeaponKind.Autocannon:
                    return SfxKind.WeaponAutocannon;
                default:
                    return SfxKind.None;
            }
        }

        /// <summary>
        /// Returns the loop one vehicle's engine runs on.
        /// </summary>
        /// <param name="kind">The vehicle.</param>
        /// <returns>Its engine loop, or <see cref="SfxKind.None"/> for no vehicle.</returns>
        public static SfxKind EngineOf(VehicleKind kind)
        {
            switch (kind)
            {
                case VehicleKind.Jeep:
                    return SfxKind.EngineJeep;
                case VehicleKind.Tank:
                    return SfxKind.EngineTank;
                case VehicleKind.Asv:
                    return SfxKind.EngineAsv;
                case VehicleKind.Helicopter:
                    return SfxKind.EngineHelicopter;
                default:
                    return SfxKind.None;
            }
        }

        /// <summary>
        /// Returns the theme that plays while one vehicle is being driven.
        /// </summary>
        /// <param name="kind">The vehicle.</param>
        /// <returns>Its theme, or <see cref="MusicKind.None"/> for no vehicle.</returns>
        public static MusicKind ThemeOf(VehicleKind kind)
        {
            switch (kind)
            {
                case VehicleKind.Jeep:
                    return MusicKind.MatchJeep;
                case VehicleKind.Tank:
                    return MusicKind.MatchTank;
                case VehicleKind.Asv:
                    return MusicKind.MatchAsv;
                case VehicleKind.Helicopter:
                    return MusicKind.MatchHelicopter;
                default:
                    return MusicKind.None;
            }
        }

        /// <summary>
        /// Returns the sound one structure makes on arriving in a state.
        /// </summary>
        /// <param name="state">The state it has just entered.</param>
        /// <returns>
        /// The sound, or <see cref="SfxKind.None"/> for a state nothing is heard on.
        /// </returns>
        /// <remarks>
        /// <see cref="DestructionState.Intact"/> is silent on purpose. A structure only ever
        /// arrives there by being built or by being put back up in the editor, and neither is
        /// an event anybody in a match is present for.
        /// </remarks>
        public static SfxKind DamageOf(DestructionState state)
        {
            switch (state)
            {
                case DestructionState.Damaged:
                    return SfxKind.StructureDamaged;
                case DestructionState.Destroyed:
                    return SfxKind.StructureDestroyed;
                default:
                    return SfxKind.None;
            }
        }

        /// <summary>
        /// Returns the sound a flag makes on arriving in a state.
        /// </summary>
        /// <param name="state">The state it has just entered.</param>
        /// <returns>The sound, or <see cref="SfxKind.None"/> when it arrived quietly.</returns>
        public static SfxKind FlagOf(FlagState state)
        {
            switch (state)
            {
                case FlagState.Carried:
                    return SfxKind.FlagPickup;
                case FlagState.Dropped:
                    return SfxKind.FlagDropped;
                case FlagState.AtTower:
                    return SfxKind.FlagReturned;
                case FlagState.Captured:
                    return SfxKind.FlagCaptured;
                default:
                    return SfxKind.None;
            }
        }

        /// <summary>
        /// Whether one piece of music is a theme somebody drives to.
        /// </summary>
        /// <param name="kind">Music to ask about.</param>
        /// <returns><c>true</c> for one of the four match themes.</returns>
        /// <remarks>
        /// The menu bed and the two end cues are not themes: they belong to a moment rather
        /// than to a vehicle. <see cref="MatchMusic"/> holds the last theme when nobody is on
        /// the field, and this is what stops it holding a victory fanfare into the rematch.
        /// </remarks>
        public static bool IsMatchTheme(MusicKind kind)
        {
            switch (kind)
            {
                case MusicKind.MatchJeep:
                case MusicKind.MatchTank:
                case MusicKind.MatchAsv:
                case MusicKind.MatchHelicopter:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether one piece of music is a bed that runs until something stops it.
        /// </summary>
        /// <param name="kind">Music to ask about.</param>
        /// <returns><c>true</c> for a loop; <c>false</c> for a cue that ends.</returns>
        /// <remarks>
        /// The end cues are the exception: a match is over, and a victory fanfare that came
        /// round again every twelve seconds would turn the moment the game exists for into a
        /// jingle nobody can leave. Everything else loops, and is rendered to loop cleanly -
        /// see the arithmetic note in <c>audio/sounds/music.scd</c>.
        /// </remarks>
        public static bool Loops(MusicKind kind)
            => kind != MusicKind.None && kind != MusicKind.Victory && kind != MusicKind.Defeat;
    }
}
