using UnityEditor;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Editing;
using IronFlag.Levels;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// Draws the one-player map the game ships with, and writes it into StreamingAssets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shipped map is <em>generated</em> rather than hand-authored, which is the whole
    /// reason this exists as a menu item instead of a JSON file somebody typed. A solo map is
    /// one bunker and a field of small fortresses settled away from each other, and settling
    /// is arithmetic - the generator does it, and doing it by hand would be doing the
    /// generator's job worse. What is committed is its output, so the game reads a plain
    /// file like every other map and nothing has to run at load.
    /// </para>
    /// <para>
    /// The seed is written down here, so pressing this again produces the same map: a
    /// shipped level that quietly changed under a rebuild would be a map players had learned
    /// and lost. Change <see cref="Seed"/> deliberately, look at what comes out, and commit
    /// the file - that is the whole workflow, and it is the same one the level editor's
    /// generate button offers, aimed at the shipped folder instead of the player's.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath unity
    ///     -executeMethod IronFlag.Editor.Gameplay.SoloLevelBuilder.BuildAndSave -logFile -
    /// </code>
    /// </example>
    public static class SoloLevelBuilder
    {
        /// <summary>The level name, which is what the file is called.</summary>
        public const string LevelName = "iron-watch";

        /// <summary>What the map calls itself on the menu.</summary>
        public const string Title = "Iron Watch";

        /// <summary>Which map the generator draws.</summary>
        /// <remarks>
        /// One number, written down, because a shipped map has to be the same map every
        /// time. Rolled once by hand rather than chosen for a reason - what makes this seed
        /// the shipped one is that somebody looked at what it drew.
        /// </remarks>
        public const int Seed = 20260827;

        /// <summary>
        /// Draws the shipped one-player map and writes it into StreamingAssets.
        /// </summary>
        /// <remarks>
        /// Anything wrong with what came out is logged rather than refused, the same way
        /// every other level warning in this project is - and a fault here is a bug in the
        /// generator worth reading, not a file worth withholding.
        /// </remarks>
        [MenuItem("Tools/IronFlag/Build Solo Level", false, 157)]
        public static void BuildAndSave()
        {
            LevelDefinition level = Draw();
            if (level == null)
            {
                Debug.LogError("IronFlag: the generator drew nothing; no solo map was written.");
                return;
            }

            foreach (string problem in LevelValidation.Problems(level))
            {
                Debug.LogWarning($"IronFlag: {LevelName} - {problem}");
            }

            string path = LevelLibrary.ShippedPathFor(LevelName);
            if (!LevelFile.TryWrite(path, level, out string trouble))
            {
                Debug.LogError(trouble);
                return;
            }

            AssetDatabase.Refresh();
            Debug.Log(
                $"IronFlag: wrote {level.Name} to {path} - "
                + $"{level.TowersFor(Team.Brown).Count} enemy towers, "
                + $"{level.Structures.Length} props.");
        }

        /// <summary>
        /// Draws the shipped one-player map without writing it anywhere.
        /// </summary>
        /// <returns>The map, or <c>null</c> when the generator drew nothing.</returns>
        /// <remarks>Split out so a test can check the shipped seed still draws a playable map.</remarks>
        public static LevelDefinition Draw()
        {
            var options = new MapOptions
            {
                Seed = Seed,
                Difficulty = MapDifficulty.Medium,
                Layout = MapLayout.None,
                Symmetry = MapSymmetry.Mirrored,
                Players = 1,
                Name = Title,
            };

            return LevelGenerator.Generate(options);
        }
    }
}
