using System;
using System.Collections.Generic;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Vehicles;

namespace IronFlag.Objective
{
    /// <summary>
    /// How many vehicles one side has left, and the moment it has no way left to win.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design document's <em>secondary</em> win condition - "destroy all enemy
    /// vehicles" - was deferred at M6 with a reason: it needs either a finite vehicle
    /// roster or a destructible bunker, and v0.1 had neither. This is the finite roster.
    /// A side starts with a stock of each vehicle from its level file, a wreck comes off
    /// that stock, and nothing puts one back.
    /// </para>
    /// <para>
    /// <strong>Losing is losing the jeeps, not losing everything.</strong> Only a jeep can
    /// carry a flag - <see cref="FlagRules.CanCarry"/> - so a side with three tanks and no
    /// jeeps left cannot reach the one ending that is a win, however long it drives around.
    /// Playing that out would be a match that has already been decided and has not been
    /// told, so the match ends the moment the last carrier is gone and the other side wins.
    /// That is <see cref="IsBeaten"/>, and it is asked through
    /// <see cref="FlagRules.CanCarry"/> rather than against <see cref="VehicleKind.Jeep"/>
    /// so that the day a second vehicle may carry a flag, this rule already knows.
    /// </para>
    /// <para>
    /// It counts by listening to <see cref="VehicleHealth.AnyDestroyed"/> rather than by
    /// being told by the bunker, so that <em>every</em> way of losing a vehicle costs the
    /// same one: shot, drowned, or blown up by its own pilot a long way from home.
    /// Scuttling used to be free - the design document calls it "a genuine tension
    /// mechanic worth keeping" - and it is a real decision again now that it is the same
    /// price as dying.
    /// </para>
    /// <para>
    /// One per side, found statically the way <see cref="TeamBunker.For"/> and
    /// <see cref="Flag.Of"/> are, and built onto that side's bunker by
    /// <see cref="IronFlag.Levels.LevelBuilder"/> because a bunker is where a side's
    /// vehicles are. A scene with none - which is what most tests are - is a scene with no
    /// limit, and everything that asks reads a missing reserve as "as many as you like"
    /// rather than as "none left".
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("IronFlag/Team Reserve")]
    public sealed class TeamReserve : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Which side these vehicles belong to.")]
        private Team team = Team.None;

        [SerializeField]
        [Tooltip("Vehicles left, one slot per VehicleKind. Written by the level builder.")]
        private List<int> remaining = new List<int>();

        /// <summary>Every reserve currently in the scene, in the order they woke up.</summary>
        private static readonly List<TeamReserve> Live = new List<TeamReserve>();

        /// <summary>Raised the moment a side loses the last vehicle that could win it.</summary>
        /// <remarks>
        /// Static for the same reason <see cref="Flag.AnyCaptured"/> is: the thing that
        /// cares is <see cref="Match"/>, which belongs to the session and never meets the
        /// map these are built onto. A subscriber has to survive a level being reloaded
        /// underneath it, and a static event is that wiring with the ordering taken out.
        /// </remarks>
        public static event Action<TeamReserve> AnyBeaten;

        /// <summary>Which side these vehicles belong to.</summary>
        public Team Team => team;

        /// <summary>
        /// Whether this side has run out of vehicles that could carry a flag home.
        /// </summary>
        /// <remarks>
        /// The whole loss condition, in one line. Nothing else about a side being in
        /// trouble - no tanks left, no fuel, no towers standing - is a defeat, because none
        /// of those stop it winning.
        /// </remarks>
        public bool IsBeaten
        {
            get
            {
                foreach (VehicleKind kind in VehicleRoster.Kinds)
                {
                    if (FlagRules.CanCarry(kind) && Remaining(kind) > 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Returns the reserve one side is drawing on.
        /// </summary>
        /// <param name="side">Side to look up.</param>
        /// <returns>That side's reserve, or <c>null</c> when the scene keeps none.</returns>
        /// <remarks>
        /// Null is a normal answer and it means unlimited. A vehicle assembled in a test has
        /// no bunker and no reserve behind it, and it must still be drivable, because most
        /// of what the tests check has nothing to do with how many are left.
        /// </remarks>
        public static TeamReserve For(Team side)
        {
            foreach (TeamReserve reserve in Live)
            {
                if (reserve != null && reserve.team == side)
                {
                    return reserve;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns how many of one vehicle a side has left.
        /// </summary>
        /// <param name="side">Side to look up.</param>
        /// <param name="kind">Vehicle to count.</param>
        /// <returns>How many are left, or <see cref="int.MaxValue"/> when nothing limits it.</returns>
        /// <remarks>
        /// The form the two callers that must not care about the difference use: a driver
        /// deciding whether a vehicle may leave the bunker, and the panel telling the player
        /// why it may not. Both want one number, and a scene with no reserve in it answers
        /// with a number no smaller than any real stock rather than with a special case each
        /// of them would have to spell out.
        /// </remarks>
        public static int LeftFor(Team side, VehicleKind kind)
        {
            TeamReserve reserve = For(side);
            return reserve == null ? int.MaxValue : reserve.Remaining(kind);
        }

        /// <summary>
        /// Sets which side this reserve belongs to and empties it.
        /// </summary>
        /// <param name="side">Side these vehicles belong to.</param>
        /// <remarks>
        /// Emptied rather than filled, because what fills it is the level - see
        /// <see cref="Give"/>. A reserve nobody has given anything to is a side with no
        /// vehicles at all, which is a mistake worth being visible.
        /// </remarks>
        public void Configure(Team side)
        {
            team = side;
            remaining.Clear();
        }

        /// <summary>
        /// Puts a stock of one vehicle into the reserve.
        /// </summary>
        /// <param name="kind">Vehicle being stocked.</param>
        /// <param name="count">How many; negative is read as none.</param>
        public void Give(VehicleKind kind, int count)
        {
            int slot = (int)kind;
            if (slot < 0)
            {
                return;
            }

            while (remaining.Count <= slot)
            {
                remaining.Add(0);
            }

            remaining[slot] = Mathf.Max(0, count);
        }

        /// <summary>
        /// Returns how many of one vehicle this side has left.
        /// </summary>
        /// <param name="kind">Vehicle to count.</param>
        /// <returns>How many are left, which is none for a vehicle it was never given.</returns>
        public int Remaining(VehicleKind kind)
        {
            int slot = (int)kind;
            return slot < 0 || slot >= remaining.Count ? 0 : remaining[slot];
        }

        /// <summary>Whether this side has any of one vehicle left to send out.</summary>
        /// <param name="kind">Vehicle to ask about.</param>
        /// <returns><c>true</c> when at least one is left.</returns>
        public bool Has(VehicleKind kind) => Remaining(kind) > 0;

        /// <summary>
        /// Takes one vehicle off this side's stock.
        /// </summary>
        /// <param name="kind">Vehicle that was lost.</param>
        /// <returns><c>true</c> when there was one to take.</returns>
        /// <remarks>
        /// Raises <see cref="AnyBeaten"/> when this is the loss that leaves the side with no
        /// carrier - on the change, never on the state, so that a level starting a side with
        /// no jeeps at all is a broken level for
        /// <see cref="IronFlag.Levels.LevelValidation"/> to report rather than a match that
        /// ends on its own first frame.
        /// </remarks>
        public bool Spend(VehicleKind kind)
        {
            if (!Has(kind))
            {
                return false;
            }

            bool couldWin = !IsBeaten;
            remaining[(int)kind]--;

            if (couldWin && IsBeaten)
            {
                AnyBeaten?.Invoke(this);
            }

            return true;
        }

        private void OnEnable()
        {
            Live.Add(this);
            VehicleHealth.AnyDestroyed += OnWrecked;
        }

        private void OnDisable()
        {
            Live.Remove(this);
            VehicleHealth.AnyDestroyed -= OnWrecked;
        }

        /// <summary>
        /// Takes a wreck off the stock it came out of.
        /// </summary>
        /// <param name="wrecked">The vehicle that has just been destroyed.</param>
        /// <remarks>
        /// Every reserve in the scene hears about every wreck and all but one of them
        /// ignores it, which is the same shape as every flag hearing about every capture.
        /// The alternative - a bunker telling its own reserve - would count only the
        /// vehicles that came out of a bunker, and a vehicle is no less destroyed for having
        /// been put on the map by something else.
        /// </remarks>
        private void OnWrecked(VehicleHealth wrecked)
        {
            if (wrecked == null || wrecked.Team != team)
            {
                return;
            }

            var driven = wrecked.GetComponent<VehicleController>();
            if (driven != null)
            {
                Spend(driven.Kind);
            }
        }
    }
}
