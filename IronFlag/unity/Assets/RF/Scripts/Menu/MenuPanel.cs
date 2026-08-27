namespace IronFlag.Menu
{
    /// <summary>
    /// Which of the menu's three screens is showing.
    /// </summary>
    /// <remarks>
    /// One at a time and always exactly one, which is why this is an enum rather than three
    /// booleans: a menu that can be in two states at once is a menu with a map list drawn over
    /// its settings, and the bug only appears on the second visit.
    /// </remarks>
    public enum MenuPanel
    {
        /// <summary>No screen at all, which is the menu before it has been built.</summary>
        None = 0,

        /// <summary>The four things the game can be asked to do.</summary>
        Root = 1,

        /// <summary>The list of maps to play.</summary>
        Levels = 2,

        /// <summary>The window and the quality tier.</summary>
        Settings = 3,
    }
}
