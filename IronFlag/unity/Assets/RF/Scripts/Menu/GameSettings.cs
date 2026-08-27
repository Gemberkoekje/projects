using System;
using System.Collections.Generic;
using UnityEngine;

namespace IronFlag.Menu
{
    /// <summary>
    /// The three things about the window a player is allowed to change, and where they are
    /// remembered between sessions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first stored preference this project has ever had, which is why it is three
    /// settings rather than a panel of them. The rule used to pick them was that a setting has
    /// to <em>do</em> something the day it ships: a master volume would be a slider over a game
    /// with no sounds in it, and key rebinding is a feature rather than a row. Screen mode,
    /// resolution and quality tier all take effect the moment they are pressed, on a machine
    /// somebody is looking at.
    /// </para>
    /// <para>
    /// Written through on every change rather than saved on the way out. A menu is the one
    /// screen a player closes by killing the process - Quit, Alt+F4, the close box - and a
    /// setting that only reached disk on a clean exit would be one that mostly did not.
    /// </para>
    /// <para>
    /// Everything here is stored as what it means rather than as an index into a list: a
    /// resolution is a width and a height, and a quality tier is its name. Both lists belong to
    /// the machine rather than to the game - a monitor changes, a tier gets renamed - and an
    /// index saved against one list and read against another is a player who set 1920x1080
    /// once and starts in 800x600 on a different screen.
    /// </para>
    /// </remarks>
    public static class GameSettings
    {
        /// <summary>Preference key holding whether the game fills the screen.</summary>
        public const string FullscreenKey = "IronFlag.Fullscreen";

        /// <summary>Preference key holding the window width in pixels.</summary>
        public const string WidthKey = "IronFlag.Width";

        /// <summary>Preference key holding the window height in pixels.</summary>
        public const string HeightKey = "IronFlag.Height";

        /// <summary>Preference key holding the name of the quality tier.</summary>
        public const string QualityKey = "IronFlag.Quality";

        /// <summary>Smallest window the interface was laid out to survive, in pixels.</summary>
        /// <remarks>
        /// The level editor's panels are pixel-exact - see
        /// <see cref="IronFlag.Editing.EditorUi"/> - so a 236-pixel column of tools and a
        /// 344-pixel inspector eat a 640-wide window entirely. Resolutions below this are
        /// dropped from the list rather than offered and regretted.
        /// </remarks>
        public static readonly Vector2Int Smallest = new Vector2Int(1280, 720);

        /// <summary>
        /// What the quality tiers are called on a menu, where the project's own names for them
        /// are the wrong words.
        /// </summary>
        /// <remarks>
        /// The two tiers in this project are Unity's stock <c>Mobile</c> and <c>PC</c>, which
        /// name the machine they were written for rather than what they do - and this game only
        /// ever runs on the second of those machines, so a player reading "MOBILE" on a desktop
        /// is reading about a platform that is not on offer. Anything not in this table shows
        /// under its own name, so renaming a tier or adding a third one degrades to the truth
        /// rather than to a blank row.
        /// </remarks>
        private static readonly Dictionary<string, string> FriendlyNames =
            new Dictionary<string, string>
            {
                { "Mobile", "SIMPLE" },
                { "PC", "FULL" },
            };

        /// <summary>Whether the game fills the screen.</summary>
        public static bool Fullscreen
        {
            get => PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) != 0;
            private set => PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
        }

        /// <summary>The window size the player last chose, in pixels.</summary>
        public static Vector2Int Size
        {
            get => new Vector2Int(
                PlayerPrefs.GetInt(WidthKey, Screen.width),
                PlayerPrefs.GetInt(HeightKey, Screen.height));
            private set
            {
                PlayerPrefs.SetInt(WidthKey, value.x);
                PlayerPrefs.SetInt(HeightKey, value.y);
            }
        }

        /// <summary>The quality tier the player last chose, by name.</summary>
        public static string Quality
        {
            get => PlayerPrefs.GetString(QualityKey, CurrentQuality());
            private set => PlayerPrefs.SetString(QualityKey, value);
        }

        /// <summary>
        /// Puts every stored setting into effect.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Called once, by the menu, on the way up. The menu is the first scene in the build, so
        /// it is the one place every session passes through - and a settings screen that applied
        /// its own values while the game scene it launched kept the defaults would be one whose
        /// settings only worked on the menu.
        /// </para>
        /// <para>
        /// The size is applied before the tier, and both are applied whether or not they differ
        /// from what is already up. <see cref="Screen.SetResolution"/> on the size that is
        /// already showing costs nothing, and the alternative - comparing first - is a
        /// comparison that goes wrong in the editor, where <see cref="Screen.width"/> is the
        /// game view rather than the window.
        /// </para>
        /// </remarks>
        public static void Apply()
        {
            Vector2Int size = Size;
            Screen.SetResolution(
                size.x,
                size.y,
                Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);

            int tier = IndexOfQuality(Quality);
            if (tier >= 0)
            {
                QualitySettings.SetQualityLevel(tier, true);
            }
        }

        /// <summary>
        /// Fills the screen, or stops filling it.
        /// </summary>
        /// <param name="on">Whether the game should fill the screen.</param>
        public static void SetFullscreen(bool on)
        {
            Fullscreen = on;
            PlayerPrefs.Save();
            Apply();
        }

        /// <summary>
        /// Changes the window size.
        /// </summary>
        /// <param name="size">Width and height in pixels.</param>
        public static void SetSize(Vector2Int size)
        {
            Size = size;
            PlayerPrefs.Save();
            Apply();
        }

        /// <summary>
        /// Changes the quality tier.
        /// </summary>
        /// <param name="name">Tier name, as <see cref="QualitySettings.names"/> spells it.</param>
        public static void SetQuality(string name)
        {
            Quality = name == null ? string.Empty : name;
            PlayerPrefs.Save();
            Apply();
        }

        /// <summary>
        /// Lists the window sizes worth offering, largest first.
        /// </summary>
        /// <returns>
        /// Every distinct size the display supports that the interface fits in, plus the size
        /// the window is currently at, so a player who is already in an odd window can see
        /// where they are rather than reading somebody else's resolution.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Deduplicated by size alone: a monitor reports the same resolution once per refresh
        /// rate it can run it at, and a list offering 1920x1080 five times is a list that has
        /// leaked its data structure onto a menu. Refresh rate is not a setting here, so the
        /// duplicates carry nothing.
        /// </para>
        /// <para>
        /// <see cref="Smallest"/> filters what is <em>offered</em> and never where you already
        /// are. A window smaller than the minimum is a window somebody is in - dragged there,
        /// or a batch run at 640x480 - and leaving it off meant the row read one size while the
        /// list held another, with no way to step from one to the other. Excluding a size that
        /// is already in use does not stop it being in use; it just stops the player leaving it.
        /// </para>
        /// </remarks>
        public static List<Vector2Int> Sizes()
        {
            var found = new List<Vector2Int>();
            var seen = new HashSet<long>();

            foreach (Resolution mode in Screen.resolutions)
            {
                Offer(new Vector2Int(mode.width, mode.height), found, seen);
            }

            // The size in use last, so it is only added when the display did not offer it -
            // which happens in a windowed session somebody has dragged to a size of their own,
            // and in the editor, where the list is the desktop's and the game view is not on it.
            Include(Size, found, seen);

            found.Sort((first, second) => Compare(second, first));
            return found;
        }

        /// <summary>
        /// Lists the quality tiers, in the order the project declares them.
        /// </summary>
        /// <returns>Tier names, exactly as <see cref="QualitySettings.names"/> spells them.</returns>
        public static List<string> Qualities() => new List<string>(QualitySettings.names);

        /// <summary>
        /// Returns what a quality tier should be called on a menu.
        /// </summary>
        /// <param name="name">Tier name as the project spells it.</param>
        /// <returns>The player-facing name, upper case.</returns>
        public static string NameOfQuality(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            return FriendlyNames.TryGetValue(name, out string friendly)
                ? friendly
                : name.ToUpperInvariant();
        }

        /// <summary>
        /// Returns how a window size reads on a menu.
        /// </summary>
        /// <param name="size">Width and height in pixels.</param>
        /// <returns>The size, written the way a monitor's box writes it.</returns>
        public static string NameOfSize(Vector2Int size) => $"{size.x} x {size.y}";

        /// <summary>
        /// Returns where a quality tier sits in the project's list.
        /// </summary>
        /// <param name="name">Tier name as the project spells it.</param>
        /// <returns>Its index, or -1 when no tier is called that.</returns>
        /// <remarks>
        /// A name that is no longer a tier answers -1 and <see cref="Apply"/> leaves the quality
        /// alone, which is the right failure: a preference written by an older build should cost
        /// the player their choice, not drop them onto whichever tier happens to be first.
        /// </remarks>
        public static int IndexOfQuality(string name) => Array.IndexOf(QualitySettings.names, name);

        /// <summary>
        /// Forgets every stored setting.
        /// </summary>
        /// <remarks>
        /// For the tests, which share a process and a preference store with the machine they run
        /// on - so a test that wrote a resolution would otherwise change what the person running
        /// it sees the next time they press Play.
        /// </remarks>
        public static void Forget()
        {
            PlayerPrefs.DeleteKey(FullscreenKey);
            PlayerPrefs.DeleteKey(WidthKey);
            PlayerPrefs.DeleteKey(HeightKey);
            PlayerPrefs.DeleteKey(QualityKey);
            PlayerPrefs.Save();
        }

        private static string CurrentQuality()
        {
            string[] tiers = QualitySettings.names;
            int tier = QualitySettings.GetQualityLevel();
            return tier >= 0 && tier < tiers.Length ? tiers[tier] : string.Empty;
        }

        /// <summary>
        /// Adds a size to the list if the interface fits in it.
        /// </summary>
        private static void Offer(Vector2Int size, List<Vector2Int> into, HashSet<long> seen)
        {
            if (size.x >= Smallest.x && size.y >= Smallest.y)
            {
                Include(size, into, seen);
            }
        }

        /// <summary>
        /// Adds a size to the list whatever its size, if it is not already on it.
        /// </summary>
        private static void Include(Vector2Int size, List<Vector2Int> into, HashSet<long> seen)
        {
            long key = ((long)size.x << 32) | (uint)size.y;
            if (seen.Add(key))
            {
                into.Add(size);
            }
        }

        private static int Compare(Vector2Int first, Vector2Int second)
        {
            int byWidth = first.x.CompareTo(second.x);
            return byWidth != 0 ? byWidth : first.y.CompareTo(second.y);
        }
    }
}
