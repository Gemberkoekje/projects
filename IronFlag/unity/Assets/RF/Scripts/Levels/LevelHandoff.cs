namespace IronFlag.Levels
{
    /// <summary>
    /// The one thing that survives a scene change: which map the next scene should open, and
    /// whether it was the editor that sent you there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The editor and the game are two scenes, so playtesting is a scene load and everything
    /// in the old scene stops existing. Two facts have to get across that gap, and they are
    /// both about which map rather than about its contents - the map itself travels the way
    /// it always has, as a file. That is the whole reason this is three fields and not a
    /// serialised level: the editor saves into
    /// <see cref="LevelLibrary.UserFolder"/>, the game reads from
    /// <see cref="LevelLibrary.PathFor"/>, and the folder order already makes the player's
    /// copy win.
    /// </para>
    /// <para>
    /// Static state that outlives a scene is worth being suspicious of, so this is kept to
    /// the smallest thing that works: it is never read by anything that decides a rule, only
    /// by <see cref="LevelLoader"/> deciding which file to open and by the playtest deciding
    /// whether there is anywhere to go back to. A build that starts in the game, with nobody
    /// having been in the editor, sees exactly what it saw before this existed.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// LevelHandoff.Playtest("my-map");
    /// SceneManager.LoadScene(LevelScenes.Game);
    /// </code>
    /// </example>
    public static class LevelHandoff
    {
        /// <summary>The map the next scene should open, or empty when nobody asked.</summary>
        public static string Level { get; private set; } = string.Empty;

        /// <summary>Whether the editor is what sent the player into the game.</summary>
        /// <remarks>
        /// What decides whether a playtest offers a way back. A player who launched the game
        /// normally has no editor to return to, and a key that quietly took them to one would
        /// be a way to lose a match by mistake.
        /// </remarks>
        public static bool FromEditor { get; private set; }

        /// <summary>Whether a map has been asked for at all.</summary>
        public static bool IsAsked => !string.IsNullOrWhiteSpace(Level);

        /// <summary>
        /// Sends a map to the game to be played.
        /// </summary>
        /// <param name="name">Level name, without a folder or an extension.</param>
        public static void Playtest(string name)
        {
            Level = name == null ? string.Empty : name.Trim();
            FromEditor = true;
        }

        /// <summary>
        /// Sends a map back to the editor to be worked on.
        /// </summary>
        /// <param name="name">Level name, without a folder or an extension.</param>
        /// <remarks>
        /// Keeps the map and drops the way back, so a second playtest has to be asked for
        /// again. The editor is where the player now is; there is nothing behind it.
        /// </remarks>
        public static void Edit(string name)
        {
            Level = name == null ? string.Empty : name.Trim();
            FromEditor = false;
        }

        /// <summary>
        /// Returns the map that was asked for, or something else when none was.
        /// </summary>
        /// <param name="fallback">What to use when nobody asked for anything.</param>
        /// <returns>The requested level name, or <paramref name="fallback"/>.</returns>
        public static string LevelOr(string fallback) => IsAsked ? Level : fallback;

        /// <summary>
        /// Forgets everything, so the next scene opens whatever it was built around.
        /// </summary>
        /// <remarks>
        /// For the tests, which share a process and would otherwise inherit whichever map the
        /// test before them handed over.
        /// </remarks>
        public static void Clear()
        {
            Level = string.Empty;
            FromEditor = false;
        }
    }
}
