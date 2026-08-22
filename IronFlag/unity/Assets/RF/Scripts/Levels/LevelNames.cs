using System;
using IronFlag.Core;
using IronFlag.Destruction;

namespace IronFlag.Levels
{
    /// <summary>
    /// Turns the words in a level file into the enums the game is written in, and back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity's <see cref="UnityEngine.JsonUtility"/> writes an enum as the integer behind it,
    /// which is the one thing a level file must never contain. A file that says
    /// <c>"Kind": 4</c> cannot be read by a person, cannot be reviewed in a diff, and
    /// silently means something else the day somebody inserts a row into
    /// <see cref="StructureKind"/>. So a level file carries names, and this is where they
    /// stop being strings.
    /// </para>
    /// <para>
    /// A name that is not recognised resolves to the empty member -
    /// <see cref="StructureKind.None"/>, <see cref="Team.None"/> - rather than throwing.
    /// A typo in a level file should cost you one prop and a warning that names it, not the
    /// whole map.
    /// </para>
    /// </remarks>
    public static class LevelNames
    {
        /// <summary>
        /// Reads a structure name.
        /// </summary>
        /// <param name="name">Name as written in the level file, e.g. <c>Bridge</c>.</param>
        /// <returns>The structure, or <see cref="StructureKind.None"/> when unrecognised.</returns>
        public static StructureKind ToStructure(string name) => Parse(name, StructureKind.None);

        /// <summary>
        /// Reads a team name.
        /// </summary>
        /// <param name="name">Name as written in the level file, e.g. <c>Green</c>.</param>
        /// <returns>The team, or <see cref="Team.None"/> when unrecognised.</returns>
        public static Team ToTeam(string name) => Parse(name, Team.None);

        /// <summary>
        /// Reads one enum member by name, case-insensitively.
        /// </summary>
        /// <typeparam name="T">Any enum.</typeparam>
        /// <param name="name">Name to read.</param>
        /// <param name="fallback">What an unrecognised or numeric name resolves to.</param>
        /// <returns>The member, or <paramref name="fallback"/>.</returns>
        /// <remarks>
        /// Digits are rejected on purpose. <see cref="Enum.TryParse{T}(string, bool, out T)"/>
        /// happily accepts a number and hands back a member that does not exist, which is
        /// exactly the unreadable file this class exists to prevent.
        /// </remarks>
        private static T Parse<T>(string name, T fallback)
            where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(name) || char.IsDigit(name.Trim()[0]))
            {
                return fallback;
            }

            return Enum.TryParse(name.Trim(), true, out T parsed) && Enum.IsDefined(typeof(T), parsed)
                ? parsed
                : fallback;
        }
    }
}
