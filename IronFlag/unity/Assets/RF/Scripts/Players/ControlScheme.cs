using System;

namespace IronFlag.Players
{
    /// <summary>
    /// The sets of controls a local player can be holding.
    /// </summary>
    /// <remarks>
    /// These match the control schemes in the actions asset by name - see
    /// <see cref="ControlSchemes"/> - because a scheme is what the Input System filters
    /// bindings by. Everything above the input layer branches on this enum instead of on
    /// device types, which is what keeps "aim with the pointer" from meaning "look for a
    /// mouse".
    /// </remarks>
    [Serializable]
    public enum ControlScheme
    {
        /// <summary>No controls: this player has no device and their vehicle idles.</summary>
        None = 0,

        /// <summary>Keyboard to drive, pointer to aim. One player at a time can hold this.</summary>
        KeyboardAndMouse = 1,

        /// <summary>One gamepad. Every player after the first is on one of these.</summary>
        Gamepad = 2,
    }
}
