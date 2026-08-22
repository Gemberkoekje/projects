namespace IronFlag.Core
{
    /// <summary>
    /// The one rule that decides whether a shot counts.
    /// </summary>
    /// <remarks>
    /// It is a single comparison, and it is worth its own file because two very different
    /// pieces of code have to agree on it: a round decides whether to stop on what it hit,
    /// and a vehicle decides whether to lose hit points for it. Answering it in two places
    /// is how a game ends up with rounds that pass through a target and damage it anyway.
    /// </remarks>
    public static class Teams
    {
        /// <summary>
        /// Reports whether one side's fire hurts another's.
        /// </summary>
        /// <param name="shooter">Side the shot came from.</param>
        /// <param name="target">Side being shot at.</param>
        /// <returns><c>true</c> when the two are on different sides.</returns>
        /// <remarks>
        /// <para>
        /// There is no friendly fire. Four vehicles a side share one start line and one
        /// bunker, and a split-screen match in which a player can wreck their own roster by
        /// firing down it is annoying rather than tactical - both players would spend the
        /// first ten seconds of every match not shooting.
        /// </para>
        /// <para>
        /// Anything on no side at all is fair game for everybody, deliberately. A vehicle
        /// that reaches the field without a team is an authoring mistake, and damage that
        /// silently goes nowhere is a much harder mistake to notice than a vehicle that
        /// anyone can shoot.
        /// </para>
        /// </remarks>
        public static bool IsHostile(Team shooter, Team target) => shooter != target;
    }
}
