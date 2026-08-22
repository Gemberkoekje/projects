using IronFlag.Vehicles;

namespace IronFlag.Objective
{
    /// <summary>
    /// The flag's whole rulebook: who may carry it, how close you have to be to anything,
    /// and how long a dropped one stays where it fell.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="CanCarry"/> is the design document's first pillar - "only the jeep carries
    /// the flag. Everything else exists to clear its path or stop the enemy's" - and it is
    /// one comparison in one place for the same reason <see cref="IronFlag.Core.Teams"/> is:
    /// three separate pieces of code have to agree on it. The flag decides whether to jump
    /// onto a vehicle, the HUD decides whether to tell the player why it did not, and the
    /// roster table in the design document is written as though both already do.
    /// </para>
    /// <para>
    /// The numbers are here rather than in a per-kind table like
    /// <see cref="IronFlag.Destruction.StructureTuning"/> because there is exactly one flag
    /// in the game and a table with one row is a table nobody can balance. They are still
    /// meant to be read against each other, and against the map.
    /// </para>
    /// <para>
    /// What is <em>not</em> here any more is what it costs to find a flag. That used to be a
    /// radius you drove inside; it is now a tower's hit points, in
    /// <see cref="IronFlag.Destruction.StructureTuning.For"/>, because finding a flag means
    /// breaking the tower open. See <see cref="FlagTower"/>.
    /// </para>
    /// </remarks>
    public static class FlagRules
    {
        /// <summary>
        /// Metres across the map a jeep may be from a flag and still pick it up.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A jeep is four metres long, so this is "drive into it" rather than "drive near
        /// it". A generous radius would let a jeep clip the corner of a tower at full speed
        /// and leave with the flag, which removes the one moment in the raid where the
        /// defender has a shot at a stationary target.
        /// </para>
        /// <para>
        /// Measured from whatever the flag is standing in rather than always from the flag:
        /// on a tower it is the distance to the <em>walls</em>, because the flag is sealed
        /// behind them - see <see cref="FlagTower.DistanceFrom"/>. Measured to the flag
        /// itself, this radius would be smaller than the tower is wide, and whether a jeep
        /// could reach it would depend on which way round the jeep happened to be pointing.
        /// </para>
        /// </remarks>
        public const float PickupRadius = 4.0f;

        /// <summary>
        /// Seconds a dropped flag lies where it fell before going back to its tower.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The defender's only way to get a stolen flag back, and it is a clock rather than
        /// a touch: a rule where driving over your own flag returns it instantly would make
        /// killing the carrier and killing the carrier <em>and then holding the ground</em>
        /// the same play, and the second one is the interesting one.
        /// </para>
        /// <para>
        /// Twelve is read against what it costs the raiding side to try again: a
        /// four-second repair on the jeep they just lost, plus the ride out of the bunker.
        /// The countdown is deliberately longer than that, so killing the carrier never wins
        /// the flag back on its own - it buys the defender time and a fight over the spot
        /// the flag is lying on, which is a position worth holding rather than a reward for
        /// a kill.
        /// </para>
        /// <para>
        /// On a map with a channel across it, twelve seconds is a distance rather than a
        /// formality: where a flag is dropped decides who can reach it. It is now also read
        /// against a tower that stays open - a raider who loses the flag on the way home
        /// does not have to buy their way back into the tower a second time.
        /// </para>
        /// </remarks>
        public const float ReturnSeconds = 12.0f;

        /// <summary>
        /// Metres above a carrier's own origin that the flag rides at.
        /// </summary>
        /// <remarks>
        /// Above the jeep's roll cage, so the whole map can see who is carrying. A flag
        /// hidden inside the silhouette of the vehicle holding it would undo the reason the
        /// jeep is the only carrier: everybody is supposed to know where it is.
        /// </remarks>
        public const float MastHeight = 1.4f;

        /// <summary>
        /// Metres above a tower's own origin that the flag stands at.
        /// </summary>
        /// <remarks>
        /// A fallback, and only a fallback. Where a flag stands on a tower is read off the
        /// <c>FlagMount</c> object inside whichever destruction state the tower is showing -
        /// see <see cref="FlagTower.FlagPoint"/> - because a tower that has been broken open
        /// carries its flag lower than one that has not. This is what a tower rebuilt from
        /// an older <c>.glb</c>, with no mount in it, falls back to: the height of the intact
        /// tower's pole.
        /// </remarks>
        public const float TowerFlagHeight = 5.2f;

        /// <summary>
        /// Reports whether one kind of vehicle is allowed to carry a flag.
        /// </summary>
        /// <param name="kind">Vehicle asking.</param>
        /// <returns><c>true</c> only for the jeep.</returns>
        /// <remarks>
        /// The whole game is arranged around this returning <c>false</c> three times out of
        /// four. It is a method rather than a comparison written out at each call site
        /// because the interesting version of this rule - a damaged jeep that cannot carry,
        /// or a second carrier unlocked by something - is a change to one line here.
        /// </remarks>
        public static bool CanCarry(VehicleKind kind) => kind == VehicleKind.Jeep;
    }
}
