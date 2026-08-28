using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Objective;
using IronFlag.Players;
using IronFlag.Supply;
using IronFlag.Vehicles;

namespace IronFlag.UI
{
    /// <summary>
    /// One player's half of the screen: the roster they choose from in the bunker, and the
    /// four readings they drive by once they are out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design document asks for a HUD panel per split-screen half showing the vehicles
    /// available and the state of their fuel and ammunition. Those turn out to be two
    /// different panels rather than one, because a player is never doing both: choosing a
    /// vehicle is a screen you look at, and driving one is a screen you glance at. Only one
    /// of them is on at a time and <see cref="PlayerVehicleDriver.AtTheBunker"/> says which.
    /// </para>
    /// <para>
    /// One canvas per player, in screen space against that player's own camera, so it fills
    /// exactly that player's viewport and nothing else - no arithmetic, no rectangles to
    /// keep in step with <see cref="SplitScreenLayout"/>, and no way for one player's panel
    /// to appear over the other's half.
    /// </para>
    /// <para>
    /// Nothing here is authored. <see cref="Build"/> generates the whole hierarchy from the
    /// player's roster, which is what lets a fifth vehicle appear on the panel without
    /// anybody opening a prefab, and what lets the command-line still show a real HUD rather
    /// than a mock-up of one.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Player Hud")]
    [RequireComponent(typeof(Canvas))]
    public sealed class PlayerHud : MonoBehaviour
    {
        /// <summary>Name of the generated child everything on the HUD hangs off.</summary>
        public const string PanelsNodeName = "Panels";

        /// <summary>Reference width the HUD is laid out in, in canvas units.</summary>
        public const float ReferenceWidth = 1920.0f;

        /// <summary>How far the select console sits in from the edges of the viewport.</summary>
        /// <remarks>
        /// Wider than the driving panel's inset, because this one runs the whole width and a
        /// strip that reached the corners would read as a letterbox bar rather than as a
        /// piece of equipment sitting in the picture.
        /// </remarks>
        private const float ConsoleInset = 30.0f;

        [SerializeField]
        [Tooltip("The player whose bunker and vehicle this HUD is showing.")]
        private PlayerVehicleDriver player;

        private readonly List<Image> rowPlates = new List<Image>();
        private readonly List<HudBracket> rowFrames = new List<HudBracket>();
        private readonly List<Text> rowNames = new List<Text>();
        private readonly List<Text> rowStates = new List<Text>();
        private readonly List<Text> rowLoads = new List<Text>();

        private RectTransform panels;
        private RectTransform bunkerPanel;
        private RectTransform statusPanel;
        private RectTransform objectivePanel;
        private RectTransform resultPanel;
        private HudBracket bunkerFrame;
        private HudBracket statusFrame;
        private HudBracket resultFrame;
        private HudGlyph attackMark;
        private HudGlyph defenceMark;
        private Text attackLine;
        private Text defenceLine;
        private Text resultTitle;
        private Text resultNote;
        private Text bunkerTitle;
        private Text bunkerPrompt;
        private Text statusName;
        private Text statusNote;
        private HudBar armour;
        private HudBar fuel;
        private HudBar ammunition;
        private HudBar leaving;

        private VehicleController watched;
        private VehicleHealth watchedHull;
        private VehicleSupply watchedSupply;

        /// <summary>The player this HUD belongs to.</summary>
        public PlayerVehicleDriver Player => player;

        /// <summary>Whether the panels have been generated yet.</summary>
        public bool IsBuilt => panels != null;

        /// <summary>The bunker roster panel, shown while the player is choosing.</summary>
        public RectTransform BunkerPanel => bunkerPanel;

        /// <summary>The driving readouts, shown while the player is out on the field.</summary>
        public RectTransform StatusPanel => statusPanel;

        /// <summary>
        /// The two lines about the flags, shown whatever else the player is doing.
        /// </summary>
        /// <remarks>
        /// The one panel that is on in the bunker as well as in the field, because where the
        /// flags are is the thing a player picks their next vehicle by: a stolen flag is a
        /// reason to take the tank out, and a flag on the ground with eight seconds left is
        /// a reason to take nothing out at all and wait.
        /// </remarks>
        public RectTransform ObjectivePanel => objectivePanel;

        /// <summary>The result, shown only once somebody has won.</summary>
        public RectTransform ResultPanel => resultPanel;

        /// <summary>
        /// Points this HUD at a player and at the camera it should draw in front of.
        /// </summary>
        /// <param name="driver">The player whose bunker and vehicle to show.</param>
        /// <param name="view">That player's camera.</param>
        /// <param name="slot">Zero-based player slot, which decides the HUD layer.</param>
        /// <remarks>
        /// Called by the sandbox scene builder. The canvas is set up here rather than in the
        /// builder because a HUD drawn against the wrong camera is a HUD in the other
        /// player's half of the screen, and that is not a scene-authoring decision.
        /// </remarks>
        public void Configure(PlayerVehicleDriver driver, Camera view, int slot)
        {
            player = driver;

            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = view;
            canvas.planeDistance = 1.0f;
            canvas.sortingOrder = slot;

            int layer = InterfaceLayers.LayerFor(slot);
            if (layer >= 0)
            {
                gameObject.layer = layer;
                ApplyLayer();
            }

            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, 1080.0f);
            // Matched on width alone: a split-screen half is a letterbox, and matching its
            // height would halve the type the moment the screen was shared.
            scaler.matchWidthOrHeight = 0.0f;
        }

        /// <summary>
        /// Generates the panels, replacing any that were there before.
        /// </summary>
        /// <remarks>
        /// Safe to call twice and safe to call outside play mode, which is what the
        /// command-line still does: the scene builder leaves an empty canvas behind, the
        /// still fills it in, and the saved scene never carries a frozen copy of a HUD whose
        /// numbers would be a lie.
        /// </remarks>
        public void Build()
        {
            Clear();

            var host = new GameObject(PanelsNodeName, typeof(RectTransform));
            host.transform.SetParent(transform, false);
            panels = host.GetComponent<RectTransform>();
            panels.anchorMin = Vector2.zero;
            panels.anchorMax = Vector2.one;
            panels.offsetMin = Vector2.zero;
            panels.offsetMax = Vector2.zero;

            BuildBunkerPanel();
            BuildStatusPanel();
            BuildObjectivePanel();
            BuildResultPanel();
            ApplyLayer();
            Refresh();
        }

        /// <summary>
        /// Puts everything on the HUD onto the canvas's own layer.
        /// </summary>
        /// <remarks>
        /// Generated objects arrive on the default layer, and one label left behind is one
        /// label hanging in the other player's view - see <see cref="InterfaceLayers"/> for why
        /// that happens at all, and why it is now also a label with the grade on it.
        /// </remarks>
        private void ApplyLayer() => InterfaceLayers.Paint(gameObject);

        /// <summary>
        /// Brings every reading on the HUD up to date with the player behind it.
        /// </summary>
        public void Refresh()
        {
            if (player == null || panels == null)
            {
                return;
            }

            RefreshObjective();

            bool over = Match.IsFinished;
            resultPanel.gameObject.SetActive(over);
            if (over)
            {
                // Everything else on the HUD is about a decision, and there are none left to
                // make. Both panels come down so the result is the only thing on the glass.
                bunkerPanel.gameObject.SetActive(false);
                statusPanel.gameObject.SetActive(false);
                RefreshResult();
                return;
            }

            // A wreck gets a moment before the cut - see PlayerVehicleDriver.OnReturned.
            // Neither panel is meant to show while the camera is holding on it, or the
            // roster would pop up over the very explosion the hold exists to let land.
            if (player.IsHoldingOnWreck)
            {
                bunkerPanel.gameObject.SetActive(false);
                statusPanel.gameObject.SetActive(false);
                return;
            }

            bool choosing = player.AtTheBunker;
            bunkerPanel.gameObject.SetActive(choosing);
            statusPanel.gameObject.SetActive(!choosing);

            if (choosing)
            {
                RefreshBunker();
            }
            else
            {
                RefreshStatus();
            }
        }

        private void Awake()
        {
            if (!IsBuilt)
            {
                Build();
            }
        }

        private void LateUpdate() => Refresh();

        /// <summary>
        /// Builds the console a player chooses their next vehicle on.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A strip across the bottom of the viewport rather than a list floating over the
        /// picture, and that is the whole point of it: the player is looking at their own
        /// base, four bays with the vehicles visibly in them, and a panel in the middle of
        /// that is a panel covering the thing it is about. It takes the same slot the driving
        /// gauges take, so the bottom of the screen is instruments in both halves of the loop
        /// and the middle of it is the world in both.
        /// </para>
        /// <para>
        /// <strong>The selection is not a highlighted row.</strong> Which vehicle is picked is
        /// said in the world - the bay lights up and the lift car comes to that deck - and the
        /// console only agrees with it, by putting corner marks round one cell and its name in
        /// full ink. A filled highlight bar here would be the menu this view exists to stop
        /// being.
        /// </para>
        /// <para>
        /// What each cell carries instead is the thing worth knowing while choosing: how much
        /// fuel and how many rounds that vehicle <em>holds</em>. Everything in a bunker is
        /// always full, so a gauge would read 100% four times over; a capacity is the
        /// difference between the four of them and is what the choice is actually about.
        /// </para>
        /// </remarks>
        private void BuildBunkerPanel()
        {
            const float height = 150.0f;
            const float margin = 14.0f;
            const float gap = 10.0f;
            const float header = 30.0f;

            float width = ReferenceWidth - (ConsoleInset * 2.0f);
            int cells = player == null ? 0 : player.Roster.Count;

            HudPlate plate = HudPalette.Plate("Bunker", panels, HudPalette.Panel);
            bunkerPanel = plate.rectTransform;
            bunkerPanel.anchorMin = new Vector2(0.5f, 0.0f);
            bunkerPanel.anchorMax = new Vector2(0.5f, 0.0f);
            bunkerPanel.pivot = new Vector2(0.5f, 0.0f);
            bunkerPanel.anchoredPosition = new Vector2(0.0f, ConsoleInset);
            bunkerPanel.sizeDelta = new Vector2(width, height);

            bunkerFrame = HudPalette.Bracket("Bunker Frame", bunkerPanel, HudPalette.FadedInk);

            bunkerTitle = HudPalette.Label("Title", bunkerPanel, 26, TextAnchor.MiddleLeft);
            HudPalette.Place(
                bunkerTitle.rectTransform, margin, height - margin - header, 520.0f, header);

            bunkerPrompt = HudPalette.Label("Prompt", bunkerPanel, 24, TextAnchor.MiddleRight);
            bunkerPrompt.color = HudPalette.FadedInk;
            HudPalette.Place(
                bunkerPrompt.rectTransform, width - margin - 900.0f, height - margin - header,
                900.0f, header);

            rowPlates.Clear();
            rowFrames.Clear();
            rowNames.Clear();
            rowStates.Clear();
            rowLoads.Clear();

            if (cells == 0)
            {
                return;
            }

            float cellHeight = height - (margin * 2.0f) - header - 6.0f;
            float cellWidth = (width - (margin * 2.0f) - (gap * (cells - 1))) / cells;

            for (int cell = 0; cell < cells; cell++)
            {
                float left = margin + (cell * (cellWidth + gap));

                Image slot = HudPalette.Box($"Cell {cell}", bunkerPanel, HudPalette.Track);
                HudPalette.Place(slot.rectTransform, left, margin, cellWidth, cellHeight);
                rowPlates.Add(slot);

                HudBracket frame = HudPalette.Bracket(
                    $"Cell {cell} Frame", slot.rectTransform, HudPalette.FadedInk);
                rowFrames.Add(frame);

                Text name = HudPalette.Label($"Cell {cell} Name", slot.rectTransform, 28, TextAnchor.MiddleLeft);
                HudPalette.Place(
                    name.rectTransform, 18.0f, cellHeight - 40.0f, cellWidth - 36.0f, 34.0f);
                rowNames.Add(name);

                Text state = HudPalette.Label($"Cell {cell} State", slot.rectTransform, 22, TextAnchor.MiddleLeft);
                HudPalette.Place(state.rectTransform, 18.0f, 8.0f, cellWidth - 36.0f, 28.0f);
                rowStates.Add(state);

                Text load = HudPalette.Label($"Cell {cell} Load", slot.rectTransform, 22, TextAnchor.MiddleRight);
                load.color = HudPalette.FadedInk;
                HudPalette.Place(load.rectTransform, 18.0f, 8.0f, cellWidth - 36.0f, 28.0f);
                rowLoads.Add(load);
            }
        }

        /// <summary>
        /// Builds the strip a player drives by.
        /// </summary>
        private void BuildStatusPanel()
        {
            const float width = 560.0f;
            const float height = 214.0f;
            const float margin = 16.0f;

            HudPlate plate = HudPalette.Plate("Status", panels, HudPalette.Panel);
            statusPanel = plate.rectTransform;
            statusPanel.anchorMin = Vector2.zero;
            statusPanel.anchorMax = Vector2.zero;
            statusPanel.pivot = Vector2.zero;
            statusPanel.anchoredPosition = new Vector2(34.0f, 30.0f);
            statusPanel.sizeDelta = new Vector2(width, height);

            statusFrame = HudPalette.Bracket("Status Frame", statusPanel, HudPalette.FadedInk);

            statusName = HudPalette.Label("Name", statusPanel, 28, TextAnchor.MiddleLeft);
            HudPalette.Place(statusName.rectTransform, margin, height - margin - 34.0f, 300.0f, 34.0f);

            statusNote = HudPalette.Label("Note", statusPanel, 22, TextAnchor.MiddleRight);
            statusNote.color = HudPalette.FadedInk;
            HudPalette.Place(
                statusNote.rectTransform, width - margin - 300.0f, height - margin - 34.0f, 300.0f, 34.0f);

            float row = width - (margin * 2.0f);
            armour = HudBar.Build(
                statusPanel, "Armour", HudGlyphKind.Armour, HudPalette.Armour,
                margin, 122.0f, row, 32.0f);
            fuel = HudBar.Build(
                statusPanel, "Fuel", HudGlyphKind.Fuel, HudPalette.Fuel,
                margin, 86.0f, row, 32.0f);
            ammunition = HudBar.Build(
                statusPanel, "Rounds", HudGlyphKind.Rounds, HudPalette.Ammunition,
                margin, 50.0f, row, 32.0f);

            // No mark on the fourth row, and that is a decision rather than an omission. This
            // bar means two different things - scuttling a vehicle in the field, stowing one
            // at home - and renames itself between them; a mark that had to change meaning
            // with the word beside it would be a mark that means nothing on its own, which is
            // the only thing a mark is for.
            leaving = HudBar.Build(
                statusPanel, "Scuttle", HudGlyphKind.None, HudPalette.Alarm,
                margin, 12.0f, row, 30.0f);
        }

        /// <summary>
        /// Builds the two lines saying where the two flags are.
        /// </summary>
        /// <remarks>
        /// Top centre, and small. It is the one thing on this HUD that is read at a glance
        /// mid-corner rather than looked at, so it goes where the eye already is - between
        /// the vehicle and the top of the screen - rather than down in the corner with the
        /// gauges, which are things a player checks deliberately.
        /// </remarks>
        private void BuildObjectivePanel()
        {
            const float width = 720.0f;
            const float height = 84.0f;
            const float margin = 14.0f;

            const float markWidth = 24.0f;
            const float markGutter = 8.0f;

            HudPlate plate = HudPalette.Plate("Objective", panels, HudPalette.Panel);
            objectivePanel = plate.rectTransform;
            objectivePanel.anchorMin = new Vector2(0.5f, 1.0f);
            objectivePanel.anchorMax = new Vector2(0.5f, 1.0f);
            objectivePanel.pivot = new Vector2(0.5f, 1.0f);
            objectivePanel.anchoredPosition = new Vector2(0.0f, -18.0f);
            objectivePanel.sizeDelta = new Vector2(width, height);

            // No corner marks on this one. They carry a side's accent - see HudBracket - and
            // this is the only panel on the HUD that is about both sides at once.
            float left = margin + markWidth + markGutter;
            float row = width - margin - left;
            float upper = height - margin - 30.0f;

            attackMark = HudPalette.Glyph(
                "Attack Mark", objectivePanel, HudGlyphKind.Flag, HudPalette.FadedInk);
            HudPalette.Place(attackMark.rectTransform, margin, upper, markWidth, 30.0f);

            attackLine = HudPalette.Label("Attack", objectivePanel, 24, TextAnchor.MiddleLeft);
            HudPalette.Place(attackLine.rectTransform, left, upper, row, 30.0f);

            defenceMark = HudPalette.Glyph(
                "Defence Mark", objectivePanel, HudGlyphKind.Flag, HudPalette.FadedInk);
            HudPalette.Place(defenceMark.rectTransform, margin, margin, markWidth, 30.0f);

            defenceLine = HudPalette.Label("Defence", objectivePanel, 24, TextAnchor.MiddleLeft);
            HudPalette.Place(defenceLine.rectTransform, left, margin, row, 30.0f);
        }

        /// <summary>
        /// Builds the panel that says who won.
        /// </summary>
        /// <remarks>
        /// In the player's own terms rather than the match's - <c>VICTORY</c> or <c>DEFEAT</c>,
        /// not the name of a side - because each half of a split screen belongs to one
        /// person, and a banner reading GREEN WINS makes both of them work out which one
        /// they are. What actually happened goes underneath, where it is the same sentence
        /// on both halves.
        /// </remarks>
        private void BuildResultPanel()
        {
            const float width = 820.0f;
            const float height = 190.0f;

            HudPlate plate = HudPalette.Plate("Result", panels, HudPalette.Panel);
            resultPanel = plate.rectTransform;
            resultPanel.anchorMin = new Vector2(0.5f, 0.5f);
            resultPanel.anchorMax = new Vector2(0.5f, 0.5f);
            resultPanel.pivot = new Vector2(0.5f, 0.5f);
            resultPanel.anchoredPosition = Vector2.zero;
            resultPanel.sizeDelta = new Vector2(width, height);

            resultFrame = HudPalette.Bracket("Result Frame", resultPanel, HudPalette.FadedInk);

            resultTitle = HudPalette.Headline("Title", resultPanel, 72, TextAnchor.MiddleCenter);
            HudPalette.Place(resultTitle.rectTransform, 0.0f, 74.0f, width, 92.0f);

            resultNote = HudPalette.Label("Note", resultPanel, 28, TextAnchor.MiddleCenter);
            resultNote.color = HudPalette.FadedInk;
            HudPalette.Place(resultNote.rectTransform, 0.0f, 26.0f, width, 40.0f);

            resultPanel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Brings the two flag lines up to date.
        /// </summary>
        private void RefreshObjective()
        {
            Team side = player.Team;
            Flag theirs = Flag.EnemyOf(side);
            Flag ours = Flag.Of(side);

            objectivePanel.gameObject.SetActive(theirs != null || ours != null);

            attackLine.text = AttackText(theirs);
            attackLine.color = AttackColour(theirs);
            defenceLine.text = DefenceText(ours);
            defenceLine.color = DefenceColour(ours);

            // The mark takes the line's colour rather than the flag's own. Which flag each
            // line is about is already said in words at the start of it; what the colour is
            // for is how that flag is doing, and a shape reads that faster than a sentence.
            attackMark.color = attackLine.color;
            defenceMark.color = defenceLine.color;
        }

        /// <summary>
        /// Brings the result up to date.
        /// </summary>
        private void RefreshResult()
        {
            Match match = Match.Current;
            if (match == null)
            {
                return;
            }

            bool won = match.Winner == player.Team;
            resultTitle.text = won ? "VICTORY" : "DEFEAT";
            resultTitle.color = won ? HudPalette.For(match.Winner) : HudPalette.FadedInk;
            resultFrame.color = resultTitle.color;
            resultNote.text = ResultNote(match);
        }

        /// <summary>
        /// Returns the line under VICTORY or DEFEAT saying what actually happened.
        /// </summary>
        /// <param name="match">The finished match.</param>
        /// <returns>One line, naming the side it happened to.</returns>
        /// <remarks>
        /// Named for the loser in both cases, which is the same choice
        /// <see cref="Match.Beaten"/> makes: the winner is already on the line above in
        /// letters an inch high, and what the player in front of this panel does not know is
        /// which of the two ways it ended.
        /// </remarks>
        private static string ResultNote(Match match)
        {
            string side = match.Beaten.ToString().ToUpperInvariant();

            switch (match.Outcome)
            {
                case MatchOutcome.FlagCaptured:
                    return $"{side} FLAG RETURNED TO THE BUNKER";
                case MatchOutcome.OutOfJeeps:
                    return $"{side} HAS NO JEEPS LEFT";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Returns the line about the flag this player is trying to take.
        /// </summary>
        /// <param name="theirs">The enemy flag, or null when the scene has none.</param>
        /// <returns>One line, in the words of the player reading it.</returns>
        /// <remarks>
        /// A flag still inside an intact tower is reported as sealed rather than as being
        /// somewhere, which is the HUD's half of the decoy: the panel knows perfectly well
        /// which of the two pyramids it is on, and must not say. It does say what to do
        /// about it, because "break a tower" is the one thing about this game that nothing
        /// else on screen can tell you - a tower under fire looks the same as a tower that
        /// cannot be hurt.
        /// </remarks>
        private string AttackText(Flag theirs)
        {
            if (theirs == null)
            {
                return string.Empty;
            }

            switch (theirs.State)
            {
                case FlagState.Carried:
                    return theirs.Carrier == player.ActiveVehicle
                        ? "THEIR FLAG    ON YOUR MAST - GET HOME"
                        : "THEIR FLAG    TAKEN";

                case FlagState.Dropped:
                    return "THEIR FLAG    ON THE GROUND - "
                        + $"{Mathf.CeilToInt(theirs.ReturnCountdown)}s";

                case FlagState.Captured:
                    return "THEIR FLAG    CAPTURED";

                default:
                    return theirs.IsVisible
                        ? "THEIR FLAG    ON ITS TOWER"
                        : "THEIR FLAG    SEALED - BREAK A TOWER";
            }
        }

        /// <summary>
        /// Returns the line about the flag this player is defending.
        /// </summary>
        /// <param name="ours">This player's own flag, or null when the scene has none.</param>
        /// <returns>One line, in the words of the player reading it.</returns>
        private static string DefenceText(Flag ours)
        {
            if (ours == null)
            {
                return string.Empty;
            }

            switch (ours.State)
            {
                case FlagState.Carried:
                    return "YOUR FLAG     STOLEN";

                case FlagState.Dropped:
                    return "YOUR FLAG     ON THE GROUND - "
                        + $"{Mathf.CeilToInt(ours.ReturnCountdown)}s";

                case FlagState.Captured:
                    return "YOUR FLAG     LOST";

                default:
                    // Visible means the tower it is on has been broken open, which is the
                    // defender's warning that somebody has been shelling their base and now
                    // knows which pyramid to send a jeep to.
                    return ours.IsVisible
                        ? "YOUR FLAG     TOWER BREACHED"
                        : "YOUR FLAG     ON ITS TOWER";
            }
        }

        /// <summary>
        /// Returns the colour the enemy flag line is drawn in.
        /// </summary>
        /// <param name="theirs">The enemy flag, or null when the scene has none.</param>
        /// <returns>A colour saying how the raid is going.</returns>
        private Color AttackColour(Flag theirs)
        {
            if (theirs == null)
            {
                return HudPalette.FadedInk;
            }

            switch (theirs.State)
            {
                case FlagState.Carried:
                case FlagState.Captured:
                    return HudPalette.Good;
                case FlagState.Dropped:
                    return HudPalette.Warning;
                default:
                    return theirs.IsVisible ? HudPalette.Ink : HudPalette.FadedInk;
            }
        }

        /// <summary>
        /// Returns the colour this side's own flag line is drawn in.
        /// </summary>
        /// <param name="ours">This player's own flag, or null when the scene has none.</param>
        /// <returns>A colour saying how the defence is going.</returns>
        private static Color DefenceColour(Flag ours)
        {
            if (ours == null)
            {
                return HudPalette.FadedInk;
            }

            switch (ours.State)
            {
                case FlagState.Carried:
                case FlagState.Captured:
                    return HudPalette.Alarm;
                case FlagState.Dropped:
                    return HudPalette.Warning;
                default:
                    // A breached tower is not an emergency yet - nobody has the flag - but
                    // it is the last warning before one is, and it wants reading as such.
                    return ours.IsVisible ? HudPalette.Warning : HudPalette.Good;
            }
        }

        /// <summary>
        /// Brings the bunker roster up to date: what is available, and what is selected.
        /// </summary>
        private void RefreshBunker()
        {
            Team side = player.Team;
            bunkerTitle.color = HudPalette.For(side);
            bunkerTitle.text = $"{side.ToString().ToUpperInvariant()} BUNKER";
            bunkerFrame.color = bunkerTitle.color;

            for (int row = 0; row < rowNames.Count; row++)
            {
                VehicleController vehicle = row < player.Roster.Count ? player.Roster[row] : null;
                VehicleBay bay = player.BayFor(row);
                bool chosen = row == player.Selected;

                // How many are left is shown against the name rather than in the state
                // column, because it is a fact about the vehicle rather than about what it
                // is doing - and the state column already has a number in it while a wreck
                // is being repaired.
                int left = player.RemainingOf(row);
                bool counted = vehicle != null && left != int.MaxValue;
                bool gone = counted && left <= 0;

                // The corner marks are the whole of the selection on this panel. They take
                // the side's colour on the chosen cell and go out on the rest, which is the
                // same thing the marks round every other panel in this game are saying:
                // this region is the one being watched.
                rowFrames[row].enabled = chosen;
                rowFrames[row].color = bunkerTitle.color;

                rowNames[row].color = chosen ? HudPalette.Ink : HudPalette.FadedInk;
                rowNames[row].text = $"{NameOf(vehicle).ToUpperInvariant()}{Stock(counted, left)}";

                rowStates[row].text = gone ? "NONE LEFT" : StateOf(bay);
                rowStates[row].color = gone ? HudPalette.Alarm : ColorOf(bay, side);
                rowLoads[row].text = Load(vehicle);
            }

            bunkerPrompt.text = Prompt();
        }

        /// <summary>
        /// Returns what one vehicle carries, as it reads on the console.
        /// </summary>
        /// <param name="vehicle">The roster entry.</param>
        /// <returns>Its fuel and its rounds, or nothing when nothing is counting either.</returns>
        /// <remarks>
        /// Capacities rather than levels. Everything waiting in a bunker is full - the bunker
        /// fills both pools the instant a vehicle is inside it - so a gauge here would read
        /// the same on all four. What differs is what each one <em>holds</em>, and that is
        /// most of what the choice is between: seventy seconds of fuel is the price of the
        /// helicopter's gun.
        /// </remarks>
        private static string Load(VehicleController vehicle)
        {
            var supply = vehicle == null ? null : vehicle.GetComponent<VehicleSupply>();
            if (supply == null || (supply.FuelCapacity <= 0.0f && supply.RoundsCarried <= 0))
            {
                return string.Empty;
            }

            return $"{Mathf.RoundToInt(supply.FuelCapacity)}s   {supply.RoundsCarried} RDS";
        }

        /// <summary>
        /// Brings the driving readouts up to date.
        /// </summary>
        private void RefreshStatus()
        {
            VehicleController vehicle = player.ActiveVehicle;
            bool swapped = Watch(vehicle);

            statusName.color = HudPalette.For(player.Team);
            statusName.text = NameOf(vehicle).ToUpperInvariant();
            statusNote.text = Note();
            statusFrame.color = statusName.color;

            if (watchedHull == null)
            {
                armour.SetVisible(false);
            }
            else
            {
                armour.SetVisible(true);
                armour.Show(
                    watchedHull.Fraction,
                    $"{Mathf.CeilToInt(watchedHull.HitPoints)}/{Mathf.RoundToInt(watchedHull.MaxHitPoints)}",
                    HudPalette.Armour);
            }

            if (watchedSupply == null)
            {
                fuel.SetVisible(false);
                ammunition.SetVisible(false);
            }
            else
            {
                fuel.SetVisible(true);
                fuel.Show(
                    watchedSupply.FuelFraction,
                    $"{Mathf.CeilToInt(watchedSupply.Fuel)}s",
                    HudPalette.Fuel);

                ammunition.SetVisible(true);
                ammunition.Show(
                    watchedSupply.RoundsFraction,
                    $"{watchedSupply.Rounds}/{watchedSupply.RoundsCarried}",
                    HudPalette.Ammunition);
            }

            float held = player.RecallProgress;
            leaving.SetVisible(held > 0.0f);
            if (held > 0.0f)
            {
                leaving.Rename(player.IsHome ? "Stowing" : "Scuttle");
                leaving.ShowProgress(held, $"{Mathf.RoundToInt(held * 100.0f)}%", HudPalette.Alarm);
            }

            // After the readings rather than before them, so a bar moves towards the number it
            // has just been given rather than the one it was given last frame - and jumps
            // straight to it on the frame the vehicle underneath it changed.
            Settle(armour, swapped, Time.deltaTime);
            Settle(fuel, swapped, Time.deltaTime);
            Settle(ammunition, swapped, Time.deltaTime);
        }

        /// <summary>
        /// Moves one bar on by a frame, or puts it straight where it belongs.
        /// </summary>
        /// <param name="bar">The bar to move.</param>
        /// <param name="swapped">Whether the vehicle underneath it has just changed.</param>
        /// <param name="deltaTime">Seconds since the last frame, ignored when it swapped.</param>
        private static void Settle(HudBar bar, bool swapped, float deltaTime)
        {
            if (swapped)
            {
                bar.Jump();
            }
            else
            {
                bar.Advance(deltaTime);
            }
        }

        /// <summary>
        /// Caches the components behind the three bars when the vehicle changes.
        /// </summary>
        /// <param name="vehicle">The vehicle now being driven.</param>
        /// <remarks>
        /// <para>
        /// A HUD that looked its own readings up every frame would do six component searches
        /// a frame for two players, all of them answering the same question they answered
        /// last frame.
        /// </para>
        /// <para>
        /// It is also the moment the gauges have to stop easing and jump. Every bar on this
        /// strip is about the vehicle being driven, so the one frame where that changes is the
        /// one frame where sliding from the old reading to the new one would be drawing a
        /// tank's armour on a jeep's gauge. See <see cref="HudBar.Jump"/>.
        /// </para>
        /// </remarks>
        private bool Watch(VehicleController vehicle)
        {
            if (watched == vehicle)
            {
                return false;
            }

            watched = vehicle;
            watchedHull = vehicle == null ? null : vehicle.GetComponent<VehicleHealth>();
            watchedSupply = vehicle == null ? null : vehicle.GetComponent<VehicleSupply>();
            return true;
        }

        /// <summary>
        /// Returns what one roster entry is doing, in a word.
        /// </summary>
        /// <param name="bay">The bay that entry waits in.</param>
        /// <returns>Its state, or how long until it has one.</returns>
        private static string StateOf(VehicleBay bay)
        {
            if (bay == null)
            {
                return "READY";
            }

            if (bay.IsRepairing)
            {
                return $"REPAIRING {bay.RepairCountdown:0.0}";
            }

            if (bay.IsDeploying)
            {
                return "DEPLOYING";
            }

            return bay.IsOnField ? "IN THE FIELD" : "READY";
        }

        private static Color ColorOf(VehicleBay bay, Team side)
        {
            if (bay == null)
            {
                return HudPalette.Good;
            }

            if (bay.IsRepairing)
            {
                return HudPalette.Warning;
            }

            return bay.IsDeploying ? HudPalette.For(side) : HudPalette.Good;
        }

        private static string NameOf(VehicleController vehicle)
            => vehicle == null ? "-" : VehicleNames.For(vehicle.Kind);

        /// <summary>
        /// Returns how many of one vehicle are left, as it reads on the roster.
        /// </summary>
        /// <param name="counted">Whether anything is limiting this vehicle at all.</param>
        /// <param name="left">How many are left.</param>
        /// <returns>A count to put after the name, or nothing when none is being kept.</returns>
        /// <remarks>
        /// Blank rather than a large number when nothing limits it. A sandbox with no
        /// reserve in it is not a side with a million jeeps; it is a side nobody is counting,
        /// and a panel that said so would be reporting a rule that is not being played.
        /// </remarks>
        private static string Stock(bool counted, int left)
            => counted ? $"  ×{left}" : string.Empty;

        /// <summary>
        /// Returns the line under the roster telling the player which buttons do what.
        /// </summary>
        /// <returns>A prompt in the words of whatever they are holding.</returns>
        /// <remarks>
        /// Named after the controls rather than the actions - "F" and not "the deploy key" -
        /// because the two players are on different hardware and each has to be told about
        /// their own. A player with no device at all is told that, which is the only
        /// explanation they would otherwise get for a panel that ignores them.
        /// </remarks>
        private string Prompt()
        {
            if (player.IsDeploying)
            {
                return "LEAVING THE BUNKER";
            }

            // A player holding nothing is shown the keyboard, which is what seat one gets
            // by default. The prompt is a teaching aid rather than a diagnostic: a player
            // with no device finds that out by pressing something, which M2 made a
            // deliberate behaviour rather than an error.
            bool pad = player.Controls != null && player.Controls.Scheme == ControlScheme.Gamepad;
            string choose = pad ? "LB / RB" : "Q / E";
            string send = pad ? "X" : "F";

            if (player.CanDeploy)
            {
                return $"{choose}  CHOOSE          {send}  DEPLOY";
            }

            // Two different reasons a vehicle cannot leave, and they want opposite things
            // from the player: one is worth waiting out and the other never comes back.
            return player.HasOneLeft(player.Selected)
                ? $"{choose}  CHOOSE          STILL IN REPAIR"
                : $"{choose}  CHOOSE          NONE LEFT";
        }

        /// <summary>
        /// Returns the one thing worth saying about the vehicle being driven.
        /// </summary>
        /// <returns>A note, or an empty string when there is nothing to report.</returns>
        /// <remarks>
        /// Deliberately one line and deliberately ordered. A stranded vehicle sitting on a
        /// depot is being told the interesting half - that it is filling up - and a vehicle
        /// that is merely somewhere useful is told nothing at all, because a HUD that always
        /// has something to say is one nobody reads when it finally does.
        /// </remarks>
        private string Note()
        {
            // First, because it is the only line here that answers a question the player is
            // asking right now: they have driven onto a flag in something that is not a jeep
            // and nothing has happened.
            Flag theirs = Flag.EnemyOf(player.Team);
            if (watched != null
                && !FlagRules.CanCarry(watched.Kind)
                && theirs != null
                && theirs.IsWithinReach(watched.transform.position))
            {
                return "ONLY THE JEEP CARRIES IT";
            }

            // Second, and the only line here about something that will not come back. The
            // roster panel counts the reserve and this screen does not, so a pilot who is
            // driving the last of something would otherwise find out by losing it.
            if (watched != null
                && TeamReserve.LeftFor(player.Team, watched.Kind) == 1)
            {
                return $"YOUR LAST {VehicleNames.For(watched.Kind).ToUpperInvariant()}";
            }

            if (watchedSupply != null && watchedSupply.Serving != null)
            {
                return watchedSupply.Serving.Team == Team.None ? "AT A DEPOT" : "AT THE BUNKER";
            }

            if (watchedSupply != null && watchedSupply.IsStranded)
            {
                return "OUT OF FUEL";
            }

            if (watchedSupply != null && watchedSupply.IsOutOfAmmunition)
            {
                return "OUT OF AMMUNITION";
            }

            return string.Empty;
        }

        /// <summary>
        /// Removes any panels already built, so building twice is not building twice over.
        /// </summary>
        private void Clear()
        {
            Transform existing = transform.Find(PanelsNodeName);
            if (existing != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(existing.gameObject);
                }
                else
                {
                    DestroyImmediate(existing.gameObject);
                }
            }

            panels = null;
            bunkerPanel = null;
            statusPanel = null;
            objectivePanel = null;
            resultPanel = null;
            bunkerFrame = null;
            statusFrame = null;
            resultFrame = null;
            attackMark = null;
            defenceMark = null;
            rowPlates.Clear();
            rowFrames.Clear();
            rowNames.Clear();
            rowStates.Clear();
            rowLoads.Clear();
        }
    }
}
