using System;

namespace IronFlag.Objective
{
    /// <summary>
    /// The two ways a match can end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design document's two win conditions, and there is no third. One side wins by
    /// doing the thing the game is about; the other way is the same sentence read backwards,
    /// where a side has run out of the one vehicle that could ever have done it.
    /// </para>
    /// <para>
    /// Kept alongside the winner rather than derived from the state of the map afterwards,
    /// because the map cannot answer it: a flag standing back on its own tower looks the
    /// same whether it was never taken or was taken and delivered.
    /// </para>
    /// </remarks>
    [Serializable]
    public enum MatchOutcome
    {
        /// <summary>Nothing has been decided: the match is still being played.</summary>
        None = 0,

        /// <summary>A jeep drove the enemy flag home.</summary>
        FlagCaptured = 1,

        /// <summary>
        /// A side lost its last jeep, so it can no longer take a flag anywhere.
        /// </summary>
        /// <remarks>
        /// Named after the jeep because that is what the player sees and what the panel
        /// says. The rule behind it is <see cref="FlagRules.CanCarry"/> - if a second
        /// vehicle is ever allowed to carry a flag, this name and the one sentence the HUD
        /// prints for it are the two things that then say something untrue.
        /// </remarks>
        OutOfJeeps = 2,
    }
}
