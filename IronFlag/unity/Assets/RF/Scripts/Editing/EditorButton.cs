using System;
using UnityEngine;
using UnityEngine.UI;
using IronFlag.Audio;

namespace IronFlag.Editing
{
    /// <summary>
    /// One button on the editor's panels: the control, the plate behind it and its caption,
    /// kept together so the thing that built it can still change how it looks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most of this editor's buttons are not commands but <em>choices</em> - which tool, which
    /// prop, which side - so the thing they most often have to do after being pressed is show
    /// that they are the one that is on. A bare <see cref="Button"/> reference cannot do that
    /// without another <c>GetComponent</c> at every call site, and a whole component per
    /// button to hold three fields would be four hundred objects on a palette.
    /// </para>
    /// <para>
    /// The same shape as <see cref="IronFlag.UI.HudBar"/> and for the same reason: a plain
    /// class holding the pieces of one generated widget, built by a factory that knows the
    /// look.
    /// </para>
    /// </remarks>
    public sealed class EditorButton
    {
        private readonly Button control;
        private readonly Image plate;
        private readonly Text caption;

        internal EditorButton(Button button, Image face, Text label)
        {
            control = button;
            plate = face;
            caption = label;
        }

        /// <summary>The rectangle this button occupies, for laying it out.</summary>
        public RectTransform Rect => plate.rectTransform;

        /// <summary>
        /// Shows or hides that this button is the chosen one.
        /// </summary>
        /// <param name="on">Whether it is the current choice.</param>
        public void SetChosen(bool on)
        {
            plate.color = on ? EditorTheme.ButtonChosen : EditorTheme.ButtonFace;
            caption.color = on ? EditorTheme.ChosenInk : EditorTheme.Ink;
        }

        /// <summary>
        /// Changes what the button says.
        /// </summary>
        /// <param name="words">The new caption.</param>
        public void SetText(string words) => caption.text = words;

        /// <summary>
        /// Turns the button on or off.
        /// </summary>
        /// <param name="on">Whether it can be pressed.</param>
        /// <remarks>
        /// A disabled button is faded rather than hidden. Undo that vanishes when there is
        /// nothing to undo is a panel whose buttons move about under the cursor.
        /// </remarks>
        public void SetEnabled(bool on)
        {
            control.interactable = on;
            caption.color = on ? EditorTheme.Ink : EditorTheme.DisabledInk;
        }

        /// <summary>
        /// Shows or hides the whole button.
        /// </summary>
        /// <param name="on">Whether it is drawn at all.</param>
        public void SetVisible(bool on) => plate.gameObject.SetActive(on);

        /// <summary>
        /// Replaces what pressing the button does.
        /// </summary>
        /// <param name="does">What to do when it is pressed.</param>
        /// <remarks>
        /// The palette rows are reused rather than rebuilt as the selection changes, so their
        /// listeners have to be replaceable - and adding one without removing the last is how
        /// a button ends up doing four things at once.
        /// </remarks>
        public void OnPress(Action does) => OnPress(does, SfxKind.UiClick);

        /// <summary>
        /// Replaces what pressing the button does, and what it sounds like.
        /// </summary>
        /// <param name="does">What to do when it is pressed.</param>
        /// <param name="sound">The noise the press makes.</param>
        /// <remarks>
        /// <para>
        /// <strong>This is the only place in the project a button is given a voice.</strong>
        /// Every button in the game - the editor's palettes, the main menu, the pause panel -
        /// is an <see cref="EditorButton"/> and reaches its action through here, so one line
        /// makes all of them audible and no call site has to remember to be. The alternative,
        /// a <c>Sfx.Play</c> at the top of forty listeners, is thirty-nine chances to forget.
        /// </para>
        /// <para>
        /// The sound plays before the action, which matters for the handful of buttons that
        /// load a scene. The <see cref="IronFlag.Audio.AudioDirector"/> belongs to the scene
        /// it is in, so PLAY and MAIN MENU have their click cut off partway through by the
        /// load; starting it first means what survives is the front of the click rather than
        /// nothing at all.
        /// </para>
        /// <para>
        /// A button with no action stays silent. The value shown between the two arrows of a
        /// stepper is a button so it can be built out of the same plate as its neighbours,
        /// and it should no more click than a caption should.
        /// </para>
        /// </remarks>
        public void OnPress(Action does, SfxKind sound)
        {
            control.onClick.RemoveAllListeners();
            if (does != null)
            {
                control.onClick.AddListener(() =>
                {
                    Sfx.Play(sound);
                    does();
                });
            }
        }
    }
}
