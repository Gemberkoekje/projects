using System;

namespace IronFlag.Editing
{
    /// <summary>
    /// What a click in the map does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One tool at a time, and each tool belongs to exactly one <see cref="EditorMode"/> -
    /// <see cref="Land"/> to the mode that shapes the island, the rest to the mode that
    /// places things on it. Choosing a tool is therefore also choosing a mode, which is why
    /// nothing stores the mode separately; see <see cref="EditorModes.Of"/>.
    /// </para>
    /// <para>
    /// Within a mode every tool still selects with the same button, and they differ only in
    /// what a click on ground the mode can reach nothing on does: <see cref="Select"/>
    /// clears the selection and the others put something there. Dragging is the same in all
    /// of them - it moves whatever is under the cursor - so a player who never changes tool
    /// can still move everything the mode they are in can see.
    /// </para>
    /// </remarks>
    [Serializable]
    public enum EditTool
    {
        /// <summary>No tool, which is what an unconfigured editor reads.</summary>
        None = 0,

        /// <summary>Pick things up and move them; a click on nothing selects nothing.</summary>
        Select = 1,

        /// <summary>
        /// Shape the island: drag out new dry ground, and drag a rectangle's corners.
        /// </summary>
        /// <remarks>
        /// The whole of <see cref="EditorMode.Land"/>, and the only tool in it. There is no
        /// second land tool that merely selects, because the mode already cannot touch
        /// anything but land - so the one thing a "select" would add is the inability to draw,
        /// which is the reason for being in this mode at all.
        /// </remarks>
        Land = 2,

        /// <summary>Place whatever the palette is showing.</summary>
        Structure = 3,

        /// <summary>Place a flag tower for the side the palette is showing.</summary>
        Tower = 4,

        /// <summary>Move a side's bunker, or give a side one it has not got.</summary>
        Bunker = 5,
    }
}
