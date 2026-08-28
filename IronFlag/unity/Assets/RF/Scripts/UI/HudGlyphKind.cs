namespace IronFlag.UI
{
    /// <summary>
    /// Which of the interface's drawn marks a glyph is.
    /// </summary>
    /// <remarks>
    /// A short list on purpose. Every one of these stands next to a word or a bar that
    /// already says the same thing, so a glyph here earns its place by being the faster half
    /// of that pair at a glance - not by replacing the words. Anything that would need a
    /// caption to be understood is not on this list.
    /// </remarks>
    public enum HudGlyphKind
    {
        /// <summary>No mark at all.</summary>
        None = 0,

        /// <summary>Hit points: a shield.</summary>
        Armour = 1,

        /// <summary>Fuel: a drop.</summary>
        Fuel = 2,

        /// <summary>Ammunition: a round.</summary>
        Rounds = 3,

        /// <summary>Either flag, on the two lines about the objective.</summary>
        Flag = 4,
    }
}
