using System;
using UnityEngine;
using IronFlag.Core;

namespace IronFlag.Objective
{
    /// <summary>
    /// The thing a match is won at: who has won, and whether anybody has yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design document's primary win condition - "return the enemy flag to your bunker"
    /// - is the whole of what this component decides, and it decides it by listening rather
    /// than by looking. <see cref="Flag"/> already knows the moment a flag reaches a bunker;
    /// this turns that moment into a result and stops the match.
    /// </para>
    /// <para>
    /// The design document's <em>secondary</em> condition, "destroy all enemy vehicles/base
    /// structures", is deliberately not here. Nothing in the game today can satisfy it: a
    /// side's roster is a fixed four that are always repaired and put back in the bunker,
    /// and the bunker and the flag towers are the two things on the map that cannot be shot
    /// down. Implementing a condition that can never fire would be a rule nobody could tell
    /// was broken.
    /// </para>
    /// <para>
    /// One per scene, found statically the way every other place in this game is - see
    /// <see cref="TeamBunker.For"/>. <see cref="IsFinished"/> is deliberately answerable
    /// without a match in the scene at all, because that is what a test rig and a bare
    /// sandbox are, and "no match object" means "nothing has been won", not "unknown".
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("IronFlag/Match")]
    public sealed class Match : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Side that has won, if anybody has. Not authored; set when a flag comes home.")]
        private Team winner = Team.None;

        [SerializeField]
        [Tooltip("How the match was won. Not authored; set when a flag comes home.")]
        private Team flagTaken = Team.None;

        /// <summary>The match in the current scene, or <c>null</c> when there is none.</summary>
        private static Match current;

        /// <summary>Raised the moment somebody wins.</summary>
        public event Action<Match> Ended;

        /// <summary>The match in the current scene, or <c>null</c> when there is none.</summary>
        public static Match Current => current;

        /// <summary>
        /// Whether the match in this scene has been won.
        /// </summary>
        /// <remarks>
        /// Static and null-safe on purpose: it is asked once a frame by both flags, both
        /// players and both halves of the HUD, and a scene with no match object - which is
        /// what every unit test builds - has to answer <c>false</c> rather than throw.
        /// </remarks>
        public static bool IsFinished => current != null && current.winner != Team.None;

        /// <summary>The side that has won, or <see cref="Team.None"/> while the match runs.</summary>
        public Team Winner => winner;

        /// <summary>Whether this match has been won.</summary>
        public bool IsOver => winner != Team.None;

        /// <summary>
        /// The side whose flag was captured, or <see cref="Team.None"/> while the match runs.
        /// </summary>
        /// <remarks>
        /// Kept alongside the winner because a HUD in front of the losing player wants to
        /// say what happened rather than who it happened to, and with more than two sides
        /// those stop being the same fact.
        /// </remarks>
        public Team FlagTaken => flagTaken;

        /// <summary>
        /// Ends the match with a winner.
        /// </summary>
        /// <param name="side">Side that won.</param>
        /// <param name="stolen">Side whose flag was captured.</param>
        /// <returns><c>true</c> when this call is the one that ended it.</returns>
        /// <remarks>
        /// The first result stands. Two flags arriving home on the same frame is a draw
        /// nobody has designed, and the game the players just watched had one jeep reach a
        /// bunker before the other.
        /// </remarks>
        public bool Win(Team side, Team stolen)
        {
            if (winner != Team.None || side == Team.None)
            {
                return false;
            }

            winner = side;
            flagTaken = stolen;
            Ended?.Invoke(this);
            return true;
        }

        /// <summary>
        /// Puts the match back to nobody having won.
        /// </summary>
        /// <remarks>
        /// For the tests, and for whatever eventually offers a rematch. Nothing in a match
        /// calls it.
        /// </remarks>
        public void Restart()
        {
            winner = Team.None;
            flagTaken = Team.None;
        }

        private void OnEnable()
        {
            if (current == null)
            {
                current = this;
            }

            Flag.AnyCaptured += OnCaptured;
        }

        private void OnDisable()
        {
            Flag.AnyCaptured -= OnCaptured;

            if (current == this)
            {
                current = null;
            }
        }

        /// <summary>
        /// Turns a delivered flag into a result.
        /// </summary>
        /// <param name="flag">The flag that reached a bunker.</param>
        private void OnCaptured(Flag flag)
        {
            if (flag != null)
            {
                Win(flag.CarriedBy, flag.Team);
            }
        }
    }
}
