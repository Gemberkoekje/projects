using IronFlag.Core;

namespace IronFlag.Combat
{
    /// <summary>
    /// Anything a round can hurt: a vehicle's hull, or a building standing in the way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// M3 gave <see cref="Projectile"/> one kind of target, and every question it asks -
    /// may I hit this, is it already wrecked, take this much off - was asked of
    /// <see cref="VehicleHealth"/> directly. M5 adds a second kind, and the three questions
    /// are the same three, so they are an interface rather than a second branch in the
    /// round: a round that knew about buildings as well as vehicles would need a third
    /// branch for the flag tower and a fourth for whatever M7 puts on the map.
    /// </para>
    /// <para>
    /// <see cref="Team"/> is on here because whose fire counts is the target's question as
    /// much as the shooter's - see <see cref="Teams.IsHostile"/>. Scenery answers
    /// <see cref="Team.None"/>, which is what makes a building fair game for both players
    /// without anybody writing a rule about scenery.
    /// </para>
    /// </remarks>
    public interface IDamageable
    {
        /// <summary>Which side this belongs to; <see cref="Team.None"/> for map furniture.</summary>
        Team Team { get; }

        /// <summary>Whether this has already been shot to pieces.</summary>
        bool IsDestroyed { get; }

        /// <summary>
        /// Takes a hit.
        /// </summary>
        /// <param name="amount">Hit points to remove; zero or less does nothing.</param>
        /// <param name="from">Side the shot came from.</param>
        /// <returns>The hit points actually lost, which is zero for a shot that cannot hurt it.</returns>
        float TakeDamage(float amount, Team from);
    }
}
