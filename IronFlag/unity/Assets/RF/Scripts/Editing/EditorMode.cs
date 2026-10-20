using System;

namespace IronFlag.Editing
{
    /// <summary>
    /// Which half of a map the mouse is working on: the ground itself, or the things
    /// standing on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The editor started without one. Every tool selected with the same button and the
    /// tools differed only in what a click on <em>empty</em> ground did - which reads well
    /// until you notice that a finished map has no empty ground on it. An island covers the
    /// part of the map worth building on, so a click meant to place a tree landed on the
    /// shore beneath it, picked the shore, and dragged the coastline away instead. Shift
    /// forced a placement, but a gesture nobody can guess is a gesture nobody uses.
    /// </para>
    /// <para>
    /// A mode fixes that by making the two jobs blind to each other: in <see cref="Land"/>
    /// the mouse cannot see a prop, and in <see cref="Build"/> it cannot see the ground.
    /// That is stronger than an ordering rule, because it holds in both directions - a
    /// coastline cannot be nudged while turrets are being placed, and a bunker cannot be
    /// picked up while the shore under it is being pulled out.
    /// </para>
    /// <para>
    /// It is <em>derived</em> from the tool in hand rather than stored beside it; see
    /// <see cref="EditorModes.Of"/> for why.
    /// </para>
    /// </remarks>
    [Serializable]
    public enum EditorMode
    {
        /// <summary>No mode, which is what an unconfigured editor reads.</summary>
        None = 0,

        /// <summary>Shaping the island: rectangles of dry ground, and nothing else.</summary>
        Land = 1,

        /// <summary>Placing and moving what stands on it: props, towers and bunkers.</summary>
        Build = 2,
    }
}
