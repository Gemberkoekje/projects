using System;
using UnityEngine;
using IronFlag.Core;

namespace IronFlag.Levels
{
    /// <summary>
    /// One side's bunker: where its roster waits, where its wrecks are repaired, where it
    /// refuels, and the place a stolen flag has to reach.
    /// </summary>
    /// <remarks>
    /// A bunker is map data rather than session data even though it is the only structure
    /// with a job, because every one of those jobs is a <em>place</em>. Moving the bunker in
    /// a level file moves the win condition, the repair bay and the fuel pump together,
    /// which is the point.
    /// </remarks>
    [Serializable]
    public sealed class LevelBunker
    {
        /// <summary>Side this bunker belongs to, by name.</summary>
        [Tooltip("Side this bunker belongs to: Green or Brown.")]
        public string Team = nameof(Core.Team.Green);

        /// <summary>Where it stands.</summary>
        [Tooltip("Where the bunker stands, on the ground plane.")]
        public Vector3 Position = Vector3.zero;

        /// <summary>Which way it faces, in degrees.</summary>
        /// <remarks>
        /// This is which way the lift bay points, so a bunker facing away from the field
        /// deploys its vehicles into its own back wall.
        /// </remarks>
        [Tooltip("Which way the lift bay faces, in degrees.")]
        public float YawDegrees;

        /// <summary>Metres from it a vehicle may be and still be resupplied.</summary>
        [Tooltip("Metres from the bunker a vehicle may be and still be resupplied.")]
        public float SupplyRadius = 12.0f;

        /// <summary>Fraction of a full pool it puts back per second.</summary>
        /// <remarks>
        /// One number for both fuel and ammunition. Home ground is the only place that fills
        /// both, and the only place that will serve the helicopter.
        /// </remarks>
        [Tooltip("Fraction of a full fuel and ammunition pool the bunker puts back per second.")]
        public float SupplyRate = 0.25f;

        /// <summary>The side this bunker belongs to.</summary>
        public Team Side => LevelNames.ToTeam(Team);
    }
}
