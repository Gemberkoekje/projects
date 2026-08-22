namespace IronFlag.Players
{
    /// <summary>
    /// The names the control schemes go by inside the actions asset.
    /// </summary>
    /// <remarks>
    /// The Input System filters a player's bindings with a binding-group mask, and the group
    /// is a string that has to match the asset exactly. Getting it wrong binds nothing and
    /// reports nothing, so the two spellings live here and <c>ControlAssetTests</c> checks
    /// them against the asset.
    /// </remarks>
    public static class ControlSchemes
    {
        /// <summary>Binding group for the keyboard-and-pointer scheme.</summary>
        public const string KeyboardAndMouse = "Keyboard&Mouse";

        /// <summary>Binding group for the gamepad scheme.</summary>
        public const string Gamepad = "Gamepad";

        /// <summary>
        /// Returns the binding group a scheme filters on.
        /// </summary>
        /// <param name="scheme">Scheme to name.</param>
        /// <returns>The binding group, or an empty string when the player has no controls.</returns>
        public static string GroupFor(ControlScheme scheme)
        {
            switch (scheme)
            {
                case ControlScheme.KeyboardAndMouse:
                    return KeyboardAndMouse;
                case ControlScheme.Gamepad:
                    return Gamepad;
                default:
                    return string.Empty;
            }
        }
    }
}
