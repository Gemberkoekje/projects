namespace IronFlag.Levels
{
    /// <summary>
    /// The two scenes the game has, by name, and where they are saved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A scene is loaded at runtime by name and generated at edit time by path, and those two
    /// strings have to be the same scene or the editor's Play button loads nothing and says
    /// so at the very moment somebody is trying to look at their map. So the name is written
    /// once and the path is built from it.
    /// </para>
    /// <para>
    /// Runtime rather than editor code, because <c>SceneManager.LoadScene</c> is what needs
    /// the name and a built game has no scene generator in it.
    /// </para>
    /// </remarks>
    public static class LevelScenes
    {
        /// <summary>Folder every scene in this project is saved under.</summary>
        public const string Folder = "Assets/RF/Scenes";

        /// <summary>The scene a match is played in.</summary>
        public const string Game = "Sandbox";

        /// <summary>The scene a map is built in.</summary>
        public const string Editor = "LevelEditor";

        /// <summary>Where the game scene is saved.</summary>
        public static string GamePath => PathFor(Game);

        /// <summary>Where the editor scene is saved.</summary>
        public static string EditorPath => PathFor(Editor);

        /// <summary>
        /// Returns where a scene of that name is saved.
        /// </summary>
        /// <param name="name">Scene name, without a folder or an extension.</param>
        /// <returns>A project-relative asset path.</returns>
        public static string PathFor(string name) => $"{Folder}/{name}.unity";
    }
}
