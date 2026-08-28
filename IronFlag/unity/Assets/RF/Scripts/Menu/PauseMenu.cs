using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronFlag.Editing;
using IronFlag.Levels;
using IronFlag.Objective;
using IronFlag.UI;

namespace IronFlag.Menu
{
    /// <summary>
    /// The way out of a match: Escape, once - a panel rather than a strip, with somewhere to
    /// stop and somewhere to go.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replaces the double-press strip this project shipped with the main menu: press
    /// Escape once, and here is that decision - keep playing, or leave - rather than a line
    /// of text that has to be read and pressed again inside four seconds. Pausing is the
    /// other half of that: a menu somebody has to read is a menu the match should not keep
    /// running underneath, so opening this sets <see cref="Time.timeScale"/> to zero and
    /// closing it - the CONTINUE button, or Escape again - sets it back.
    /// </para>
    /// <para>
    /// A finished match has nothing to continue, so the CONTINUE button comes down and only
    /// MAIN MENU is left. Which side won or lost is already on screen - both halves of
    /// <see cref="PlayerHud"/> put it there the moment <see cref="Match.IsFinished"/> - so
    /// this panel is deliberately neutral about the result rather than a second, competing
    /// place to read it.
    /// </para>
    /// <para>
    /// A sibling of <see cref="IronFlag.Editing.PlaytestReturn"/> rather than part of it.
    /// That component is the only thing in this scene that knows the editor exists, and it
    /// switches itself off outside a playtest; this one is about the menu and is always on.
    /// F1 still goes straight back to the editor during a playtest, paused or not - leaving
    /// that way does not need this panel open at all.
    /// </para>
    /// <para>
    /// This is also the first panel in this scene anybody clicks, so it is the first thing
    /// here that needs an event system and a raycasting canvas - see
    /// <see cref="IronFlag.Editor.Gameplay.VehicleSandboxScene"/>. Built out of
    /// <see cref="EditorTheme"/>, the same borrowed look <see cref="MainMenuController"/>
    /// already uses for exactly this reason: a menu read while sitting still is the same
    /// kind of thing whichever scene it is in.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Pause Menu")]
    public sealed class PauseMenu : MonoBehaviour
    {
        /// <summary>Name of the generated child the panel hangs off.</summary>
        public const string PanelNodeName = "Pause Panel";

        private const float PanelWidth = 460.0f;
        private const float PanelHeight = 258.0f;
        private const float ButtonWidth = 360.0f;
        private const float ButtonHeight = 56.0f;
        private const float ButtonGutter = 14.0f;
        private const float Margin = 24.0f;

        private RectTransform panel;
        private Text title;
        private EditorButton continueButton;
        private EditorButton mainMenuButton;

        /// <summary>Whether the panel is up and the match is paused because of it.</summary>
        public bool IsOpen => panel != null && panel.gameObject.activeSelf;

        /// <summary>
        /// Pauses the match and shows the panel.
        /// </summary>
        /// <remarks>
        /// Safe to call while already open - it just rereads <see cref="Match.IsFinished"/>
        /// and redraws, which is what lets a test call it directly without pressing a key.
        /// </remarks>
        public void Open()
        {
            if (panel == null)
            {
                return;
            }

            Time.timeScale = 0.0f;
            Refresh();
            panel.gameObject.SetActive(true);
        }

        /// <summary>
        /// Un-pauses the match and hides the panel.
        /// </summary>
        /// <remarks>What CONTINUE does, and what a second press of Escape does too.</remarks>
        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            Time.timeScale = 1.0f;
            panel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Leaves the match and goes back to the menu.
        /// </summary>
        /// <remarks>
        /// The handoff is cleared on the way out so the menu is not still holding the map
        /// that was being played - which would otherwise be the map a later scene opened
        /// without anybody choosing it. The time scale is put back here rather than left to
        /// <see cref="OnDisable"/> alone, so a test that calls this directly, without ever
        /// pausing first, still leaves the engine exactly as it found it.
        /// </remarks>
        public void BackToMenu()
        {
            Time.timeScale = 1.0f;
            LevelHandoff.Clear();
            SceneManager.LoadScene(LevelScenes.MainMenu);
        }

        /// <summary>
        /// Builds the panel, replacing any that was there before.
        /// </summary>
        public void Build()
        {
            Clear();

            var host = new GameObject(
                PanelNodeName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            host.transform.SetParent(transform, false);

            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above PlaytestReturn's and the old strip's notices, which is the highest this
            // scene draws - a paused match is meant to have exactly one thing on top.
            canvas.sortingOrder = 30;

            var scaler = host.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);
            scaler.matchWidthOrHeight = 0.5f;

            Image plate = EditorTheme.Plate("Plate", host.transform, HudPalette.Panel);
            panel = plate.rectTransform;
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            title = EditorTheme.Label("Title", panel, 34, TextAnchor.MiddleCenter);
            EditorTheme.Place(
                title.rectTransform, 0.0f, PanelHeight - Margin - 50.0f, PanelWidth, 50.0f);

            continueButton = EditorTheme.Button("Continue", panel, "CONTINUE", 24, Close);
            mainMenuButton = EditorTheme.Button("Main Menu", panel, "MAIN MENU", 24, BackToMenu);

            panel.gameObject.SetActive(false);
        }

        /// <remarks>
        /// Guarded on actually playing, because the scene generator adds this component with
        /// <c>AddComponent</c> in edit mode and <c>Awake</c> fires on that line - so without
        /// the guard every saved copy of the game scene would carry a built panel that only
        /// ever means something during a match.
        /// </remarks>
        private void Awake()
        {
            if (Application.isPlaying)
            {
                Build();
            }
        }

        /// <remarks>
        /// Belt and braces: leaving this scene any other way than CONTINUE or MAIN MENU - F1
        /// back to the editor while paused, a test tearing the scene down mid-pause - must
        /// not leave the engine paused for whatever loads next. <c>OnDisable</c> runs when
        /// this scene unloads no matter which door was used, which is what a single
        /// early-return in <see cref="Close"/> cannot promise on its own.
        /// </remarks>
        private void OnDisable() => Time.timeScale = 1.0f;

        private void Update()
        {
            Keyboard keys = Keyboard.current;
            if (keys == null || !keys.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        /// <summary>
        /// Brings the panel up to date with whether the match is still being played.
        /// </summary>
        private void Refresh()
        {
            bool finished = Match.IsFinished;
            title.text = finished ? "MATCH OVER" : "PAUSED";

            // Main menu keeps its slot at the foot of the panel either way; continue sits
            // above it and simply is not there once a match has nothing left to continue.
            continueButton.SetVisible(!finished);

            float bottom = Margin;
            PlaceButton(mainMenuButton, bottom);

            if (!finished)
            {
                bottom += ButtonHeight + ButtonGutter;
                PlaceButton(continueButton, bottom);
            }
        }

        private static void PlaceButton(EditorButton button, float bottom)
            => EditorTheme.Place(
                button.Rect, (PanelWidth - ButtonWidth) * 0.5f, bottom, ButtonWidth, ButtonHeight);

        private void Clear()
        {
            panel = null;
            title = null;
            continueButton = null;
            mainMenuButton = null;

            Transform existing = transform.Find(PanelNodeName);
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
