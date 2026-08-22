using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IronFlag.Levels
{
    /// <summary>
    /// Where level files live, and the order they are looked for in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two folders, and the order between them is the whole design. A level shipped with the
    /// game sits in <c>StreamingAssets/Levels</c>, which is a plain folder of plain files in
    /// a built game as well as in the editor - so a map can be opened in a text editor and
    /// changed without Unity, which is the point of having a format at all. A level the
    /// player made sits in <c>persistentDataPath/Levels</c>, and <em>shadows</em> the
    /// shipped one of the same name.
    /// </para>
    /// <para>
    /// That shadowing is the scaffold the in-game editor is going to stand on: editing a
    /// shipped map writes a copy next to the player's saves, the game picks the copy up
    /// without being told to, and reverting is deleting a file. Nothing has to be able to
    /// write into the install folder, which on a real machine it often cannot.
    /// </para>
    /// <para>
    /// Desktop only, deliberately. On Android and the web
    /// <see cref="Application.streamingAssetsPath"/> is a URL rather than a directory and
    /// this class would have to grow a web request; the game is a local split-screen game
    /// on a PC, so it does not.
    /// </para>
    /// </remarks>
    public static class LevelLibrary
    {
        /// <summary>The map the game opens on when nothing else is asked for.</summary>
        public const string DefaultLevel = "iron-channel";

        /// <summary>Folder name used under both roots.</summary>
        public const string Folder = "Levels";

        /// <summary>Extension every level file carries.</summary>
        public const string Extension = ".json";

        /// <summary>Folder the shipped maps are read from.</summary>
        public static string ShippedFolder => Path.Combine(Application.streamingAssetsPath, Folder);

        /// <summary>Folder the player's own maps are read from and written to.</summary>
        public static string UserFolder => Path.Combine(Application.persistentDataPath, Folder);

        /// <summary>
        /// Returns where a shipped level of that name would be.
        /// </summary>
        /// <param name="name">Level name, without a folder or an extension.</param>
        /// <returns>The full path, whether or not anything is there.</returns>
        public static string ShippedPathFor(string name)
            => Path.Combine(ShippedFolder, name + Extension);

        /// <summary>
        /// Returns where a player-made level of that name would be.
        /// </summary>
        /// <param name="name">Level name, without a folder or an extension.</param>
        /// <returns>The full path, whether or not anything is there.</returns>
        /// <remarks>Where a level editor saves. Nothing writes into the shipped folder.</remarks>
        public static string UserPathFor(string name)
            => Path.Combine(UserFolder, name + Extension);

        /// <summary>
        /// Returns the file a level name resolves to.
        /// </summary>
        /// <param name="name">Level name, without a folder or an extension.</param>
        /// <returns>
        /// The player's copy if there is one, else the shipped copy. The shipped path is
        /// returned even when nothing is there, so a failure to load names the file the
        /// author meant rather than the one they did not write.
        /// </returns>
        public static string PathFor(string name)
        {
            string mine = UserPathFor(name);
            return File.Exists(mine) ? mine : ShippedPathFor(name);
        }

        /// <summary>
        /// Lists every level that can be loaded, from both folders.
        /// </summary>
        /// <returns>Level names, without folders or extensions, in alphabetical order.</returns>
        /// <remarks>
        /// A name that exists in both folders is listed once: the player's copy is the same
        /// map, edited, and not a second entry on a menu.
        /// </remarks>
        public static List<string> Names()
        {
            var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            Collect(ShippedFolder, found);
            Collect(UserFolder, found);
            return new List<string>(found);
        }

        /// <summary>
        /// Creates the folder a path is in, if it is not there.
        /// </summary>
        /// <param name="path">A file path.</param>
        /// <remarks>Called before writing. Reading never creates anything.</remarks>
        public static void EnsureFolderFor(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }

        private static void Collect(string folder, SortedSet<string> into)
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(folder, "*" + Extension))
            {
                into.Add(Path.GetFileNameWithoutExtension(file));
            }
        }
    }
}
