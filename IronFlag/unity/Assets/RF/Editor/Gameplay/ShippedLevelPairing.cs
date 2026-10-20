using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using IronFlag.Editing;
using IronFlag.Levels;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// Records which of a shipped map's props are each other's opposite numbers, and writes
    /// the maps back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The migration for <see cref="LevelStructure.Pair"/>, and the batch twin of the level
    /// editor's own <em>Pair up the halves</em> button. Both shipped maps were drawn before
    /// pairs existed and both are built exactly in halves, so this records forty-odd
    /// relationships that are visibly there rather than inventing any. It moves nothing and
    /// changes nothing about how either map plays - a pair number is an editing annotation
    /// and no rule of play reads it.
    /// </para>
    /// <para>
    /// It is a menu item rather than something that happens on load, for the reason
    /// <see cref="LevelEdits.PairTheHalves"/> gives: running it puts back any pair somebody
    /// deliberately broke, so it has to be something a person asks for. The editor's button
    /// covers the map being worked on; this one covers the shipped folder, which the editor
    /// cannot write to - it saves into the player's own folder, which is the point of that
    /// folder.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath unity
    ///     -executeMethod IronFlag.Editor.Gameplay.ShippedLevelPairing.PairUpShippedMaps -logFile -
    /// </code>
    /// </example>
    public static class ShippedLevelPairing
    {
        /// <summary>
        /// Pairs up the halves of every map in StreamingAssets and writes them back.
        /// </summary>
        /// <remarks>
        /// A map that gained no pairs is left untouched on disk rather than rewritten
        /// identically, so running this twice makes one commit and not two.
        /// </remarks>
        [MenuItem("Tools/IronFlag/Pair Up Shipped Maps", false, 158)]
        public static void PairUpShippedMaps()
        {
            string folder = LevelLibrary.ShippedFolder;
            if (!Directory.Exists(folder))
            {
                Debug.LogError($"IronFlag: there is no shipped level folder at {folder}.");
                return;
            }

            var written = new List<string>();

            foreach (string path in Directory.GetFiles(folder, "*" + LevelLibrary.Extension))
            {
                if (!LevelFile.TryRead(path, out LevelDefinition level, out string problem))
                {
                    Debug.LogError(problem);
                    continue;
                }

                int found = LevelEdits.PairTheHalves(level);
                if (found == 0)
                {
                    Debug.Log(
                        $"IronFlag: {Path.GetFileName(path)} had nothing left to pair up.");
                    continue;
                }

                if (!LevelFile.TryWrite(path, level, out string trouble))
                {
                    Debug.LogError(trouble);
                    continue;
                }

                written.Add($"{Path.GetFileName(path)} ({found})");
            }

            AssetDatabase.Refresh();
            Debug.Log(
                written.Count == 0
                    ? "IronFlag: every shipped map was already paired up."
                    : $"IronFlag: paired up {string.Join(", ", written)}.");
        }
    }
}
