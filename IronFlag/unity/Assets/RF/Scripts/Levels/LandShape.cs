using System;

namespace IronFlag.Levels
{
    /// <summary>
    /// What outline a piece of land is cut to inside the box a level file gives it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Displacing a coastline makes a rectangle's <em>edge</em> natural at the metre scale.
    /// It does not make a 164 by 79 metre rectangle stop being a rectangle at the
    /// hundred-metre scale, and noise will not rescue it: at that scale the island's outline
    /// is authored, and the only honest fix is to let a level author something other than a
    /// box.
    /// </para>
    /// <para>
    /// So one alternative shape, and the same four numbers. A level file already states a
    /// piece of land as its edges, which is a bounding box, so an ellipse is the one
    /// inscribed in that box and the format costs exactly one optional word. Three or four
    /// overlapping ellipses with a displaced coast is an island; a polygon list would be
    /// more general, would be unreadable by hand - which is the property the whole format
    /// was chosen for - and would be the wrong thing to hand a level editor that drags
    /// corners.
    /// </para>
    /// </remarks>
    [Serializable]
    public enum LandShape
    {
        /// <summary>Not a shape, which is what an unrecognised name in a level file reads.</summary>
        None = 0,

        /// <summary>The whole box: the default, and what every map before this one is made of.</summary>
        Rectangle = 1,

        /// <summary>The ellipse inscribed in the box.</summary>
        Ellipse = 2,
    }
}
