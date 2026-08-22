using System;
using System.IO;
using UnityEngine;

namespace IronFlag.Levels
{
    /// <summary>
    /// Reads and writes a <see cref="LevelDefinition"/> as JSON on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity's <see cref="JsonUtility"/> rather than a JSON library, because the format was
    /// designed to be readable by it: flat classes, public fields, no dictionaries, no
    /// inheritance, and names instead of enums. The dependency a nicer serialiser would add
    /// is not worth what it would buy on a file this shape.
    /// </para>
    /// <para>
    /// Every failure that can happen to a file - missing, unreadable, not JSON, from a
    /// newer version of the game - comes back as a sentence rather than an exception, and
    /// the callers turn it into a warning that names the file. A level that will not load
    /// should tell you which one and why, on the first line of the log.
    /// </para>
    /// </remarks>
    public static class LevelFile
    {
        /// <summary>
        /// Reads a level file.
        /// </summary>
        /// <param name="path">Full path to the file.</param>
        /// <param name="level">The level, or <c>null</c> when it could not be read.</param>
        /// <param name="problem">Empty on success, else what went wrong, naming the file.</param>
        /// <returns><c>true</c> when a level came back.</returns>
        /// <example>
        /// <code>
        /// if (!LevelFile.TryRead(LevelLibrary.PathFor("iron-channel"), out LevelDefinition level, out string problem))
        /// {
        ///     Debug.LogWarning(problem);
        /// }
        /// </code>
        /// </example>
        public static bool TryRead(string path, out LevelDefinition level, out string problem)
        {
            level = null;
            problem = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                problem = "IronFlag: no level file was named.";
                return false;
            }

            if (!File.Exists(path))
            {
                problem = $"IronFlag: there is no level file at '{path}'.";
                return false;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException error)
            {
                problem = $"IronFlag: could not read '{path}': {error.Message}";
                return false;
            }
            catch (UnauthorizedAccessException error)
            {
                problem = $"IronFlag: could not read '{path}': {error.Message}";
                return false;
            }

            return TryParse(text, path, out level, out problem);
        }

        /// <summary>
        /// Reads a level file, logging anything that goes wrong.
        /// </summary>
        /// <param name="path">Full path to the file.</param>
        /// <returns>The level, or <c>null</c>.</returns>
        public static LevelDefinition Read(string path)
        {
            if (TryRead(path, out LevelDefinition level, out string problem))
            {
                return level;
            }

            Debug.LogError(problem);
            return null;
        }

        /// <summary>
        /// Reads a level out of JSON text.
        /// </summary>
        /// <param name="text">The file's contents.</param>
        /// <param name="source">What to call the text in a problem, usually its path.</param>
        /// <param name="level">The level, or <c>null</c> when it could not be read.</param>
        /// <param name="problem">Empty on success, else what went wrong.</param>
        /// <returns><c>true</c> when a level came back.</returns>
        /// <remarks>
        /// Split out from <see cref="TryRead"/> so the parsing can be tested without a file,
        /// and so a level that arrives from somewhere other than a disk - a text asset, an
        /// editor buffer - goes through exactly the same checks.
        /// </remarks>
        public static bool TryParse(string text, string source, out LevelDefinition level, out string problem)
        {
            level = null;
            problem = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                problem = $"IronFlag: '{source}' is empty.";
                return false;
            }

            LevelDefinition parsed;
            try
            {
                parsed = JsonUtility.FromJson<LevelDefinition>(text);
            }
            catch (ArgumentException error)
            {
                problem = $"IronFlag: '{source}' is not a level file: {error.Message}";
                return false;
            }

            if (parsed == null)
            {
                problem = $"IronFlag: '{source}' is not a level file.";
                return false;
            }

            if (parsed.SchemaVersion > LevelDefinition.Schema)
            {
                problem =
                    $"IronFlag: '{source}' is a version {parsed.SchemaVersion} level and this "
                    + $"build reads version {LevelDefinition.Schema}. Update the game.";
                return false;
            }

            level = parsed;
            return true;
        }

        /// <summary>
        /// Writes a level file, creating its folder if it is missing.
        /// </summary>
        /// <param name="path">Full path to write to.</param>
        /// <param name="level">The level to write.</param>
        /// <param name="problem">Empty on success, else what went wrong.</param>
        /// <returns><c>true</c> when the file was written.</returns>
        /// <remarks>
        /// Pretty-printed, always. A level file is read by people and lives in source
        /// control; a one-line map is a file no diff can review.
        /// </remarks>
        public static bool TryWrite(string path, LevelDefinition level, out string problem)
        {
            problem = string.Empty;

            if (level == null)
            {
                problem = "IronFlag: there is no level to write.";
                return false;
            }

            try
            {
                LevelLibrary.EnsureFolderFor(path);
                File.WriteAllText(path, ToJson(level));
            }
            catch (IOException error)
            {
                problem = $"IronFlag: could not write '{path}': {error.Message}";
                return false;
            }
            catch (UnauthorizedAccessException error)
            {
                problem = $"IronFlag: could not write '{path}': {error.Message}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Renders a level as the JSON that goes in a file.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <returns>Pretty-printed JSON.</returns>
        public static string ToJson(LevelDefinition level) => JsonUtility.ToJson(level, true);
    }
}
