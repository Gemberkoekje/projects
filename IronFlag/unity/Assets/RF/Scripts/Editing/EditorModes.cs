namespace IronFlag.Editing
{
    /// <summary>
    /// Which mode a tool belongs to, and what each mode is allowed to touch.
    /// </summary>
    /// <remarks>
    /// The mode is <em>derived</em> from the tool rather than kept in a field next to it,
    /// because two fields that have to agree are two fields that can come to disagree - and
    /// the way that happens here is obvious: any button that sets a tool without also
    /// setting the mode leaves the editor in a mode whose tool it is not holding. Picking a
    /// prop off the palette is exactly such a button. With one field there is nothing to keep
    /// in step: choosing a tool <em>is</em> choosing the mode it lives in.
    /// </remarks>
    public static class EditorModes
    {
        /// <summary>
        /// Returns which mode a tool belongs to.
        /// </summary>
        /// <param name="tool">The tool in hand.</param>
        /// <returns>The mode it works in, or <see cref="EditorMode.None"/> for no tool.</returns>
        /// <remarks>
        /// Every tool belongs to exactly one mode, which is what makes this total. If a tool
        /// ever needs to work in both, it wants two entries in <see cref="EditTool"/> rather
        /// than a mode field here - the mode is what the player is doing, and a tool that
        /// means two things is two tools.
        /// </remarks>
        public static EditorMode Of(EditTool tool)
        {
            switch (tool)
            {
                case EditTool.Land:
                    return EditorMode.Land;
                case EditTool.Select:
                case EditTool.Structure:
                case EditTool.Tower:
                case EditTool.Bunker:
                    return EditorMode.Build;
                default:
                    return EditorMode.None;
            }
        }

        /// <summary>
        /// Returns the tool a mode starts on.
        /// </summary>
        /// <param name="mode">The mode being entered.</param>
        /// <returns>Its opening tool, or <see cref="EditTool.None"/> for no mode.</returns>
        /// <remarks>
        /// <see cref="EditorMode.Land"/> has only the one tool, so entering it is unambiguous.
        /// <see cref="EditorMode.Build"/> opens on <see cref="EditTool.Select"/> - the tool
        /// that places nothing - so that a mode nobody has chosen a tool in yet cannot put
        /// anything on the map by being entered.
        /// </remarks>
        public static EditTool DefaultToolOf(EditorMode mode)
        {
            switch (mode)
            {
                case EditorMode.Land:
                    return EditTool.Land;
                case EditorMode.Build:
                    return EditTool.Select;
                default:
                    return EditTool.None;
            }
        }

        /// <summary>
        /// Reports whether a mode can pick, move or delete one kind of thing.
        /// </summary>
        /// <param name="mode">The mode in force.</param>
        /// <param name="target">Which of a level's lists the thing is in.</param>
        /// <returns><c>true</c> when the mouse in that mode can reach it.</returns>
        /// <remarks>
        /// <see cref="EditTarget.None"/> is reachable in every mode, because "nothing
        /// selected" is a legal state to be in whatever is being worked on - it is the state
        /// a mode change leaves behind when it drops a selection the new mode cannot hold.
        /// </remarks>
        public static bool Reaches(EditorMode mode, EditTarget target)
        {
            if (target == EditTarget.None)
            {
                return true;
            }

            switch (mode)
            {
                case EditorMode.Land:
                    return target == EditTarget.Land;
                case EditorMode.Build:
                    return target != EditTarget.Land;

                // An unconfigured editor still reaches everything, which is what the whole
                // editor did before there were modes at all.
                default:
                    return true;
            }
        }

        /// <summary>
        /// Returns the mode that is not this one.
        /// </summary>
        /// <param name="mode">The mode in force.</param>
        /// <returns>The other mode.</returns>
        /// <remarks>
        /// There are two, so switching is a toggle and wants one key rather than two. An
        /// editor with no mode set answers <see cref="EditorMode.Land"/>, so the key does
        /// something rather than nothing.
        /// </remarks>
        public static EditorMode Other(EditorMode mode)
            => mode == EditorMode.Land ? EditorMode.Build : EditorMode.Land;
    }
}
