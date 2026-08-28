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
    /// Which of a side's towers is marked real here is authored, not rolled - but it no
    /// longer decides which tower a match is actually played on. It is what the level
    /// editor shows and edits, and it is where <see cref="LevelBuilder"/> puts the one flag
    /// it builds per side; <see cref="IronFlag.Objective.FlagTower.Roll"/> then rerolls the
    /// choice at random, once per side, the instant a real match begins. A raider who has
    /// already played a map has already learned which pyramid to shell, and a decoy that
    /// never moves stops being tested after the first raid - see that method's remarks.
    /// </para>
    /// <para>
    /// A side with two real towers, or none, is a broken level rather than an interesting
    /// one; <see cref="LevelValidation"/> says so. The rule survives the reroll above
    /// because the editor, the level list and the still all build straight off the file and
    /// never see a match begin - they still need one definite answer to show.
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
