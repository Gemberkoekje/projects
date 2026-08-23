using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronFlag.Levels;
using IronFlag.UI;

namespace IronFlag.Editing
{
    /// <summary>
    /// The way back out of a playtest, and the only thing in the game scene that knows the
    /// editor exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this, playing a map you have just made is a one-way trip: the editor loads the
    /// game scene, and getting back means stopping and starting again. That turns the loop
    /// this whole feature is about - change something, drive on it, change it again - into
    /// something nobody does more than twice.
    /// </para>
    /// <para>
    /// It does nothing at all unless <see cref="LevelHandoff.FromEditor"/> says the editor is
    /// what sent the player here. A player who launched the game normally has no editor behind
    /// them, and a key that quietly took them to one would be a way to lose a match by
    /// mistake. So the component sits in the game scene permanently and switches itself off,
    /// which is cheaper and much harder to get wrong than adding it to the scene conditionally.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Playtest Return")]
    public sealed class PlaytestReturn : MonoBehaviour
    {
        /// <summary>Name of the generated child the notice hangs off.</summary>
        public const string NoticeNodeName = "Notice";

        private const float Width = 620.0f;
        private const float Height = 46.0f;

        private Text notice;

        /// <summary>Whether there is an editor to go back to.</summary>
        public static bool IsPlaytest => LevelHandoff.FromEditor;

        /// <summary>
        /// Goes back to the editor, with the map that was being played still open.
        /// </summary>
        /// <returns><c>true</c> when the editor was asked for.</returns>
        /// <remarks>
        /// Nothing is saved on the way out and nothing needs to be: the editor's Play button
        /// saved the map before it left, so the file the editor is about to re-open is the one
        /// that was just played. That is also why a playtest cannot lose work - the only way
        /// into one is through a save.
        /// </remarks>
        public bool BackToEditor()
        {
            if (!IsPlaytest)
            {
                return false;
            }

            LevelHandoff.Edit(LevelHandoff.Level);
            SceneManager.LoadScene(LevelScenes.Editor);
            return true;
        }

        /// <summary>
        /// Builds the line of text that says how to get back.
        /// </summary>
        /// <remarks>
        /// Top left, which is the one corner of a split screen that neither player's HUD uses:
        /// the roster and the gauges sit low in each half and the flag lines sit top centre.
        /// Its own canvas rather than a panel on somebody's HUD, because it belongs to the
        /// session rather than to either player - and a notice about the editor drawn on one
        /// player's instruments would be one only that player could see.
        /// </remarks>
        public void Build()
        {
            Clear();

            var host = new GameObject(
                NoticeNodeName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            host.transform.SetParent(transform, false);

            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;

            var scaler = host.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);
            scaler.matchWidthOrHeight = 0.5f;

            Image plate = EditorTheme.Plate("Plate", host.transform, HudPalette.Panel);
            RectTransform rect = plate.rectTransform;
            rect.anchorMin = new Vector2(0.0f, 1.0f);
            rect.anchorMax = new Vector2(0.0f, 1.0f);
            rect.pivot = new Vector2(0.0f, 1.0f);
            rect.anchoredPosition = new Vector2(18.0f, -18.0f);
            rect.sizeDelta = new Vector2(Width, Height);

            // Nothing here is clickable, so the plate must not take the mouse: aiming is a
            // pointer position on the keyboard scheme, and a strip that swallowed it would be
            // a corner of the map player one could not shoot at.
            plate.raycastTarget = false;

            notice = HudPalette.Label("Text", rect, 22, TextAnchor.MiddleLeft);
            notice.color = EditorTheme.Drawn;
            HudPalette.Place(notice.rectTransform, 14.0f, 0.0f, Width - 28.0f, Height);

            Refresh();
        }

        private void Awake()
        {
            if (!IsPlaytest)
            {
                enabled = false;
                return;
            }

            Build();
        }

        private void Update()
        {
            Keyboard keys = Keyboard.current;
            if (keys != null && keys.f1Key.wasPressedThisFrame)
            {
                BackToEditor();
            }
        }

        private void Refresh()
        {
            if (notice != null)
            {
                notice.text =
                    $"PLAYTESTING '{LevelHandoff.Level}'   ·   F1  BACK TO THE EDITOR";
            }
        }

        private void Clear()
        {
            Transform existing = transform.Find(NoticeNodeName);
            if (existing == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(existing.gameObject);
            }
            else
            {
                DestroyImmediate(existing.gameObject);
            }
        }
    }
}
