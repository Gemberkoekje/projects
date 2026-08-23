using System;

namespace IronFlag.Editing
{
    /// <summary>
    /// What a click in the map does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One tool at a time, and every tool selects with the same button, because an editor in
    /// which selecting is a different gesture per tool is one where the player has to
    /// remember which mode they are in before they can find out which mode they are in. The
    /// difference between the tools is only what a click on <em>empty</em> ground does:
    /// <see cref="Select"/> clears the selection, and every other tool puts something there.
    /// </para>
    /// <para>
    /// Dragging is the same in all of them too - it moves whatever is under the cursor - so
    /// a player who never changes tool can still move everything on the map.
    /// </para>
    /// </remarks>
    [Serializable]
    public enum EditTool
    {
        /// <summary>No tool, which is what an unconfigured editor reads.</summary>
        None = 0,

        /// <summary>Pick things up and move them; a click on nothing selects nothing.</summary>
        Select = 1,

        /// <summary>Drag out a new rectangle of dry ground.</summary>
        Land = 2,

        /// <summary>Place whatever the palette is showing.</summary>
        Structure = 3,

        /// <summary>Place a flag tower for the side the palette is showing.</summary>
        Tower = 4,

        /// <summary>Move a side's bunker, or give a side one it has not got.</summary>
        Bunker = 5,
    }
}
