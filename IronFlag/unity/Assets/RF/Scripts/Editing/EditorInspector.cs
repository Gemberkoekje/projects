using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using IronFlag.Audio;
using IronFlag.Core;
using IronFlag.Levels;
using IronFlag.Vehicles;

namespace IronFlag.Editing
{
    /// <summary>
    /// The panel that shows the numbers behind whatever is selected, and lets them be typed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mouse is for where a thing roughly goes and this is for where it exactly goes.
    /// Both matter: a tree is dragged, and a coastline that has to meet another one at
    /// <c>z = -13</c> is typed. The shipped map's channel is 13 m wide at the bridgeheads
    /// because a test computes that number from the jeep's real top speed and the real bank
    /// height - it is not a number anybody was ever going to find by dragging.
    /// </para>
    /// <para>
    /// With nothing selected it shows the <em>map</em>: its name, what it is trying to be, and
    /// how big the world is. A level file's description is the only place a map can explain
    /// itself, because JSON has no comments, so an editor with nowhere to type one would
    /// quietly guarantee that every map made in it was undocumented.
    /// </para>
    /// <para>
    /// Every widget is built once and rebound as the selection changes, never rebuilt.
    /// Rebuilding would destroy the field the player is typing in at the moment their edit
    /// lands, which is the one frame it must not happen on.
    /// </para>
    /// </remarks>
    public sealed class EditorInspector
    {
        /// <summary>How many labelled fields the panel can show at once.</summary>
        /// <remarks>
        /// The map itself is now the longest of these, not a selected prop: a title, a
        /// description, three numbers about the world and one per vehicle in the reserve.
        /// That is nine, and the tenth is the room a new field would want. A supplying
        /// structure - the longest selection - still only reaches seven.
        /// </remarks>
        public const int RowCount = 10;

        /// <summary>How many stacked action buttons the panel can show at once.</summary>
        public const int ActionCount = 4;

        private const float Margin = 12.0f;
        private const float RowHeight = 40.0f;
        private const float FirstRowTop = 82.0f;
        private const float CaptionWidth = 116.0f;
        private const float ActionHeight = 38.0f;
        private const float Gutter = 6.0f;

        private readonly LevelEditorSession session;
        private readonly Text title;
        private readonly Text subtitle;
        private readonly List<Text> captions = new List<Text>();
        private readonly List<InputField> fields = new List<InputField>();
        private readonly List<EditorButton> sides = new List<EditorButton>();
        private readonly List<EditorButton> actions = new List<EditorButton>();

        private int usedRows;
        private int usedActions;

        private EditorInspector(LevelEditorSession editor, Text heading, Text note)
        {
            session = editor;
            title = heading;
            subtitle = note;
        }

        /// <summary>
        /// Builds the inspector into a panel.
        /// </summary>
        /// <param name="host">The panel to build it into.</param>
        /// <param name="editor">The editor whose selection it shows.</param>
        /// <param name="width">Width of the panel, in canvas units.</param>
        /// <returns>The inspector, already showing whatever is selected.</returns>
        public static EditorInspector Build(
            RectTransform host, LevelEditorSession editor, float width)
        {
            float inner = width - (Margin * 2.0f);

            Text heading = EditorTheme.Label("Title", host, 26, TextAnchor.MiddleLeft);
            Hang(heading.rectTransform, Margin, -Margin, inner, 32.0f);

            Text note = EditorTheme.Label("Subtitle", host, 19, TextAnchor.MiddleLeft);
            note.color = EditorTheme.FadedInk;
            Hang(note.rectTransform, Margin, -Margin - 34.0f, inner, 24.0f);

            var inspector = new EditorInspector(editor, heading, note);

            for (int row = 0; row < RowCount; row++)
            {
                float top = -FirstRowTop - (row * RowHeight);

                Text caption = EditorTheme.Label($"Caption {row}", host, 19, TextAnchor.MiddleLeft);
                caption.color = EditorTheme.FadedInk;
                Hang(caption.rectTransform, Margin, top, CaptionWidth, RowHeight - Gutter);
                inspector.captions.Add(caption);

                InputField field = EditorTheme.Field($"Field {row}", host, 20, null);
                Hang(
                    (RectTransform)field.transform,
                    Margin + CaptionWidth,
                    top,
                    inner - CaptionWidth,
                    RowHeight - Gutter);
                inspector.fields.Add(field);
            }

            // The two side buttons share the first row, which is where the side of a bunker
            // or a tower is always shown. Fixed rather than laid out on demand, so nothing in
            // this panel moves while it is being used.
            float half = (inner - CaptionWidth - Gutter) * 0.5f;
            IReadOnlyList<Team> playable = LevelEditorSession.PlayableSides();
            for (int slot = 0; slot < playable.Count; slot++)
            {
                EditorButton button = EditorTheme.Button(
                    $"Side {slot}", host, playable[slot].ToString().ToUpperInvariant(), 19, null);
                Hang(
                    button.Rect,
                    Margin + CaptionWidth + (slot * (half + Gutter)),
                    -FirstRowTop,
                    half,
                    RowHeight - Gutter);
                inspector.sides.Add(button);
            }

            for (int slot = 0; slot < ActionCount; slot++)
            {
                EditorButton button = EditorTheme.Button(
                    $"Action {slot}", host, string.Empty, 19, null);
                Hang(
                    button.Rect,
                    Margin,
                    -FirstRowTop - (RowCount * RowHeight) - Gutter
                        - (slot * (ActionHeight + Gutter)),
                    inner,
                    ActionHeight);
                inspector.actions.Add(button);
            }

            inspector.Refresh();
            return inspector;
        }

        /// <summary>
        /// Brings the panel up to date with what is selected.
        /// </summary>
        public void Refresh()
        {
            usedRows = 0;
            usedActions = 0;
            title.color = EditorTheme.Ink;

            foreach (EditorButton button in sides)
            {
                button.SetVisible(false);
            }

            LevelDefinition level = session.Level;
            EditSelection picked = session.Selection;

            if (level == null)
            {
                title.text = "NO MAP";
                subtitle.text = string.Empty;
            }
            else if (!LevelEdits.Holds(level, picked))
            {
                ShowMap(level);
            }
            else
            {
                ShowSelection(level, picked);
            }

            for (int row = usedRows; row < fields.Count; row++)
            {
                captions[row].enabled = false;
                fields[row].gameObject.SetActive(false);
            }

            for (int slot = usedActions; slot < actions.Count; slot++)
            {
                actions[slot].SetVisible(false);
            }
        }

        /// <summary>
        /// Shows the map itself, which is what nothing being selected means.
        /// </summary>
        private void ShowMap(LevelDefinition level)
        {
            title.text = "THE MAP";
            subtitle.text = $"{level.Land.Length} land · {level.Towers.Length} towers · "
                + $"{level.Structures.Length} props";

            Row("Title", level.Name, typed => Set(() => level.Name = typed, "Renamed the map."));
            Row(
                "About",
                level.Description,
                typed => Set(() => level.Description = typed, "Described the map."));

            LevelBounds bounds = level.Bounds;
            if (bounds == null)
            {
                return;
            }

            Number(
                "Half extent",
                bounds.HalfExtent,
                value => Set(
                    () => bounds.HalfExtent = Mathf.Max(1.0f, value), "Resized the world."));
            Number(
                "Water depth",
                bounds.WaterDepth,
                value => Set(
                    () => bounds.WaterDepth = Mathf.Max(0.05f, value), "Changed the bank height."));
            Number(
                "Sea thickness",
                bounds.SeaThickness,
                value => Set(
                    () => bounds.SeaThickness = Mathf.Max(0.1f, value), "Changed the sea slab."));

            ShowReserve(level);
        }

        /// <summary>
        /// Shows how many of each vehicle this map gives a side.
        /// </summary>
        /// <param name="level">The map being edited.</param>
        /// <remarks>
        /// <para>
        /// A loop over <see cref="VehicleRoster.Kinds"/> rather than four written-out rows,
        /// so that a fifth vehicle appears on this panel without anybody opening this file -
        /// the same bet <see cref="LevelReserve.Set"/> is there to make good on.
        /// </para>
        /// <para>
        /// On the map rather than on either bunker, which is the level format's own choice:
        /// one reserve for the level means neither side can be given more than the other by
        /// an editor slip nobody would see.
        /// </para>
        /// </remarks>
        private void ShowReserve(LevelDefinition level)
        {
            if (level.Reserve == null)
            {
                // Never actually null in practice - JsonUtility keeps the field initializer
                // for a file written before version 4 - but Set() rather than a bare
                // assignment in case that ever stops being true, so a repair still marks the
                // session dirty instead of silently mutating the model underneath it.
                Set(() => level.Reserve = new LevelReserve(), "Filled in the default vehicle reserve.");
            }

            LevelReserve stock = level.Reserve;

            foreach (VehicleKind kind in VehicleRoster.Kinds)
            {
                VehicleKind counted = kind;
                Number(
                    $"{VehicleNames.For(counted)}s",
                    stock.For(counted),
                    value => Set(
                        () => stock.Set(counted, Mathf.RoundToInt(value)),
                        $"Changed how many {VehicleNames.For(counted)}s a side gets."));
            }
        }

        /// <summary>
        /// Shows whatever is selected.
        /// </summary>
        private void ShowSelection(LevelDefinition level, EditSelection picked)
        {
            title.text = LevelEdits.Describe(level, picked).ToUpperInvariant();
            subtitle.text = $"{picked.Target} {picked.Index}";

            switch (picked.Target)
            {
                case EditTarget.Land:
                    ShowLand(level, picked);
                    break;
                case EditTarget.Bunker:
                    ShowBunker(level, picked);
                    break;
                case EditTarget.Tower:
                    ShowTower(level, picked);
                    break;
                case EditTarget.Structure:
                    ShowStructure(level, picked);
                    break;
                default:
                    break;
            }

            Action("Mirror to the other side", () =>
            {
                EditSelection made = LevelEdits.Mirror(level, picked);
                if (!made.IsSomething)
                {
                    return;
                }

                session.Select(made);
                session.Commit("Mirrored to the other side.");
            });

            Action("Delete", () => session.DeleteSelected());
        }

        private void ShowLand(LevelDefinition level, EditSelection picked)
        {
            LevelLand piece = level.Land[picked.Index];
            subtitle.text = $"{piece.Width:0.#} × {piece.Depth:0.#} m";

            Row(
                "Name",
                piece.Name,
                typed => Set(() => piece.Name = typed, "Renamed a piece of land."));
            Edge(level, picked, "West edge", piece.MinX, LandEdge.West);
            Edge(level, picked, "East edge", piece.MaxX, LandEdge.East);
            Edge(level, picked, "South edge", piece.MinZ, LandEdge.South);
            Edge(level, picked, "North edge", piece.MaxZ, LandEdge.North);
        }

        private void ShowBunker(LevelDefinition level, EditSelection picked)
        {
            LevelBunker bunker = level.Bunkers[picked.Index];
            title.color = EditorTheme.For(bunker.Side);
            subtitle.text = "DEPLOYS, REPAIRS, REFUELS AND WINS";

            Side(bunker.Side, side => Set(
                () => bunker.Team = side.ToString(), $"Gave the bunker to {side}."));
            Position(level, picked);
            Heading(level, picked, "Lift bay (°)");
            Number(
                "Supply radius",
                bunker.SupplyRadius,
                value => Set(
                    () => bunker.SupplyRadius = Mathf.Max(0.0f, value),
                    "Changed the supply radius."));
            Number(
                "Supply rate",
                bunker.SupplyRate,
                value => Set(
                    () => bunker.SupplyRate = Mathf.Max(0.0f, value), "Changed the supply rate."));
        }

        private void ShowTower(LevelDefinition level, EditSelection picked)
        {
            LevelTower tower = level.Towers[picked.Index];
            title.color = EditorTheme.For(tower.Side);
            subtitle.text = tower.HoldsTheFlag ? "HOLDS THE FLAG" : "DECOY";

            Side(tower.Side, side => Set(
                () => tower.Team = side.ToString(), $"Gave the tower to {side}."));
            Position(level, picked);
            Heading(level, picked, "Facing (°)");

            // A promotion rather than a switch. A side must have exactly one real tower, and
            // a checkbox on each of four pyramids is a way to build a map with none - which
            // looks identical on the map, because that is what a decoy is for.
            if (!tower.HoldsTheFlag)
            {
                Action("Put the flag on this one", () =>
                {
                    if (LevelEdits.MakeRealTower(level, picked.Index))
                    {
                        session.Commit($"The {tower.Side} flag is on this tower now.");
                    }
                });
            }
        }

        private void ShowStructure(LevelDefinition level, EditSelection picked)
        {
            LevelStructure structure = level.Structures[picked.Index];
            subtitle.text = structure.Supplies
                ? $"{structure.Kind} · RESUPPLIES"
                : structure.Kind;

            // Only the kinds that have a side get the buttons for one. A tree with a Side
            // row would be a control that writes a word the level file is then refused for.
            if (structure.NeedsASide)
            {
                title.color = EditorTheme.For(structure.Team);
                Side(structure.Team, side => Set(
                    () => structure.Side = side.ToString(),
                    $"Gave the {structure.Kind} to {side}."));
            }

            Row(
                "Name",
                structure.Name,
                typed => Set(() => structure.Name = typed, "Renamed a prop."));
            Position(level, picked);
            Heading(level, picked, "Facing (°)");
            Number(
                "Fuel rate",
                structure.FuelRate,
                value => Set(
                    () => structure.FuelRate = Mathf.Max(0.0f, value), "Changed a fuel rate."));
            Number(
                "Ammo rate",
                structure.AmmoRate,
                value => Set(
                    () => structure.AmmoRate = Mathf.Max(0.0f, value),
                    "Changed an ammunition rate."));
            Number(
                "Supply radius",
                structure.SupplyRadius,
                value => Set(
                    () => structure.SupplyRadius = Mathf.Max(0.0f, value),
                    "Changed the supply radius."));
        }

        /// <summary>
        /// Shows one edge of a rectangle of land, and lets it be typed.
        /// </summary>
        /// <param name="level">The map.</param>
        /// <param name="picked">Which rectangle.</param>
        /// <param name="caption">What to call the row.</param>
        /// <param name="value">What the edge is now.</param>
        /// <param name="edge">Which edge this row sets.</param>
        /// <remarks>
        /// Each row sets only its own edge and goes through
        /// <see cref="LevelEdits.SetLandEdges"/>, which sorts the four numbers and refuses a
        /// sliver. Typing a west edge that is east of the east edge therefore turns the
        /// rectangle inside out rather than describing land of negative width - which the
        /// builder would decline to build, with a warning about a piece of land that has no
        /// area, some distance from where anybody would look for it.
        /// </remarks>
        private void Edge(
            LevelDefinition level, EditSelection picked, string caption, float value, LandEdge edge)
            => Number(caption, value, typed =>
            {
                LevelLand piece = level.Land[picked.Index];
                float minX = edge == LandEdge.West ? typed : piece.MinX;
                float maxX = edge == LandEdge.East ? typed : piece.MaxX;
                float minZ = edge == LandEdge.South ? typed : piece.MinZ;
                float maxZ = edge == LandEdge.North ? typed : piece.MaxZ;

                if (LevelEdits.SetLandEdges(level, picked.Index, minX, maxX, minZ, maxZ))
                {
                    session.Commit("Moved a coastline.");
                }
            });

        /// <summary>
        /// Shows the two coordinates a placed thing stands at.
        /// </summary>
        /// <remarks>
        /// Only x and z. Height is never authored - land is at <c>y = 0</c> because the whole
        /// game is resolved on that plane, and the one exception is a bridge, which is sunk to
        /// its deck by a number that comes out of the model rather than out of the map.
        /// </remarks>
        private void Position(LevelDefinition level, EditSelection picked)
        {
            Vector3 at = LevelEdits.PositionOf(level, picked);

            Number("East (x)", at.x, value =>
            {
                Vector3 now = LevelEdits.PositionOf(level, picked);
                LevelEdits.MoveTo(level, picked, new Vector3(value, 0.0f, now.z));
                session.Commit("Moved it.");
            });

            Number("North (z)", at.z, value =>
            {
                Vector3 now = LevelEdits.PositionOf(level, picked);
                LevelEdits.MoveTo(level, picked, new Vector3(now.x, 0.0f, value));
                session.Commit("Moved it.");
            });
        }

        private void Heading(LevelDefinition level, EditSelection picked, string caption)
            => Number(caption, LevelEdits.YawOf(level, picked), value =>
            {
                LevelEdits.SetYaw(level, picked, value);
                session.Commit("Turned it.");
            });

        /// <summary>
        /// Fills the first row with the two sides, as a pair of buttons.
        /// </summary>
        /// <param name="current">The side it is on now.</param>
        /// <param name="set">What to do when the other one is pressed.</param>
        /// <remarks>
        /// Buttons rather than a typed name, because there are exactly two sides and a
        /// misspelt one resolves to nobody - which produces a bunker belonging to no team, on
        /// a map that looks completely normal until nobody can deploy from it.
        /// </remarks>
        private void Side(Team current, Action<Team> set)
        {
            if (usedRows >= fields.Count)
            {
                return;
            }

            int row = usedRows++;
            captions[row].enabled = true;
            captions[row].text = "Side";
            fields[row].gameObject.SetActive(false);

            IReadOnlyList<Team> playable = LevelEditorSession.PlayableSides();
            for (int slot = 0; slot < sides.Count && slot < playable.Count; slot++)
            {
                Team side = playable[slot];
                EditorButton button = sides[slot];
                button.SetVisible(true);
                button.SetEnabled(true);
                button.SetChosen(side == current);
                button.OnPress(() => set(side), SfxKind.UiSelect);
            }
        }

        /// <summary>
        /// Fills the next row with a caption and a line of text.
        /// </summary>
        private void Row(string caption, string value, Action<string> done)
        {
            if (usedRows >= fields.Count)
            {
                return;
            }

            int row = usedRows++;
            captions[row].enabled = true;
            captions[row].text = caption;

            InputField field = fields[row];
            field.gameObject.SetActive(true);
            field.onEndEdit.RemoveAllListeners();

            // Never while it is being typed in, or a rebuild triggered by anything else in
            // the editor would put the old text back under the caret mid-word.
            if (!HasFocus(field))
            {
                field.text = value == null ? string.Empty : value;
            }

            field.onEndEdit.AddListener(typed => done(typed));
        }

        /// <summary>
        /// Fills the next row with a caption and a number.
        /// </summary>
        /// <remarks>
        /// Read and written in the invariant culture, deliberately. A level file is JSON
        /// written by <c>JsonUtility</c>, which is invariant, so a machine set to a
        /// comma-decimal locale must not be able to type a number into this editor that its
        /// own level file cannot then express. A rejected value is left on the field rather
        /// than replaced with the old one, so what is on screen is always what was typed.
        /// </remarks>
        private void Number(string caption, float value, Action<float> done)
            => Row(caption, value.ToString("0.###", CultureInfo.InvariantCulture), typed =>
            {
                if (TryParseNumber(typed, out float parsed))
                {
                    done(parsed);
                }
                else
                {
                    session.Announce($"'{typed}' is not a number this map can hold.");
                }
            });

        /// <summary>
        /// Parses a number the way every field on this panel does.
        /// </summary>
        /// <param name="text">What was typed.</param>
        /// <param name="value">The parsed number, or zero when parsing failed.</param>
        /// <returns><c>true</c> when the text is a finite number.</returns>
        /// <remarks>
        /// <see cref="float.TryParse(string, NumberStyles, IFormatProvider, out float)"/>
        /// accepts the literal strings <c>"NaN"</c> and <c>"Infinity"</c> as valid numbers
        /// regardless of the style flags passed to it, and neither survives what happens
        /// next: every safety clamp in this file compares with <c>Mathf.Max</c>/<c>Min</c>,
        /// and every comparison against NaN is false, so <c>Mathf.Max(1.0f, float.NaN)</c>
        /// returns NaN unchanged. <see cref="IronFlag.Levels.LevelValidation"/>'s guard
        /// checks are built the same way, so a NaN half-extent read as fully valid and
        /// <c>LevelFile.TryWrite</c> serialised it into the file without complaint. Rejecting
        /// it here, before it reaches a single setter, is simpler than trying to make every
        /// downstream clamp and rule NaN-aware.
        /// </remarks>
        public static bool TryParseNumber(string text, out float value)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value) && !float.IsInfinity(value))
            {
                return true;
            }

            value = 0.0f;
            return false;
        }

        /// <summary>
        /// Fills the next action button.
        /// </summary>
        private void Action(string words, Action does)
        {
            if (usedActions >= actions.Count)
            {
                return;
            }

            EditorButton button = actions[usedActions++];
            button.SetVisible(true);
            button.SetText(words);
            button.SetChosen(false);
            button.SetEnabled(true);
            button.OnPress(does);
        }

        /// <summary>
        /// Makes a change and tells the session about it.
        /// </summary>
        private void Set(Action change, string what)
        {
            change();
            session.Commit(what);
        }

        private static bool HasFocus(InputField field)
        {
            EventSystem events = EventSystem.current;
            return events != null && events.currentSelectedGameObject == field.gameObject;
        }

        /// <summary>
        /// Places a rectangle a distance down from the top-left corner of its parent.
        /// </summary>
        /// <param name="rect">Rectangle to place.</param>
        /// <param name="left">Distance from the parent's left edge, in canvas units.</param>
        /// <param name="top">Distance below the parent's top edge; negative goes down.</param>
        /// <param name="width">Width in canvas units.</param>
        /// <param name="height">Height in canvas units.</param>
        /// <remarks>
        /// Downward from the top, unlike the HUD's bottom-left rule, because an inspector is a
        /// list that grows as things are selected - and a list that grew upward would move its
        /// first row every time a second one appeared under it.
        /// </remarks>
        private static void Hang(
            RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = new Vector2(0.0f, 1.0f);
            rect.anchorMax = new Vector2(0.0f, 1.0f);
            rect.pivot = new Vector2(0.0f, 1.0f);
            rect.anchoredPosition = new Vector2(left, top);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
