using System.Collections.Generic;
using UnityEditor;
using IronFlag.Levels;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// The scenes a built game contains, in the order it needs them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the whole content of this file, and it is load-bearing twice over. Unity
    /// starts a built game in the scene at index 0, so whichever scene is first is what the
    /// game <em>is</em> to somebody who has just double-clicked it - and
    /// <c>SceneManager.LoadScene</c> can only reach a scene that is on this list at all, so a
    /// scene left off it is a button that silently does nothing.
    /// </para>
    /// <para>
    /// One owner rather than a copy in each scene builder. Every builder here rewrites the list
    /// when it saves, and two builders that each knew a different order would fight: whichever
    /// menu item was pressed last would decide what the game boots into, which is not a thing
    /// anybody would think to check after rebuilding a map.
    /// </para>
    /// <para>
    /// A scene that has not been generated yet is left out rather than listed as missing, so a
    /// fresh checkout where only some of the builders have been run gets a build list that is
    /// short rather than one that is broken. Running the rest puts them in the right places.
    /// </para>
    /// </remarks>
    internal static class BuildScenes
    {
        /// <summary>
        /// Every scene the game ships with, first to last.
        /// </summary>
        /// <returns>Asset paths, in build order.</returns>
        internal static List<string> Order() => new List<string>
        {
            LevelScenes.MainMenuPath,
            LevelScenes.GamePath,
            LevelScenes.EditorPath,
        };

        /// <summary>
        /// Rewrites the build list so the game's own scenes are on it, in order.
        /// </summary>
        /// <remarks>
        /// Anything else already on the list is kept, after them: this owns where the game's
        /// three scenes go, not what somebody else has added for their own reasons. An existing
        /// entry keeps whether it was enabled, so a scene somebody deliberately switched off
        /// stays off when it is moved.
        /// </remarks>
        internal static void Register()
        {
            List<string> wanted = Order();
            var listed = new List<EditorBuildSettingsScene>();

            foreach (string path in wanted)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    continue;
                }

                listed.Add(Existing(path) ?? new EditorBuildSettingsScene(path, true));
            }

            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (!wanted.Contains(scene.path))
                {
                    listed.Add(scene);
                }
            }

            EditorBuildSettings.scenes = listed.ToArray();
        }

        private static EditorBuildSettingsScene Existing(string path)
        {
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.path == path)
                {
                    return scene;
                }
            }

            return null;
        }
    }
}
