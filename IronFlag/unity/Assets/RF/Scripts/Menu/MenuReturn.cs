using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronFlag.Editing;
using IronFlag.Levels;
using IronFlag.UI;

namespace IronFlag.Menu
{
    /// <summary>
    /// The way out of a match: Escape, twice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this the menu is a screen the game shows once. Every session would reach it at
    /// boot, choose a map, and then have no way back short of killing the process - which
    /// makes a level list, a settings screen and a level editor button into things a player
    /// sees exactly once per launch.
    /// </para>
    /// <para>
    /// Escape rather than a button, because a match has no interface anybody clicks - the HUD
    /// is read, and the scene has no cursor. Escape twice rather than once, because the whole
    /// of what this key does is throw away a match in progress, and this project already
    /// answers that question the same way in two other places: the editor's New/Open/Revert
    /// guard and <see cref="LevelEditorSession.ConfirmQuit"/> both ask by arming themselves
    /// and waiting to be pressed again rather than by putting up a dialogue.
    /// </para>
    /// <para>
    /// It says nothing until the first press. A permanent "ESC to quit" strip would be on
    /// every screenshot this project takes of a match from now on, and Escape is the one key a
    /// player tries without being told - so the first press is the discovery and the line it
    /// puts up is the instruction.
    /// </para>
    /// <para>
    /// A sibling of <see cref="PlaytestReturn"/> rather than part of it. That component is the
    /// only thing in the game scene that knows the <em>editor</em> exists and it switches
    /// itself off outside a playtest; this one is about the menu and is always on. During a
    /// playtest both are up - F1 goes back to the editor, Escape goes to the menu - which is
    /// why this one's notice stands clear of the other's.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Menu Return")]
    public sealed class MenuReturn : MonoBehaviour
    {
        /// <summary>Name of the generated child the notice hangs off.</summary>
        public const string NoticeNodeName = "Leave Notice";

        /// <summary>How long a first press stays armed for, in seconds.</summary>
        /// <remarks>
        /// Long enough to read the line and decide, short enough that an Escape pressed by
        /// accident at the start of a match cannot be completed by another one four minutes
        /// later - which is the failure the editor's own guard grew a version stamp to avoid.
        /// </remarks>
        public const float ArmedSeconds = 4.0f;

        private const float Width = 620.0f;
        private const float Height = 46.0f;
        private const float Inset = 18.0f;

        private GameObject strip;
        private Text notice;
        private float armedUntil = -1.0f;

        /// <summary>Whether a first press is still waiting for its second.</summary>
        public bool IsArmed => armedUntil > 0.0f && Time.unscaledTime < armedUntil;

        /// <summary>
        /// Presses Escape.
        /// </summary>
        /// <returns><c>true</c> when the match was actually left.</returns>
        /// <remarks>
        /// Public and separate from the key so a test can press it without an input device -
        /// the same arrangement <see cref="PlaytestReturn.BackToEditor"/> uses.
        /// </remarks>
        public bool Press()
        {
            if (IsArmed)
            {
                BackToMenu();
                return true;
            }

            armedUntil = Time.unscaledTime + ArmedSeconds;
            Refresh();
            return false;
        }

        /// <summary>
        /// Leaves the match and goes back to the menu.
        /// </summary>
        /// <remarks>
        /// The handoff is cleared on the way out so the menu is not still holding the map that
        /// was being played - which would otherwise be the map a later scene opened without
        /// anybody choosing it.
        /// </remarks>
        public void BackToMenu()
        {
            LevelHandoff.Clear();
            SceneManager.LoadScene(LevelScenes.MainMenu);
        }

        /// <summary>
        /// Builds the line that says what a second press will do.
        /// </summary>
        /// <remarks>
        /// Top left, under <see cref="PlaytestReturn"/>'s strip when there is one. That corner
        /// is the one part of a split screen neither player's HUD uses, and this belongs to the
        /// session rather than to either player - so it is its own canvas rather than a panel
        /// on somebody's instruments.
        /// </remarks>
        public void Build()
        {
            Clear();

            var host = new GameObject(
                NoticeNodeName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            host.transform.SetParent(transform, false);

            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 21;

            var scaler = host.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);
            scaler.matchWidthOrHeight = 0.5f;

            Image plate = EditorTheme.Plate("Plate", host.transform, HudPalette.Panel);
            strip = plate.gameObject;
            RectTransform rect = plate.rectTransform;
            rect.anchorMin = new Vector2(0.0f, 1.0f);
            rect.anchorMax = new Vector2(0.0f, 1.0f);
            rect.pivot = new Vector2(0.0f, 1.0f);
            rect.anchoredPosition = new Vector2(Inset, -Inset - StripAbove());
            rect.sizeDelta = new Vector2(Width, Height);

            // Nothing here is clickable, and aiming on the keyboard scheme is a pointer
            // position - so a strip that took the mouse would be a corner of the map player
            // one could not shoot at.
            plate.raycastTarget = false;

            notice = HudPalette.Label("Text", rect, 22, TextAnchor.MiddleLeft);
            notice.color = EditorTheme.Unsaved;
            HudPalette.Place(notice.rectTransform, 14.0f, 0.0f, Width - 28.0f, Height);

            Refresh();
        }

        /// <summary>
        /// Returns how far down the notice starts, in canvas units.
        /// </summary>
        /// <returns>
        /// The height of <see cref="PlaytestReturn"/>'s strip during a playtest, and nothing at
        /// all otherwise.
        /// </returns>
        private static float StripAbove() => PlaytestReturn.IsPlaytest ? Height + 8.0f : 0.0f;

        /// <remarks>
        /// Guarded on actually playing, because the scene generator adds this component with
        /// <c>AddComponent</c> in edit mode and <c>Awake</c> fires on that line - so without
        /// the guard every saved copy of the game scene would carry a notice canvas that only
        /// ever means something during a match.
        /// </remarks>
        private void Awake()
        {
            if (Application.isPlaying)
            {
                Build();
            }
        }

        private void Update()
        {
            Keyboard keys = Keyboard.current;
            if (keys != null && keys.escapeKey.wasPressedThisFrame)
            {
                Press();
                return;
            }

            // Redrawn only on the frame the arming runs out, so the line disappears by itself
            // rather than staying up as an instruction that no longer applies.
            if (armedUntil > 0.0f && !IsArmed)
            {
                armedUntil = -1.0f;
                Refresh();
            }
        }

        private void Refresh()
        {
            if (notice == null)
            {
                return;
            }

            notice.text = IsArmed ? "ESC AGAIN  ·  LEAVE THE MATCH AND GO BACK TO THE MENU" : string.Empty;

            if (strip != null)
            {
                strip.SetActive(IsArmed);
            }
        }

        private void Clear()
        {
            strip = null;
            notice = null;

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
