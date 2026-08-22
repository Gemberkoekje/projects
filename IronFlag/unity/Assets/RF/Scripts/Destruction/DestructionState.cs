using System;

namespace IronFlag.Destruction
{
    /// <summary>
    /// The three shapes a destructible thing can be in, in the order it passes through them.
    /// </summary>
    /// <remarks>
    /// The design document's v0.1 destruction is a mesh swap - "intact -> damaged ->
    /// destroyed mesh per structure, with a debris VFX burst on each transition" - so the
    /// state is the whole model, and every destructible in the asset spec ships exactly
    /// these three meshes. The order matters: the values increase as the building gets
    /// worse, which is what lets a transition be compared rather than tabulated.
    /// </remarks>
    [Serializable]
    public enum DestructionState
    {
        /// <summary>Nothing decided yet, which is what a component reads before it wakes up.</summary>
        None = 0,

        /// <summary>Untouched.</summary>
        Intact = 1,

        /// <summary>Hurt but standing, and still cover.</summary>
        Damaged = 2,

        /// <summary>Rubble. Whatever it did for a living, it no longer does.</summary>
        Destroyed = 3,
    }
}
