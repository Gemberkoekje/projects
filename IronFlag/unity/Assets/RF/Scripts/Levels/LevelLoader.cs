using UnityEngine;

namespace IronFlag.Levels
{
    /// <summary>
    /// Loads the map from its file when the game starts, and can be told to load another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scene on disk carries a <em>baked</em> copy of the map so that opening it shows
    /// something and the command-line still has something to photograph. This throws that
    /// copy away on the first frame and rebuilds from the file, every time, without
    /// comparing them. That is deliberate and it is the whole point of the milestone: the
    /// file is the map. Edit the JSON, press Play, and the change is there - no scene
    /// rebuild, no editor menu, no recompile.
    /// </para>
    /// <para>
    /// It is also the seam the in-game level editor will be built on. Editing a map is
    /// <see cref="Show"/> with a changed definition; saving it is
    /// <see cref="LevelFile.TryWrite"/> into <see cref="LevelLibrary.UserFolder"/>; playing
    /// it again is <see cref="Load"/>. None of that needs this class to grow.
    /// </para>
    /// <para>
    /// The map is built as a child of this object, so replacing a level is emptying one
    /// transform. This object therefore lives at the origin: level coordinates are absolute,
    /// and a loader somebody had dragged would silently move the whole world.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Level Loader")]
    public sealed class LevelLoader : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Name of the level file to load, without a folder or an extension.")]
        private string levelName = LevelLibrary.DefaultLevel;

        [SerializeField]
        [Tooltip("The prefabs and materials maps are built out of.")]
        private LevelCatalog catalog;

        /// <summary>The level that is loaded, or <c>null</c> before anything is.</summary>
        /// <remarks>
        /// Static because there is one map at a time and everything that wants to ask about
        /// it - a minimap, a level editor, a test - would otherwise have to be handed a
        /// reference through whatever happens to own it.
        /// </remarks>
        public static LevelDefinition Current { get; private set; }

        /// <summary>Name of the level file this loads.</summary>
        public string LevelName => levelName;

        /// <summary>The prefabs and materials maps are built out of.</summary>
        public LevelCatalog Catalog => catalog;

        /// <summary>
        /// Points the loader at a level file and the catalog to build it from.
        /// </summary>
        /// <param name="name">Level name, without a folder or an extension.</param>
        /// <param name="from">The prefabs and materials to build it out of.</param>
        /// <remarks>Called by the scene generator. Loading is a separate step.</remarks>
        public void Configure(string name, LevelCatalog from)
        {
            levelName = name;
            catalog = from;
        }

        /// <summary>
        /// Reads a level file and builds it, replacing whatever map is up.
        /// </summary>
        /// <param name="name">Level name, without a folder or an extension.</param>
        /// <returns><c>true</c> when the level loaded.</returns>
        /// <remarks>
        /// The file is read before anything is torn down, so a level that will not load
        /// leaves you standing in the one that did rather than in an empty world.
        /// </remarks>
        public bool Load(string name)
        {
            string path = LevelLibrary.PathFor(name);
            if (!LevelFile.TryRead(path, out LevelDefinition level, out string problem))
            {
                Debug.LogError($"{problem} The map you are on has been left alone.");
                return false;
            }

            levelName = name;
            Show(level);
            return true;
        }

        /// <summary>
        /// Builds a level, replacing whatever map is up.
        /// </summary>
        /// <param name="level">The map to build.</param>
        /// <remarks>
        /// <para>
        /// What a level editor calls after every change. The old map is <em>disabled</em>
        /// before it is destroyed, and that is not tidiness: <c>Destroy</c> is deferred to
        /// the end of the frame, so without it the outgoing bunkers, towers and flags are
        /// still on their roll-calls while the incoming ones register, and
        /// <see cref="IronFlag.Core.TeamBunker.For"/> can hand somebody a bunker that is
        /// about to stop existing. Disabling runs <c>OnDisable</c> now, which takes them off.
        /// </para>
        /// <para>
        /// Anything the level file complains about is logged rather than refused. A map with
        /// a tower in the sea is worth looking at; a map that will not load is not.
        /// </para>
        /// </remarks>
        public void Show(LevelDefinition level)
        {
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;

            string named = level == null ? "(no level)" : level.Name;
            foreach (string problem in LevelValidation.Problems(level))
            {
                Debug.LogWarning($"IronFlag: {named} - {problem}");
            }

            Clear();

            LevelBuilder.Build(level, catalog).transform.SetParent(transform, false);
            Current = level;
        }

        /// <summary>
        /// Takes down the map that is up, now rather than at the end of the frame.
        /// </summary>
        public void Clear()
        {
            for (int index = transform.childCount - 1; index >= 0; index--)
            {
                GameObject child = transform.GetChild(index).gameObject;
                child.SetActive(false);

                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

        /// <summary>
        /// Loads the map before anything else in the scene has started.
        /// </summary>
        /// <remarks>
        /// <c>Awake</c> rather than <c>Start</c>, because the bunkers this builds have to be
        /// on <see cref="IronFlag.Core.TeamBunker.For"/> by the time
        /// <see cref="IronFlag.Players.PlayerVehicleDriver"/> stows its roster in one - and
        /// that happens in <c>Start</c>, for exactly this reason.
        /// </remarks>
        private void Awake()
        {
            if (Application.isPlaying)
            {
                Load(levelName);
            }
        }
    }
}
