using System;

namespace IronFlag.Core
{
    /// <summary>
    /// Where one vehicle is in the loop between its bunker and the field.
    /// </summary>
    /// <remarks>
    /// Four states, and only one of them is a vehicle anybody can drive. The order is the
    /// order they happen in: a vehicle waits in the bunker, is chosen, rides the lift out,
    /// and eventually comes back to be repaired.
    /// </remarks>
    [Serializable]
    public enum VehicleBayState
    {
        /// <summary>Not in the loop at all: a vehicle nobody has stowed, so it just drives.</summary>
        None = 0,

        /// <summary>In the bunker, repaired, fuelled and available to be picked.</summary>
        Ready = 1,

        /// <summary>In the bunker being put back together after being wrecked.</summary>
        Repairing = 2,

        /// <summary>On the lift or the pad, on its way out, not yet drivable.</summary>
        Deploying = 3,

        /// <summary>Out on the field with somebody at the controls.</summary>
        OnField = 4,
    }
}
