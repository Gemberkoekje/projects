using System;
using UnityEngine;
using IronFlag.Core;

namespace IronFlag.Levels
{
    /// <summary>
    /// One flag tower, real or decoy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which of a side's towers is real is written here rather than rolled at load, because
    /// the design document puts the decoy under map design. A tower that moved between
    /// matches could not be learned, defended or planned around - and, now that levels are
    /// files, "where is it on this map" is a thing a second level can answer differently.
    /// </para>
    /// <para>
    /// A side with two real towers, or none, is a broken level rather than an interesting
    /// one; <see cref="LevelValidation"/> says so.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class LevelTower
    {
        /// <summary>Side whose flag this tower is for, by name.</summary>
        [Tooltip("Side whose flag this tower is for: Green or Brown.")]
        public string Team = nameof(Core.Team.Green);

        /// <summary>Whether this is the one that actually flies the flag.</summary>
        [Tooltip("Whether this is the real tower rather than the decoy.")]
        public bool HoldsTheFlag;

        /// <summary>Where it stands.</summary>
        [Tooltip("Where the tower stands, on the ground plane.")]
        public Vector3 Position = Vector3.zero;

        /// <summary>Which way it faces, in degrees.</summary>
        [Tooltip("Which way the tower faces, in degrees.")]
        public float YawDegrees;

        /// <summary>The side whose flag this tower is for.</summary>
        public Team Side => LevelNames.ToTeam(Team);
    }
}
