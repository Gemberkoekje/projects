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

        [SerializeField]
        [Tooltip("The player whose bunker and vehicle this HUD is showing.")]
        private PlayerVehicleDriver player;

        private readonly List<Image> rowPlates = new List<Image>();
        private readonly List<Text> rowNames = new List<Text>();
        private readonly List<Text> rowStates = new List<Text>();

        private RectTransform panels;
        private RectTransform bunkerPanel;
        private RectTransform statusPanel;
        private RectTransform objectivePanel;
        private RectTransform resultPanel;
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
        /// Builds the panel a player chooses their next vehicle on.
        /// </summary>
        private void BuildBunkerPanel()
        {
            const float width = 760.0f;
            const float rowHeight = 52.0f;
            const float margin = 20.0f;

            int rows = player == null ? 0 : player.Roster.Count;
            float height = margin + 40.0f + 10.0f + (rows * rowHeight) + 10.0f + 40.0f + margin;

            Image plate = HudPalette.Box("Bunker", panels, HudPalette.Panel);
            bunkerPanel = plate.rectTransform;
            bunkerPanel.anchorMin = new Vector2(0.5f, 0.5f);
            bunkerPanel.anchorMax = new Vector2(0.5f, 0.5f);
            bunkerPanel.pivot = new Vector2(0.5f, 0.5f);
            // Off to the left rather than dead centre. The camera is parked on the player's
            // own bunker while they choose, and a panel in the middle of the view would be
            // covering the building it is a panel about.
            bunkerPanel.anchoredPosition = new Vector2(-ReferenceWidth * 0.25f, 0.0f);
            bunkerPanel.sizeDelta = new Vector2(width, height);

            bunkerTitle = HudPalette.Label("Title", bunkerPanel, 30, TextAnchor.MiddleLeft);
            HudPalette.Place(
                bunkerTitle.rectTransform, margin, height - margin - 40.0f, width - (margin * 2.0f), 40.0f);

            rowPlates.Clear();
            rowNames.Clear();
            rowStates.Clear();

            for (int row = 0; row < rows; row++)
            {
                float bottom = height - margin - 50.0f - ((row + 1) * rowHeight);

                Image highlight = HudPalette.Box($"Row {row}", bunkerPanel, HudPalette.Highlight);
                HudPalette.Place(
                    highlight.rectTransform, margin * 0.5f, bottom, width - margin, rowHeight - 4.0f);
                rowPlates.Add(highlight);

                Text name = HudPalette.Label($"Row {row} Name", bunkerPanel, 28, TextAnchor.MiddleLeft);
                HudPalette.Place(name.rectTransform, margin + 12.0f, bottom, 420.0f, rowHeight - 4.0f);
                rowNames.Add(name);

                Text state = HudPalette.Label($"Row {row} State", bunkerPanel, 24, TextAnchor.MiddleRight);
                HudPalette.Place(
                    state.rectTransform, width - margin - 320.0f, bottom, 300.0f, rowHeight - 4.0f);
                rowStates.Add(state);
            }

            bunkerPrompt = HudPalette.Label("Prompt", bunkerPanel, 24, TextAnchor.MiddleCenter);
            bunkerPrompt.color = HudPalette.FadedInk;
            HudPalette.Place(bunkerPrompt.rectTransform, margin, margin, width - (margin * 2.0f), 36.0f);
        }

        /// <summary>
        /// Builds the strip a player drives by.
        /// </summary>
        private void BuildStatusPanel()
        {
            const float width = 560.0f;
            const float height = 214.0f;
            const float margin = 16.0f;

            Image plate = HudPalette.Box("Status", panels, HudPalette.Panel);
            statusPanel = plate.rectTransform;
            statusPanel.anchorMin = Vector2.zero;
            statusPanel.anchorMax = Vector2.zero;
            statusPanel.pivot = Vector2.zero;
            statusPanel.anchoredPosition = new Vector2(34.0f, 30.0f);
            statusPanel.sizeDelta = new Vector2(width, height);

            statusName = HudPalette.Label("Name", statusPanel, 28, TextAnchor.MiddleLeft);
            HudPalette.Place(statusName.rectTransform, margin, height - margin - 34.0f, 300.0f, 34.0f);

            statusNote = HudPalette.Label("Note", statusPanel, 22, TextAnchor.MiddleRight);
            statusNote.color = HudPalette.FadedInk;
            HudPalette.Place(
                statusNote.rectTransform, width - margin - 300.0f, height - margin - 34.0f, 300.0f, 34.0f);

            float row = width - (margin * 2.0f);
            armour = HudBar.Build(statusPanel, "Armour", HudPalette.Armour, margin, 122.0f, row, 32.0f);
            fuel = HudBar.Build(statusPanel, "Fuel", HudPalette.Fuel, margin, 86.0f, row, 32.0f);
            ammunition = HudBar.Build(
                statusPanel, "Rounds", HudPalette.Ammunition, margin, 50.0f, row, 32.0f);
            leaving = HudBar.Build(statusPanel, "Scuttle", HudPalette.Alarm, margin, 12.0f, row, 30.0f);
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

            Image plate = HudPalette.Box("Objective", panels, HudPalette.Panel);
            objectivePanel = plate.rectTransform;
            objectivePanel.anchorMin = new Vector2(0.5f, 1.0f);
            objectivePanel.anchorMax = new Vector2(0.5f, 1.0f);
            objectivePanel.pivot = new Vector2(0.5f, 1.0f);
            objectivePanel.anchoredPosition = new Vector2(0.0f, -18.0f);
            objectivePanel.sizeDelta = new Vector2(width, height);

            float row = width - (margin * 2.0f);
            attackLine = HudPalette.Label("Attack", objectivePanel, 24, TextAnchor.MiddleLeft);
            HudPalette.Place(attackLine.rectTransform, margin, height - margin - 30.0f, row, 30.0f);

            defenceLine = HudPalette.Label("Defence", objectivePanel, 24, TextAnchor.MiddleLeft);
            HudPalette.Place(defenceLine.rectTransform, margin, margin, row, 30.0f);
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

            Image plate = HudPalette.Box("Result", panels, HudPalette.Panel);
            resultPanel = plate.rectTransform;
            resultPanel.anchorMin = new Vector2(0.5f, 0.5f);
            resultPanel.anchorMax = new Vector2(0.5f, 0.5f);
            resultPanel.pivot = new Vector2(0.5f, 0.5f);
            resultPanel.anchoredPosition = Vector2.zero;
            resultPanel.sizeDelta = new Vector2(width, height);

            resultTitle = HudPalette.Label("Title", resultPanel, 72, TextAnchor.MiddleCenter);
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
            resultNote.text =
                $"{match.FlagTaken.ToString().ToUpperInvariant()} FLAG RETURNED TO THE BUNKER";
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

            for (int row = 0; row < rowNames.Count; row++)
            {
                VehicleController vehicle = row < player.Roster.Count ? player.Roster[row] : null;
                VehicleBay bay = player.BayFor(row);
                bool chosen = row == player.Selected;

                rowPlates[row].enabled = chosen;
                rowNames[row].color = chosen ? HudPalette.Ink : HudPalette.FadedInk;
                rowNames[row].text = chosen
                    ? $"> {NameOf(vehicle)}"
                    : $"  {NameOf(vehicle)}";

                rowStates[row].text = StateOf(bay);
                rowStates[row].color = ColorOf(bay, side);
            }

            bunkerPrompt.text = Prompt();
        }

        /// <summary>
        /// Brings the driving readouts up to date.
        /// </summary>
        private void RefreshStatus()
        {
            VehicleController vehicle = player.ActiveVehicle;
            Watch(vehicle);

            statusName.color = HudPalette.For(player.Team);
            statusName.text = NameOf(vehicle).ToUpperInvariant();
            statusNote.text = Note();

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
                    HudPalette.Level(HudPalette.Armour, watchedHull.Fraction));
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
                    HudPalette.Level(HudPalette.Fuel, watchedSupply.FuelFraction));

                ammunition.SetVisible(true);
                ammunition.Show(
                    watchedSupply.RoundsFraction,
                    $"{watchedSupply.Rounds}/{watchedSupply.RoundsCarried}",
                    HudPalette.Level(HudPalette.Ammunition, watchedSupply.RoundsFraction));
            }

            float held = player.RecallProgress;
            leaving.SetVisible(held > 0.0f);
            if (held > 0.0f)
            {
                leaving.Rename(player.IsHome ? "Stowing" : "Scuttle");
                leaving.Show(held, $"{Mathf.RoundToInt(held * 100.0f)}%", HudPalette.Alarm);
            }
        }

        /// <summary>
        /// Caches the components behind the three bars when the vehicle changes.
        /// </summary>
        /// <param name="vehicle">The vehicle now being driven.</param>
        /// <remarks>
        /// A HUD that looked its own readings up every frame would do six component searches
        /// a frame for two players, all of them answering the same question they answered
        /// last frame.
        /// </remarks>
        private void Watch(VehicleController vehicle)
        {
            if (watched == vehicle)
            {
                return;
            }

            watched = vehicle;
            watchedHull = vehicle == null ? null : vehicle.GetComponent<VehicleHealth>();
            watchedSupply = vehicle == null ? null : vehicle.GetComponent<VehicleSupply>();
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

            return player.CanDeploy
                ? $"{choose}  CHOOSE          {send}  DEPLOY"
                : $"{choose}  CHOOSE          STILL IN REPAIR";
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
            rowPlates.Clear();
            rowNames.Clear();
            rowStates.Clear();
        }
    }
}
