using System.Collections.Generic;
using UnityEngine;
using IronFlag.Levels;

namespace IronFlag.Editing
{
    /// <summary>
    /// Undo and redo, as a list of whole maps.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A snapshot of the entire level per step, kept as the JSON the file format already
    /// writes - not a list of reversible commands. Commands are the usual answer and they are
    /// the wrong size here: a level is a few kilobytes of text, a hundred of them is a few
    /// hundred kilobytes, and the alternative is an inverse operation per edit that has to
    /// stay correct as the format grows. This one is correct by construction, and it stays
    /// correct the day somebody adds a field, because the thing it snapshots is the thing the
    /// file contains.
    /// </para>
    /// <para>
    /// It also gets a real property for free: undoing is exactly what loading that map from
    /// disk would do, so there is no state anywhere else in the editor that undo can miss.
    /// Everything the session shows is derived from the level it is holding.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// history.Reset(level);
    /// LevelEdits.AddStructure(level, StructureKind.Tree, at);
    /// history.Record(level);
    /// </code>
    /// </example>
    public sealed class EditHistory
    {
        /// <summary>How many steps back the editor remembers.</summary>
        /// <remarks>
        /// Deep enough to cover a session's worth of placing props and shallow enough that
        /// the whole history is under a megabyte of text on any map this format can describe.
        /// </remarks>
        public const int Depth = 64;

        private readonly List<string> steps = new List<string>();

        private int cursor = -1;

        /// <summary>Whether there is a step to go back to.</summary>
        public bool CanUndo => cursor > 0;

        /// <summary>Whether a step has been undone and can be put back.</summary>
        public bool CanRedo => cursor >= 0 && cursor < steps.Count - 1;

        /// <summary>How many steps are being held, the current one included.</summary>
        public int Count => steps.Count;

        /// <summary>
        /// Throws the history away and starts again from one map.
        /// </summary>
        /// <param name="level">The map that becomes the first step.</param>
        /// <remarks>Called when a level is opened or a new one is made.</remarks>
        public void Reset(LevelDefinition level)
        {
            steps.Clear();
            cursor = -1;

            if (level != null)
            {
                steps.Add(LevelFile.ToJson(level));
                cursor = 0;
            }
        }

        /// <summary>
        /// Records a map as the newest step.
        /// </summary>
        /// <param name="level">The map, after the change.</param>
        /// <returns><c>true</c> when a step was added.</returns>
        /// <remarks>
        /// <para>
        /// Called <em>after</em> a change is committed rather than before it, so the list is
        /// always a list of states the editor has actually been in and the current one is
        /// always the last.
        /// </para>
        /// <para>
        /// A map identical to the step already on top does not add one. A drag that ends
        /// where it began, or a field re-typed to the value it already had, is not an edit,
        /// and an undo stack full of them is one where undo appears not to work.
        /// </para>
        /// </remarks>
        public bool Record(LevelDefinition level)
        {
            if (level == null)
            {
                return false;
            }

            string json = LevelFile.ToJson(level);
            if (cursor >= 0 && steps[cursor] == json)
            {
                return false;
            }

            // Anything that was undone is now a future that did not happen.
            if (cursor < steps.Count - 1)
            {
                steps.RemoveRange(cursor + 1, steps.Count - cursor - 1);
            }

            steps.Add(json);

            if (steps.Count > Depth)
            {
                steps.RemoveAt(0);
            }

            cursor = steps.Count - 1;
            return true;
        }

        /// <summary>
        /// Steps back one change.
        /// </summary>
        /// <returns>The map as it was, or <c>null</c> when there is nothing to go back to.</returns>
        public LevelDefinition Undo()
        {
            if (!CanUndo)
            {
                return null;
            }

            cursor--;
            return Read(cursor);
        }

        /// <summary>
        /// Puts back a change that was undone.
        /// </summary>
        /// <returns>The map as it was, or <c>null</c> when there is nothing to put back.</returns>
        public LevelDefinition Redo()
        {
            if (!CanRedo)
            {
                return null;
            }

            cursor++;
            return Read(cursor);
        }

        /// <summary>
        /// Reads one step back out as a level.
        /// </summary>
        /// <param name="step">Which step.</param>
        /// <returns>A fresh level, sharing nothing with anything the editor is holding.</returns>
        private LevelDefinition Read(int step)
            => JsonUtility.FromJson<LevelDefinition>(steps[step]);
    }
}
