using System;

namespace IronFlag.Editing
{
    /// <summary>
    /// The one thing on the map the editor is working on: which list it is in, and where.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A list and an index rather than a reference to the object itself, and that is not a
    /// simplification. Every mutation in <see cref="LevelEdits"/> rebuilds the array it
    /// touches, and undo replaces the whole <see cref="IronFlag.Levels.LevelDefinition"/>
    /// with one parsed back out of JSON - so a held reference would survive both and point at
    /// an object nothing is looking at any more. An index survives the first and is checked
    /// against the second.
    /// </para>
    /// <para>
    /// A value type, so <see cref="Nothing"/> needs no allocation and a selection can be
    /// compared with <c>==</c> rather than by reading two fields at every call site.
    /// </para>
    /// </remarks>
    [Serializable]
    public readonly struct EditSelection : IEquatable<EditSelection>
    {
        /// <summary>Nothing selected.</summary>
        public static readonly EditSelection Nothing = default;

        /// <summary>
        /// Points at one entry of one of a level's lists.
        /// </summary>
        /// <param name="target">Which list.</param>
        /// <param name="index">Which entry of it.</param>
        public EditSelection(EditTarget target, int index)
        {
            Target = target;
            Index = index;
        }

        /// <summary>Which of the level's lists this points into.</summary>
        public EditTarget Target { get; }

        /// <summary>Which entry of that list, zero-based.</summary>
        public int Index { get; }

        /// <summary>Whether anything at all is selected.</summary>
        public bool IsSomething => Target != EditTarget.None && Index >= 0;

        /// <summary>
        /// Compares two selections.
        /// </summary>
        /// <param name="left">First selection.</param>
        /// <param name="right">Second selection.</param>
        /// <returns><c>true</c> when they point at the same entry of the same list.</returns>
        public static bool operator ==(EditSelection left, EditSelection right) => left.Equals(right);

        /// <summary>
        /// Compares two selections.
        /// </summary>
        /// <param name="left">First selection.</param>
        /// <param name="right">Second selection.</param>
        /// <returns><c>true</c> when they point at different things.</returns>
        public static bool operator !=(EditSelection left, EditSelection right) => !left.Equals(right);

        /// <inheritdoc/>
        public bool Equals(EditSelection other) => Target == other.Target && Index == other.Index;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is EditSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => ((int)Target * 397) ^ Index;

        /// <inheritdoc/>
        public override string ToString() => IsSomething ? $"{Target} {Index}" : "nothing";
    }
}
