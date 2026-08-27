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
    /// The design document's <em>secondary</em> condition, "destroy all enemy vehicles",
    /// is here now, in the only form that decides anything: a side that has lost its last
    /// jeep has lost the ability to carry a flag home, so the match is over and the other
    /// side has won. That is <see cref="TeamReserve.IsBeaten"/>, and it arrives here the
    /// same way a capture does - as an event, from something that was already watching.
    /// It could not exist before M6 because a side's roster was a fixed four that were
    /// always repaired and put back in the bunker; a level now says how many there are.
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
        [Tooltip("Side it was won against. Not authored; set when the match ends.")]
        private Team beaten = Team.None;

        [SerializeField]
        [Tooltip("How the match was won. Not authored; set when the match ends.")]
        private MatchOutcome outcome = MatchOutcome.None;

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
        /// The side it was won against, or <see cref="Team.None"/> while the match runs.
        /// </summary>
        /// <remarks>
        /// Kept alongside the winner because a HUD in front of the losing player wants to
        /// say what happened rather than who it happened to, and with more than two sides
        /// those stop being the same fact. Which of the two things happened to them is
        /// <see cref="Outcome"/>: their flag was driven away, or they ran out of jeeps.
        /// </remarks>
        public Team Beaten => beaten;

        /// <summary>How the match was won, or <see cref="MatchOutcome.None"/> while it runs.</summary>
        public MatchOutcome Outcome => outcome;

        /// <summary>
        /// Ends the match with a winner.
        /// </summary>
        /// <param name="side">Side that won.</param>
        /// <param name="loser">Side it was won against.</param>
        /// <param name="how">Which of the two endings this is.</param>
        /// <returns><c>true</c> when this call is the one that ended it.</returns>
        /// <remarks>
        /// The first result stands. Two flags arriving home on the same frame is a draw
        /// nobody has designed, and the game the players just watched had one jeep reach a
        /// bunker before the other. A win with no ending named is refused rather than
        /// recorded blank, because the panel that reports it has nothing to say about one.
        /// </remarks>
        public bool Win(Team side, Team loser, MatchOutcome how)
        {
            if (winner != Team.None || side == Team.None || how == MatchOutcome.None)
            {
                return false;
            }

            winner = side;
            beaten = loser;
            outcome = how;
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
            beaten = Team.None;
            outcome = MatchOutcome.None;
        }

        private void OnEnable()
        {
            if (current == null)
            {
                current = this;
            }

            Flag.AnyCaptured += OnCaptured;
            TeamReserve.AnyBeaten += OnRunOut;
        }

        private void OnDisable()
        {
            Flag.AnyCaptured -= OnCaptured;
            TeamReserve.AnyBeaten -= OnRunOut;

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
                Win(flag.CarriedBy, flag.Team, MatchOutcome.FlagCaptured);
            }
        }

        /// <summary>
        /// Turns a side running out of jeeps into a result for the other one.
        /// </summary>
        /// <param name="reserve">The reserve that has just lost its last carrier.</param>
        /// <remarks>
        /// The winner is worked out here rather than announced by the reserve, because
        /// "who wins when this side cannot" is a question about the match rather than about
        /// a stock of vehicles - and it is a question that stops having one answer as soon
        /// as there are three sides. <see cref="Teams.OpponentOf"/> says so by answering
        /// <see cref="Team.None"/>, and a match with nobody to award it to keeps running.
        /// </remarks>
        private void OnRunOut(TeamReserve reserve)
        {
            if (reserve == null)
            {
                return;
            }

            Win(Teams.OpponentOf(reserve.Team), reserve.Team, MatchOutcome.OutOfJeeps);
        }
    }
}
