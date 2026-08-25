using System;

namespace IronFlag.Editing
{
    /// <summary>
    /// Whether the two halves of a generated map are the same shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Mirrored means rotated, not reflected.</strong> Every map this game has is
    /// built in pairs turned half a turn about the origin, and the difference is the whole
    /// point: a reflection gives one side a left-handed base and the other a right-handed
    /// one, and the two runs to the enemy flag are then not the same shape. A rotation makes
    /// them identical. See <see cref="LevelEdits.Turned"/>, which is where that arithmetic
    /// is actually written down.
    /// </para>
    /// <para>
    /// Asymmetrical is not "unfair" so much as "unmeasured": both sides still get a bunker,
    /// a real tower, a decoy and the same count of everything else, and both are still
    /// joined by the same guaranteed crossing. What differs is the ground, and whether that
    /// favours somebody is a question only playing it answers.
    /// </para>
    /// </remarks>
    [Serializable]
    public enum MapSymmetry
    {
        /// <summary>Not a symmetry, which is what an unset option reads.</summary>
        None = 0,

        /// <summary>One half generated and the other turned half a turn out of it.</summary>
        Mirrored = 1,

        /// <summary>Each half generated on its own draws.</summary>
        Asymmetrical = 2,
    }
}
