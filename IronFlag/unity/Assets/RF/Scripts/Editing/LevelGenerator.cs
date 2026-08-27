using System;
using System.Collections.Generic;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Levels;
using IronFlag.Vehicles;

namespace IronFlag.Editing
{
    /// <summary>
    /// Draws a whole map out of a seed: the ground, the coastlines, the bunkers, the towers,
    /// the depots, the emplacements and the trees.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The same shape as <see cref="LevelEdits.Starter"/>, with the numbers rolled.</strong>
    /// No scene, no camera, no components - a static function that hands back a
    /// <see cref="LevelDefinition"/>, which is why the whole of it can be tested without an
    /// editor and why the editor's own button is four lines long.
    /// </para>
    /// <para>
    /// <strong>Playable by construction, not by luck.</strong> The one topological rule a map
    /// owes is that the two bunkers are joined by land with no bridge in it - see
    /// <see cref="LevelValidation.IsConnected"/> - and a from-scratch random layout is exactly
    /// where that gets broken by accident. So every layout pays it deliberately: an
    /// <see cref="MapLayout.Island"/>'s two halves are drawn overlapping across the origin, a
    /// <see cref="MapLayout.Channel"/>'s causeway is one unbroken asphalt rectangle that
    /// reaches deep into both shores, and a <see cref="MapLayout.Lagoon"/>'s ring is closed at
    /// both flanks by link blobs drawn before anything is allowed to jitter. Nothing here
    /// hopes.
    /// </para>
    /// <para>
    /// <strong>Everything placed is settled onto land before it is kept.</strong>
    /// <see cref="Settle"/> takes the spot a layout wants and walks outward until it finds one
    /// that is far enough inside the realised coast - the noise-displaced one, not the drawn
    /// one - and far enough from everything already placed. That one function is where the
    /// shore margins, the tower-spacing rule and "do not put a tree inside the bunker" all
    /// come from, so none of them is written twice.
    /// </para>
    /// <para>
    /// <strong>Then it checks, and re-rolls.</strong> <see cref="Attempts"/> candidates at
    /// most; the first clean one wins, and when none is clean the least-broken one is handed
    /// back for the editor's own Problems panel to explain. A generated map does not have to
    /// be perfect - it has an editor around it - but it should not usually need touching.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// LevelDefinition map = LevelGenerator.Generate(new MapOptions
    /// {
    ///     Seed = 1995,
    ///     Difficulty = MapDifficulty.Hard,
    ///     Layout = MapLayout.Channel,
    /// });
    /// </code>
    /// </example>
    public static class LevelGenerator
    {
        /// <summary>How many maps are drawn before the best of a bad lot is handed back.</summary>
        /// <remarks>
        /// <para>
        /// Each attempt costs one <see cref="SurfaceField"/> - the map rasterised to a metre -
        /// so this is not free, and it is not meant to be a search either. Every layout is
        /// built to pass on the first attempt; these are for the tail, where a jittered blob
        /// left something with nowhere to stand. Eight is comfortably more than that tail
        /// needs and still well under a second on the largest map.
        /// </para>
        /// <para>
        /// Each attempt's seed is the asked-for seed mixed with the attempt number, so asking
        /// twice for one seed gets one map, re-rolls and all.
        /// </para>
        /// </remarks>
        public const int Attempts = 8;

        /// <summary>Half-width of the world an easy map gets, in metres.</summary>
        public const float EasyExtent = 100.0f;

        /// <summary>Half-width of the world a medium map gets, in metres.</summary>
        /// <remarks>Near the shipped map's 120 m, which is the size this game is tuned at.</remarks>
        public const float MediumExtent = 140.0f;

        /// <summary>Half-width of the world a hard map gets, in metres.</summary>
        public const float HardExtent = 180.0f;

        /// <summary>Metres of clearance a bunker is settled with.</summary>
        /// <remarks>
        /// A bunker's supply radius is 12 m and its lift bay deploys into the field, so
        /// nothing else on the map may stand inside that. This is the number
        /// <see cref="Settle"/> keeps everything else out by; the separate question of how
        /// much dry land it needs around it is
        /// <see cref="LevelValidation.BunkerShoreMargin"/>.
        /// </remarks>
        private const float BunkerRoom = 20.0f;

        /// <summary>Metres of clearance a flag tower is settled with.</summary>
        /// <remarks>
        /// Comfortably past twice the widest blast in the game, which is the rule
        /// <see cref="LevelValidation"/> actually enforces - see <see cref="SpacingNeeded"/>,
        /// read off the weapon table so a retuned rocket moves it. Wider than the rule on
        /// purpose: two towers exactly at the limit are a map that passes validation and still
        /// reads as a mistake.
        /// </remarks>
        private const float TowerRoom = 22.0f;

        /// <summary>Metres of clearance a solo map's flag towers are settled with.</summary>
        /// <remarks>
        /// Much wider than <see cref="TowerRoom"/>, because a solo map's towers are not a pair
        /// to tell apart but a field of small fortresses to crack open one at a time, each
        /// with its own ring of emplacements. Two of them inside each other's rings would be
        /// one bigger fortress.
        /// </remarks>
        private const float FortressRoom = 40.0f;

        /// <summary>Metres of clearance a depot is settled with.</summary>
        private const float DepotRoom = 12.0f;

        /// <summary>Metres of clearance an emplacement is settled with.</summary>
        private const float TurretRoom = 9.0f;

        /// <summary>Metres of clearance a building is settled with.</summary>
        private const float BuildingRoom = 10.0f;

        /// <summary>Metres of clearance a tree is settled with.</summary>
        private const float TreeRoom = 5.0f;

        /// <summary>Metres of clearance a bridge is settled with.</summary>
        private const float BridgeRoom = 10.0f;

        /// <summary>Metres of clearance one segment of a wall run is settled with.</summary>
        /// <remarks>
        /// Half a segment, which is the only room here that is a measurement rather than a
        /// judgement — and it has to be under <see cref="LevelEdits.SegmentLength"/> or a run
        /// would crowd itself out one segment after the first. It is the same number
        /// <see cref="LevelPick.ReachOf"/> uses for the same reason: the boundary between two
        /// segments falls exactly on the join a player can see.
        /// </remarks>
        private const float RampartRoom = LevelEdits.SegmentLength * 0.5f;

        /// <summary>How far out a side's bunker stands, as a share of the world.</summary>
        /// <remarks>
        /// These six ratios are read off the shipped map rather than invented. At its 120 m
        /// half-extent its bunkers stand at 70 m, its towers at 56 m and 18 m either side of
        /// the centre line, its depots at 38 m across and 34 m out, and its bridge
        /// emplacements 24 m back from the water. Every number here is one of those divided by
        /// 120, which is why a generated map at the medium size plays at roughly the distances
        /// the game is tuned for.
        /// </remarks>
        private const float BunkerOut = 0.58f;

        /// <summary>How far out a side's towers stand, as a share of the world.</summary>
        private const float TowerOut = 0.46f;

        /// <summary>How far either side of the centre line a side's towers stand.</summary>
        private const float TowerAcross = 0.15f;

        /// <summary>How far out a side's depots stand, as a share of the world.</summary>
        private const float DepotOut = 0.28f;

        /// <summary>How far either side of the centre line a side's depots stand.</summary>
        private const float DepotAcross = 0.32f;

        /// <summary>How far out a side's emplacements stand, as a share of the world.</summary>
        private const float FrontOut = 0.20f;

        /// <summary>How far out a side's wall runs stand, as a share of the world.</summary>
        /// <remarks>
        /// <strong>Behind the emplacements, not in front of them.</strong> A round stops on
        /// the first thing it hits and a wall is two metres tall, so a run laid between a
        /// turret and the enemy is a turret firing into its own defences. Between
        /// <see cref="FrontOut"/> and <see cref="DepotOut"/> puts it where a second line
        /// belongs: the guns cover the approach, and what gets past them meets the wall.
        /// </remarks>
        private const float RampartOut = 0.24f;

        /// <summary>Metres wide a causeway is built.</summary>
        /// <remarks>
        /// The shipped map's number, and it is a number rather than a taste: wide enough to be
        /// a road two vehicles pass on, narrow enough to be a decision to commit to. Asphalt
        /// keeps its edges exactly as written - <see cref="SurfaceTuning.NaturalEdge"/> is
        /// false for it - so 12 m stays 12 m however hard the coast either side wanders.
        /// </remarks>
        private const float CausewayWidth = 12.0f;

        /// <summary>Metres of open water a bridge is asked to span.</summary>
        /// <remarks>
        /// The shipped map's narrows, and measured rather than chosen: a test computed the
        /// jeep's ballistic jump and this is what clears it. A bridge over less than this is a
        /// ramp anybody can skip.
        /// </remarks>
        private const float Narrows = 13.0f;

        /// <summary>How far either side of the centre line a causeway runs.</summary>
        /// <remarks>
        /// Short, because a causeway is a crossing rather than a motorway. It only has to
        /// span the water between two headlands and land in each of them: the headlands reach
        /// to within a tenth of the world of the centre line, so this leaves seventeen metres
        /// of landfall at either end on the medium size. An earlier draft ran it to four
        /// tenths of the world for safety and drew a grey strip half the height of the map
        /// down each flank, which is not a road but a runway.
        /// </remarks>
        private const float CausewayReach = 0.24f;

        /// <summary>How far out a headland is centred, as a share of the world.</summary>
        /// <remarks>
        /// With the depth below, a headland runs from a tenth of the world out from the centre
        /// line to nearly six tenths - close enough to the water to make a short crossing, far
        /// enough back to bury itself in the shore behind it. Both of those are the point; see
        /// <see cref="ChannelGround"/>.
        /// </remarks>
        private const float HeadlandOut = 0.34f;

        /// <summary>Half a headland's width, as a share of the world.</summary>
        private const float HeadlandAcross = 0.16f;

        /// <summary>Half a headland's depth, as a share of the world.</summary>
        private const float HeadlandDeep = 0.24f;

        /// <summary>Where a side's cross road runs, as a share of the world.</summary>
        /// <remarks>
        /// The depots' own line, so the road between a bunker and its supplies goes past them
        /// rather than near them, and so the spur out to the causeway has something to leave
        /// from. It is also inside <see cref="BunkerOut"/> and outside the front, which is what
        /// lets one number join all three.
        /// </remarks>
        private const float CrossOut = 0.36f;

        /// <summary>Words a generated map's name can start with.</summary>
        /// <remarks>
        /// A name is not decoration. A map called "new-map-7" is one nobody can refer to in a
        /// sentence, and the editor's open list is a column of them within an afternoon. Two
        /// short lists and a layout make a name that is at least about the map.
        /// </remarks>
        private static readonly string[] Adjectives =
        {
            "Iron", "Broken", "Cold", "Long", "Salt", "Grey",
            "Low", "Far", "Quiet", "Hard", "Old", "Black",
        };

        /// <summary>Words an island map's name can end with.</summary>
        private static readonly string[] IslandNouns =
        {
            "Reach", "Rock", "Holm", "Rise", "Head", "Ground",
        };

        /// <summary>Words a channel map's name can end with.</summary>
        private static readonly string[] ChannelNouns =
        {
            "Narrows", "Channel", "Crossing", "Strait", "Divide", "Run",
        };

        /// <summary>Words a lagoon map's name can end with.</summary>
        private static readonly string[] LagoonNouns =
        {
            "Lagoon", "Ring", "Basin", "Atoll", "Circle", "Bay",
        };

        /// <summary>
        /// Draws a map.
        /// </summary>
        /// <param name="options">What to draw, or <c>null</c> for the middle of everything.</param>
        /// <returns>
        /// A level: the first candidate with nothing wrong with it, or the least-broken one
        /// when every attempt found something. Never <c>null</c>.
        /// </returns>
        /// <remarks>
        /// The one entry point, and it needs no Unity beyond <see cref="Vector3"/> and
        /// <see cref="Mathf"/>: no scene, no camera, no editor. What the editor's button does
        /// is call this and hand the answer to <see cref="LevelEditorSession.Adopt"/>.
        /// </remarks>
        public static LevelDefinition Generate(MapOptions options)
        {
            MapOptions settled = (options == null ? new MapOptions() : options).Settled();

            LevelDefinition best = null;
            int fewest = int.MaxValue;

            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                LevelDefinition candidate = Draw(settled, attempt);
                int faults = Faults(candidate, settled).Count;

                if (faults == 0)
                {
                    return candidate;
                }

                if (faults < fewest)
                {
                    fewest = faults;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// Lists what is wrong with a candidate, judged as the kind of map it was asked to be.
        /// </summary>
        /// <param name="level">The candidate.</param>
        /// <param name="options">What was asked for, which nothing here needs any more.</param>
        /// <returns>One sentence per fault; empty when there is nothing to re-roll away from.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="LevelValidation.Problems"/> and nothing else, for both kinds of map.
        /// There is one definition of a broken map and this is not a second one.
        /// </para>
        /// <para>
        /// It used to be two. A solo candidate was scored against a private copy of the rules
        /// that survive the shape change, because validation knew only the rules of a
        /// <em>match</em> - both sides own a bunker, both own exactly one real tower - and a
        /// one-player map breaks three of them on purpose, which left every re-roll tied at
        /// three faults and the loop picking between them on noise. Validation knows what a
        /// one-player map is now, so the copy is gone and the editor's Problems panel and the
        /// re-roll loop are reading the same answer again.
        /// </para>
        /// <para>
        /// The options are still taken, and still ignored, so that the caller does not have to
        /// know that the distinction has stopped mattering - the kind of map a candidate is
        /// is legible from the candidate itself, which is the point of
        /// <see cref="LevelDefinition.IsSolo"/>.
        /// </para>
        /// </remarks>
        public static List<string> Faults(LevelDefinition level, MapOptions options)
            => LevelValidation.Problems(level);

        /// <summary>
        /// Returns how far apart two of a side's towers have to stand.
        /// </summary>
        /// <returns>The distance, in metres.</returns>
        /// <remarks>
        /// Read off the weapon table rather than written down, exactly as
        /// <see cref="LevelValidation"/> reads it: two towers inside one blast both open to a
        /// single round and the decoy costs nothing to see through, so retuning the rocket has
        /// to move this rule with it rather than leave it quietly wrong.
        /// </remarks>
        public static float SpacingNeeded()
        {
            float widest = 0.0f;
            foreach (VehicleKind kind in Enum.GetValues(typeof(VehicleKind)))
            {
                widest = Mathf.Max(widest, WeaponTuning.For(kind).SplashRadius);
            }

            return widest * 2.0f;
        }

        /// <summary>
        /// Returns how big a world one difficulty gets.
        /// </summary>
        /// <param name="difficulty">The setting.</param>
        /// <returns>The half-extent, in metres.</returns>
        public static float ExtentFor(MapDifficulty difficulty)
        {
            switch (difficulty)
            {
                case MapDifficulty.Easy:
                    return EasyExtent;
                case MapDifficulty.Hard:
                    return HardExtent;
                default:
                    return MediumExtent;
            }
        }

        /// <summary>
        /// Draws one candidate.
        /// </summary>
        /// <param name="options">Settled options.</param>
        /// <param name="attempt">Which attempt this is, which is mixed into the seed.</param>
        /// <returns>A level, playable or not.</returns>
        /// <remarks>
        /// <para>
        /// Two passes, and the order is not a style choice. <strong>Every piece of land is
        /// drawn before anything is placed on it</strong>, because
        /// <see cref="LevelDefinition.Field"/> - the map rasterised, which is what
        /// <see cref="LevelDefinition.IsOnLand"/> actually consults - is rebuilt whenever the
        /// land underneath it changes. Interleaving the two would rebuild it once per
        /// rectangle and would still be correct; doing it this way makes it once per map.
        /// </para>
        /// </remarks>
        private static LevelDefinition Draw(MapOptions options, int attempt)
        {
            var dice = new Dice(Blend(options.Seed, attempt));
            float extent = ExtentFor(options.Difficulty);
            MapLayout layout = options.Layout == MapLayout.None
                ? Rolled(dice)
                : options.Layout;

            Frame frame = Plan(layout, extent, options, dice);

            // Rolled whether or not it is wanted, so that naming a map does not shift every
            // draw after it and make the same seed two different maps.
            string rolled = Named(layout, dice);

            var level = new LevelDefinition
            {
                SchemaVersion = LevelDefinition.Schema,
                Name = options.Name.Length > 0 ? options.Name : rolled,
                Seed = dice.Seed,
                Bounds = new LevelBounds { HalfExtent = extent },
            };

            level.Description = Described(level.Name, frame, options);

            Ground(level, frame, options, dice);
            Furnish(level, frame, options, dice);

            return level;
        }

        /// <summary>
        /// Rolls the numbers both halves of the map have to agree about.
        /// </summary>
        /// <param name="layout">The layout being drawn.</param>
        /// <param name="extent">Half-width of the world, in metres.</param>
        /// <param name="options">Settled options.</param>
        /// <param name="dice">The map's own dice.</param>
        /// <returns>The frame every later pass reads.</returns>
        /// <remarks>
        /// <para>
        /// The whole point of doing this first. A causeway is one rectangle shared by both
        /// halves and a headland is the piece of coast it lands on, so the two have to be
        /// drawn at the same x - and on an asymmetrical map the two halves are drawn from
        /// different dice and would otherwise disagree. Rolling the shared numbers once, up
        /// front, is what makes "asymmetrical" mean "different ground" rather than "no
        /// crossing".
        /// </para>
        /// <para>
        /// A mirrored map always gets a <em>pair</em> of crossings. One causeway is a
        /// perfectly good map and it is not a rotationally symmetric one: turned half a turn
        /// about the origin, a road on the east flank is a road on the west, so a single one
        /// would make the two runs different lengths on the one setting that promises they are
        /// not.
        /// </para>
        /// </remarks>
        private static Frame Plan(MapLayout layout, float extent, MapOptions options, Dice dice)
        {
            var frame = new Frame
            {
                Layout = layout,
                Extent = extent,
                Causeways = Array.Empty<float>(),
                Bridges = Array.Empty<float>(),
            };

            bool paired = options.Symmetry != MapSymmetry.Asymmetrical;

            switch (layout)
            {
                case MapLayout.Channel:
                    float causewayAt = dice.Between(0.48f, 0.56f) * extent;
                    float bridgeAt = dice.Between(0.18f, 0.32f) * extent;

                    frame.Causeways = paired || dice.Chance(0.5f)
                        ? new[] { -causewayAt, causewayAt }
                        : new[] { dice.Chance(0.5f) ? -causewayAt : causewayAt };

                    frame.Bridges = paired || dice.Chance(0.65f)
                        ? new[] { -bridgeAt, bridgeAt }
                        : new[] { dice.Chance(0.5f) ? -bridgeAt : bridgeAt };
                    break;

                case MapLayout.Lagoon:
                    frame.Ring = dice.Between(0.60f, 0.64f) * extent;
                    frame.Blob = dice.Between(0.26f, 0.28f) * extent;
                    break;
            }

            return frame;
        }

        /// <summary>
        /// Draws every piece of land on the map.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="options">Settled options.</param>
        /// <param name="dice">The map's own dice.</param>
        /// <remarks>
        /// <para>
        /// <strong>Worked out in full before a single rectangle is written, and then written
        /// one surface at a time.</strong> Overlap is paint order - the last rectangle in the
        /// file wins, see <see cref="LevelLand.Surface"/> - so a map drawn a landmass at a
        /// time puts the second landmass's <em>sand</em> over the first landmass's
        /// <em>grass</em>, and every place two of them meet comes out with a beach through the
        /// middle of it. On an island map, where the two halves are supposed to overlap into
        /// one peanut, that beach is drawn straight across the waist and the map reads as two
        /// islands touching.
        /// </para>
        /// <para>
        /// So the shapes are collected first and emitted in surface order: every bank as sand,
        /// then every bank again as grass inset by its own beach, then the meadows, then the
        /// scuffs, then the roads. What is left showing as sand is the rim that no grass
        /// covered, which is the shipped map's own idiom - a beach nobody drew - and it now
        /// only ever appears at a coastline.
        /// </para>
        /// <para>
        /// A mirrored map turns the collected shapes rather than the written rectangles, which
        /// is the same arithmetic - see <see cref="LevelEdits.Turned"/> - one step earlier, so
        /// both halves are in hand before the ordering matters.
        /// </para>
        /// </remarks>
        private static void Ground(
            LevelDefinition level, Frame frame, MapOptions options, Dice dice)
        {
            var banks = new List<Patch>();
            var meadows = new List<Patch>();
            var scuffs = new List<Patch>();
            var roads = new List<Patch>();

            Links(frame, banks);

            int bankFrom = banks.Count;
            int meadowFrom = meadows.Count;
            int scuffFrom = scuffs.Count;
            int roadFrom = roads.Count;

            SideGround(Team.Green, frame, dice.Branch(1), banks, meadows, scuffs, roads);

            if (options.Symmetry == MapSymmetry.Mirrored)
            {
                Turn(banks, bankFrom);
                Turn(meadows, meadowFrom);
                Turn(scuffs, scuffFrom);
                Turn(roads, roadFrom);
            }
            else
            {
                SideGround(Team.Brown, frame, dice.Branch(2), banks, meadows, scuffs, roads);
            }

            foreach (Patch bank in banks)
            {
                Ellipse(level, Name(bank.What, bank.Side), SurfaceKind.Sand, bank.At,
                    bank.Across, bank.Up);
            }

            foreach (Patch bank in banks)
            {
                Ellipse(level, Name(bank.Turf, bank.Side), SurfaceKind.Grass, bank.At,
                    bank.Across - bank.Beach, bank.Up - bank.Beach);
            }

            foreach (Patch meadow in meadows)
            {
                Ellipse(level, Name(meadow.What, meadow.Side), SurfaceKind.Grass, meadow.At,
                    meadow.Across, meadow.Up);
            }

            foreach (Patch scuff in scuffs)
            {
                Ellipse(level, Name(scuff.What, scuff.Side), SurfaceKind.Sand, scuff.At,
                    scuff.Across, scuff.Up);
            }

            foreach (Patch road in roads)
            {
                Rect(level, Name(road.What, road.Side), SurfaceKind.Asphalt, road.At,
                    road.Across, road.Up);
            }

            Causeways(level, frame);
        }

        /// <summary>
        /// Works out one side's half of the land, without writing any of it.
        /// </summary>
        /// <param name="side">Whose half.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="dice">This half's own dice.</param>
        /// <param name="banks">Landmasses, which get a beach and a grass middle.</param>
        /// <param name="meadows">Extra grass, laid over the banks.</param>
        /// <param name="scuffs">Extra sand, laid over the grass.</param>
        /// <param name="roads">Asphalt, laid over everything.</param>
        private static void SideGround(
            Team side,
            Frame frame,
            Dice dice,
            List<Patch> banks,
            List<Patch> meadows,
            List<Patch> scuffs,
            List<Patch> roads)
        {
            switch (frame.Layout)
            {
                case MapLayout.Channel:
                    ChannelGround(side, frame, dice, banks, meadows, scuffs, roads);
                    break;
                case MapLayout.Lagoon:
                    LagoonGround(side, frame, dice, banks, meadows, scuffs, roads);
                    break;
                default:
                    IslandGround(side, frame, dice, banks, meadows, scuffs, roads);
                    break;
            }
        }

        /// <summary>
        /// Works out half of one landmass, reaching across the origin so the halves meet.
        /// </summary>
        /// <param name="side">Whose half.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="dice">This half's own dice.</param>
        /// <param name="banks">Landmasses.</param>
        /// <param name="meadows">Extra grass.</param>
        /// <param name="scuffs">Extra sand.</param>
        /// <param name="roads">Asphalt.</param>
        /// <remarks>
        /// <para>
        /// <strong>This is where an island map pays its connectivity.</strong> Each half is an
        /// ellipse deeper than it is offset from the origin, so it reaches past the centre
        /// line - by a tenth of the world on the very worst draw - and the two overlap in a
        /// band twenty metres deep at the smallest size. The coastline wobble is three metres.
        /// Nothing can pull them apart.
        /// </para>
        /// </remarks>
        private static void IslandGround(
            Team side,
            Frame frame,
            Dice dice,
            List<Patch> banks,
            List<Patch> meadows,
            List<Patch> scuffs,
            List<Patch> roads)
        {
            float extent = frame.Extent;
            float toward = Toward(side);
            float across = dice.Between(0.56f, 0.62f) * extent;
            float deep = dice.Between(0.44f, 0.48f) * extent;
            float middle = dice.Between(0.30f, 0.34f) * extent;
            float beach = Beach(extent);
            var centre = new Vector3(dice.Spread(0.05f * extent), 0.0f, toward * middle);

            banks.Add(new Patch("Shore", "Downs", side, centre, across, deep, beach));

            Patches(side, dice, centre, across * 0.55f, deep * 0.55f, extent, meadows, scuffs);
            roads.Add(BunkerRoad(side, frame, dice));
        }

        /// <summary>
        /// Works out one shore of a channel, its headlands, and the roads across it.
        /// </summary>
        /// <param name="side">Whose shore.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="dice">This half's own dice.</param>
        /// <param name="banks">Landmasses.</param>
        /// <param name="meadows">Extra grass.</param>
        /// <param name="scuffs">Extra sand.</param>
        /// <param name="roads">Asphalt.</param>
        /// <remarks>
        /// <para>
        /// A shore ellipse is widest at its middle and narrow at its flanks, which is exactly
        /// where a causeway wants to land - so a causeway run out to a bare shore meets it at
        /// a sliver a few metres deep, and three metres of coastline wobble can take that
        /// away. <strong>The headlands are the fix rather than the decoration</strong>: one
        /// per causeway, centred on the causeway's own x and reaching from near the water most
        /// of the way home, so a crossing lands in twenty metres of ground on every draw.
        /// </para>
        /// <para>
        /// A bridgehead is measured against the shore rather than given a fixed length, for
        /// the same reason and with the same arithmetic: how far in this side's coast actually
        /// starts at that x, plus enough to swallow the wobble. Given a fixed length it is
        /// either a ramp that stops in the water on the outermost bridge or a runway on the
        /// innermost, depending on which draw it was tuned against.
        /// </para>
        /// </remarks>
        private static void ChannelGround(
            Team side,
            Frame frame,
            Dice dice,
            List<Patch> banks,
            List<Patch> meadows,
            List<Patch> scuffs,
            List<Patch> roads)
        {
            float extent = frame.Extent;
            float toward = Toward(side);
            float across = dice.Between(0.58f, 0.62f) * extent;
            float near = dice.Between(0.11f, 0.14f) * extent;
            float far = dice.Between(0.88f, 0.92f) * extent;
            float deep = (far - near) * 0.5f;
            float beach = Beach(extent);
            var centre = new Vector3(dice.Spread(0.03f * extent), 0.0f, toward * (near + deep));

            banks.Add(new Patch("Shore", "Downs", side, centre, across, deep, beach));

            foreach (float at in frame.Causeways)
            {
                banks.Add(new Patch(
                    "Headland",
                    "Headland downs",
                    side,
                    new Vector3(at, 0.0f, toward * HeadlandOut * extent),
                    HeadlandAcross * extent,
                    HeadlandDeep * extent,
                    beach));
            }

            foreach (float at in frame.Bridges)
            {
                float head = Narrows * 0.5f;
                float reach = ShoreEdge(centre.x, across, near, deep, at, CausewayWidth * 0.5f)
                    + SurfaceNoise.Amplitude + 5.0f;
                if (reach - head < LevelEdits.MinimumLandSide)
                {
                    continue;
                }

                roads.Add(new Patch(
                    "Bridgehead",
                    string.Empty,
                    side,
                    new Vector3(at, 0.0f, toward * (head + reach) * 0.5f),
                    CausewayWidth * 0.5f,
                    (reach - head) * 0.5f,
                    0.0f));
            }

            Patches(side, dice, centre, across * 0.5f, deep * 0.5f, extent, meadows, scuffs);

            Patch home = BunkerRoad(side, frame, dice);
            roads.Add(home);

            if (frame.Causeways.Length == 0)
            {
                return;
            }

            // A spur out to one causeway, on one flank only, so tarmac never runs right round
            // the channel and reaching the far crossing means leaving the road. The shipped map
            // does the same thing for the same reason.
            float spur = frame.Causeways[dice.Upto(frame.Causeways.Length)];
            roads.Add(new Patch(
                "Causeway road",
                string.Empty,
                side,
                new Vector3(spur, 0.0f, toward * CrossOut * extent),
                2.5f,
                0.16f * extent,
                0.0f));

            // And the road that makes those two a network rather than two strips of tarmac
            // that happen to be on the same map. It runs across the depots' own line - which
            // is where a road between a bunker and its supplies belongs - and out as far as
            // whichever causeway this side is the near one for, crossing the bunker road on
            // the way. The shipped map's depot road is the same road.
            float west = Mathf.Min(-DepotAcross * extent, spur);
            float east = Mathf.Max(DepotAcross * extent, spur);
            roads.Add(new Patch(
                "Depot road",
                string.Empty,
                side,
                new Vector3((west + east) * 0.5f, 0.0f, toward * DepotOut * extent),
                (east - west) * 0.5f,
                2.5f,
                0.0f));
        }

        /// <summary>
        /// Works out one side's arc of the ring around a lagoon.
        /// </summary>
        /// <param name="side">Whose arc.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="dice">This half's own dice.</param>
        /// <param name="banks">Landmasses.</param>
        /// <param name="meadows">Extra grass.</param>
        /// <param name="scuffs">Extra sand.</param>
        /// <param name="roads">Asphalt.</param>
        /// <remarks>
        /// <para>
        /// <strong>This is where a lagoon map pays its connectivity</strong>, and it is
        /// arithmetic rather than hope. Six blobs an arc, thirty degrees apart, each about
        /// half the ring's radius wide: neighbours are a third of the world apart centre to
        /// centre and half of it across, so they overlap by ten metres even when both jitter
        /// the wrong way. What closes the ring at the flanks, where two arcs drawn from
        /// different dice meet, is not these at all - see <see cref="Links"/>.
        /// </para>
        /// </remarks>
        private static void LagoonGround(
            Team side,
            Frame frame,
            Dice dice,
            List<Patch> banks,
            List<Patch> meadows,
            List<Patch> scuffs,
            List<Patch> roads)
        {
            const int Blobs = 6;
            float extent = frame.Extent;
            float home = Toward(side) < 0.0f ? 270.0f : 90.0f;
            float beach = Beach(extent);

            for (int blob = 0; blob < Blobs; blob++)
            {
                float around = home + ((blob - ((Blobs - 1) * 0.5f)) * 30.0f) + dice.Spread(5.0f);
                float radians = around * Mathf.Deg2Rad;
                float radius = frame.Ring + dice.Spread(0.02f * extent);
                var centre = new Vector3(
                    Mathf.Cos(radians) * radius, 0.0f, Mathf.Sin(radians) * radius);

                banks.Add(new Patch(
                    "Bank",
                    "Downs",
                    side,
                    centre,
                    frame.Blob + dice.Spread(0.015f * extent),
                    frame.Blob + dice.Spread(0.015f * extent),
                    beach));

                if (dice.Chance(0.4f))
                {
                    scuffs.Add(new Patch(
                        "Scuff",
                        string.Empty,
                        side,
                        centre + new Vector3(
                            dice.Spread(frame.Blob * 0.4f), 0.0f, dice.Spread(frame.Blob * 0.4f)),
                        dice.Between(0.05f, 0.10f) * extent,
                        dice.Between(0.04f, 0.09f) * extent,
                        0.0f));
                }
            }

            roads.Add(BunkerRoad(side, frame, dice));
        }

        /// <summary>
        /// Works out the natural land both halves share.
        /// </summary>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="banks">Landmasses to add to.</param>
        /// <remarks>
        /// Two blobs at the flanks of a lagoon, on the centre line, belonging to nobody. They
        /// are what closes the ring where one arc ends and the other begins - the one place on
        /// that map where two halves drawn from different dice have to meet - so they are
        /// fixed, drawn first, and never jittered. Every other layout closes itself and needs
        /// none.
        /// </remarks>
        private static void Links(Frame frame, List<Patch> banks)
        {
            if (frame.Layout != MapLayout.Lagoon)
            {
                return;
            }

            float beach = Beach(frame.Extent);

            for (int flank = 0; flank < 2; flank++)
            {
                banks.Add(new Patch(
                    "Link",
                    "Link downs",
                    Team.None,
                    new Vector3(flank == 0 ? frame.Ring : -frame.Ring, 0.0f, 0.0f),
                    frame.Blob,
                    frame.Blob,
                    beach));
            }
        }

        /// <summary>
        /// Scatters a few patches of other ground over a landmass.
        /// </summary>
        /// <param name="side">Whose half.</param>
        /// <param name="dice">This half's own dice.</param>
        /// <param name="centre">Middle of the landmass they go on.</param>
        /// <param name="acrossRoom">How far across the middle they may wander.</param>
        /// <param name="upRoom">How far up the middle they may wander.</param>
        /// <param name="extent">Half-width of the world, in metres.</param>
        /// <param name="meadows">Extra grass to add to.</param>
        /// <param name="scuffs">Extra sand to add to.</param>
        /// <remarks>
        /// Placed off the landmass's own geometry rather than by asking the map what is under
        /// them, and that is not laziness: <see cref="LevelDefinition.Field"/> is rebuilt
        /// whenever the land changes, so a patch that asked would rebuild the whole map to
        /// place one ellipse. Kept well inside the parent instead, which is cheaper and cannot
        /// leave an islet off the coast for something to be stranded on.
        /// </remarks>
        private static void Patches(
            Team side,
            Dice dice,
            Vector3 centre,
            float acrossRoom,
            float upRoom,
            float extent,
            List<Patch> meadows,
            List<Patch> scuffs)
        {
            int spread = 1 + dice.Upto(3);
            for (int patch = 0; patch < spread; patch++)
            {
                float size = dice.Between(0.06f, 0.14f) * extent;
                meadows.Add(new Patch(
                    "Meadow",
                    string.Empty,
                    side,
                    new Vector3(
                        centre.x + dice.Spread(acrossRoom), 0.0f, centre.z + dice.Spread(upRoom)),
                    size,
                    size * dice.Between(0.5f, 1.0f),
                    0.0f));
            }

            int scuffed = 1 + dice.Upto(3);
            for (int patch = 0; patch < scuffed; patch++)
            {
                float size = dice.Between(0.05f, 0.12f) * extent;
                scuffs.Add(new Patch(
                    "Scuff",
                    string.Empty,
                    side,
                    new Vector3(
                        centre.x + dice.Spread(acrossRoom), 0.0f, centre.z + dice.Spread(upRoom)),
                    size,
                    size * dice.Between(0.5f, 1.0f),
                    0.0f));
            }
        }

        /// <summary>
        /// Works out the road out of one side's bunker.
        /// </summary>
        /// <param name="side">Whose road.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="dice">This half's own dice.</param>
        /// <returns>The road.</returns>
        /// <remarks>
        /// <para>
        /// Cosmetic in the sense that no rule needs it, and not cosmetic at all in what the
        /// map reads as: asphalt is a fifth faster and a good deal less thirsty than grass, so
        /// a road out of a bunker is the answer to the soft going in front of it and it says
        /// which way this side is expected to leave home.
        /// </para>
        /// <para>
        /// It runs the way out of the bunker, which is a different direction on a lagoon than
        /// on the other two. On an island or a channel "out" is toward the far side, straight
        /// up the map. On a lagoon the far side is round the ring and the middle is water, so
        /// out is along the ring instead - a road up the middle of a lagoon would be a jetty.
        /// </para>
        /// <para>
        /// Drawn against where the bunker <em>wants</em> to be rather than where it ends up,
        /// because the land is finished before anything is settled onto it. The bunker settles
        /// within a few metres of here on any ordinary draw, which is close enough for a road.
        /// </para>
        /// </remarks>
        private static Patch BunkerRoad(Team side, Frame frame, Dice dice)
        {
            float extent = frame.Extent;
            Vector3 home = Where(frame, side, BunkerOut, 0.0f);

            if (frame.Layout == MapLayout.Lagoon)
            {
                return new Patch(
                    "Bunker road",
                    string.Empty,
                    side,
                    new Vector3(home.x + dice.Spread(0.04f * extent), 0.0f, home.z),
                    0.16f * extent,
                    2.5f,
                    0.0f);
            }

            // Reaches in past the depot road rather than stopping at the front, so it crosses
            // the network it is meant to join: a road that stopped just outside one would be a
            // strip of tarmac pointing at it.
            float front = (FrontOut + 0.04f) * extent;
            float back = Mathf.Abs(home.z);

            return new Patch(
                "Bunker road",
                string.Empty,
                side,
                new Vector3(
                    home.x + dice.Spread(0.03f * extent),
                    0.0f,
                    Toward(side) * (front + back) * 0.5f),
                2.5f,
                Mathf.Max(LevelEdits.MinimumLandSide, (back - front) * 0.5f),
                0.0f);
        }

        /// <summary>
        /// Draws the causeways, which are the last thing on the map and belong to nobody.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <remarks>
        /// <para>
        /// Written after every other piece of land, including both sides' own roads, because
        /// this is the one surface on the map that must not be painted over: a causeway is
        /// what makes a channel map winnable when every bridge on it has been dropped, and a
        /// causeway with a beach over it is a causeway nobody can see.
        /// </para>
        /// <para>
        /// Drawn once rather than per side and never turned, because it spans the seam and is
        /// already the same on both. That is also what makes an asymmetrical map an
        /// asymmetrical map rather than a broken one: the two halves disagree about everything
        /// except the ground that joins them.
        /// </para>
        /// </remarks>
        private static void Causeways(LevelDefinition level, Frame frame)
        {
            if (frame.Layout != MapLayout.Channel)
            {
                return;
            }

            foreach (float at in frame.Causeways)
            {
                Rect(
                    level, "Causeway", SurfaceKind.Asphalt,
                    new Vector3(at, 0.0f, 0.0f),
                    CausewayWidth * 0.5f,
                    CausewayReach * frame.Extent);
            }
        }

        /// <summary>
        /// Measures how far in from the centre line a shore starts at a given x.
        /// </summary>
        /// <param name="middle">Where across the map the shore ellipse is centred.</param>
        /// <param name="across">Half the shore's width, in metres.</param>
        /// <param name="near">How far in from the centre line its nearest point is.</param>
        /// <param name="deep">Half the shore's depth, in metres.</param>
        /// <param name="at">Where across the map the answer is wanted.</param>
        /// <param name="width">Half the width of the thing that wants the answer.</param>
        /// <returns>Metres from the centre line, always positive.</returns>
        /// <remarks>
        /// Measured at whichever edge of that width is further from the middle of the shore,
        /// because that is the corner that reaches the water first - an ellipse falls away in
        /// both directions and the near corner would flatter it.
        /// </remarks>
        private static float ShoreEdge(
            float middle, float across, float near, float deep, float at, float width)
        {
            float west = Mathf.Abs((at - width) - middle);
            float east = Mathf.Abs((at + width) - middle);
            float along = across <= 0.0f ? 1.0f : Mathf.Clamp01(Mathf.Max(west, east) / across);

            return near + deep - (deep * Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - (along * along))));
        }

        /// <summary>
        /// Returns how wide a beach a map of a given size gets.
        /// </summary>
        /// <param name="extent">Half-width of the world, in metres.</param>
        /// <returns>Metres of sand left showing around a bank's grass.</returns>
        /// <remarks>
        /// A share of the world rather than a fixed width, so a beach is the same fraction of
        /// the picture on every size, with a floor under it so the smallest map's coast is
        /// still a coast rather than a line.
        /// </remarks>
        private static float Beach(float extent) => Mathf.Max(4.0f, 0.05f * extent);

        /// <summary>
        /// Turns every collected shape from an index onward into the other side's.
        /// </summary>
        /// <param name="patches">The list.</param>
        /// <param name="from">First one to turn.</param>
        /// <remarks>
        /// The end of the list is read once, before anything is appended, so the turned copies
        /// are not themselves turned - which would put the map back where it started and take
        /// twice as long doing it.
        /// </remarks>
        private static void Turn(List<Patch> patches, int from)
        {
            int collected = patches.Count;
            for (int index = from; index < collected; index++)
            {
                patches.Add(patches[index].Turned());
            }
        }

        /// <summary>
        /// Puts everything that stands on the map onto it.
        /// </summary>
        /// <param name="level">The level being built, with all its land drawn.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="options">Settled options.</param>
        /// <param name="dice">The map's own dice.</param>
        /// <remarks>
        /// Order is priority. Everything settled goes into one list of taken spots, and each
        /// thing keeps everything after it at arm's length, so the bunker gets the ground it
        /// wants, the towers get theirs out of what is left, and the fortieth tree takes
        /// whatever it can find.
        /// </remarks>
        private static void Furnish(
            LevelDefinition level, Frame frame, MapOptions options, Dice dice)
        {
            var taken = new List<Placed>();

            SeamProps(level, frame, taken, dice.Branch(5));

            if (options.IsSolo)
            {
                SoloProps(level, frame, options, taken, dice);
                return;
            }

            int structuresFrom = level.Structures.Length;
            int towersFrom = level.Towers.Length;
            int bunkersFrom = level.Bunkers.Length;

            SideProps(level, Team.Green, frame, options, taken, dice.Branch(3));

            if (options.Symmetry == MapSymmetry.Mirrored)
            {
                TurnProps(level, structuresFrom, towersFrom, bunkersFrom);
            }
            else
            {
                SideProps(level, Team.Brown, frame, options, taken, dice.Branch(4));
            }
        }

        /// <summary>
        /// Drops the bridges over the narrows.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="taken">Spots already used.</param>
        /// <param name="dice">The seam's own dice.</param>
        /// <remarks>
        /// Placed rather than settled, and deliberately: a bridge is the one prop on the map
        /// that belongs over water, and <see cref="LevelValidation"/> refuses one that spans
        /// dry land as a ramp to nowhere. The gap it crosses was left by
        /// <see cref="ChannelGround"/>, so this only has to stand in the middle of it - sunk
        /// to its deck, which <see cref="LevelEdits.AddStructure"/> handles.
        /// </remarks>
        private static void SeamProps(
            LevelDefinition level, Frame frame, List<Placed> taken, Dice dice)
        {
            if (frame.Layout != MapLayout.Channel)
            {
                return;
            }

            foreach (float at in frame.Bridges)
            {
                var over = new Vector3(at, 0.0f, 0.0f);
                int placed = LevelEdits.AddStructure(level, StructureKind.Bridge, over);
                if (placed < 0)
                {
                    continue;
                }

                level.Structures[placed].Name = at < 0.0f ? "West bridge" : "East bridge";
                taken.Add(new Placed(over, BridgeRoom));
            }
        }

        /// <summary>
        /// Puts one side's bunker, towers, depots, emplacements and scenery on the map.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="side">Whose things.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="options">Settled options.</param>
        /// <param name="taken">Spots already used.</param>
        /// <param name="dice">This side's own dice.</param>
        private static void SideProps(
            LevelDefinition level,
            Team side,
            Frame frame,
            MapOptions options,
            List<Placed> taken,
            Dice dice)
        {
            Tier tier = TierFor(options.Difficulty);

            Bunker(level, side, frame, taken, dice);
            Towers(level, side, frame, taken, dice);

            // Before the depots and the guns, which is a claim about priority and worth
            // reading as one. A wall run is not something a map owes - it is skipped when
            // there is nowhere for it - but it is the one thing here whose shape is fixed:
            // its segments must touch, so it cannot be nudged aside the way a settled depot
            // or a settled emplacement can. Laid first, everything after it settles around
            // the finished run; laid last, it would be the only thing on the map that could
            // not get out of its own way.
            Ramparts(level, side, frame, tier.Ramparts, taken, dice);

            Depots(level, side, frame, taken, dice);
            Turrets(level, side, frame, tier.Turrets, taken, dice);
            Scenery(level, side, frame, tier, taken, dice);
        }

        /// <summary>
        /// Puts a solo map's one bunker and its field of enemy towers on the map.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="options">Settled options.</param>
        /// <param name="taken">Spots already used.</param>
        /// <param name="dice">The map's own dice.</param>
        /// <remarks>
        /// <para>
        /// Green gets a bunker and no towers, because there is nobody to take its flag. Brown
        /// gets no bunker and several towers - one real and the rest decoys - each ringed with
        /// its own emplacements, so finding the flag is a series of small fortresses to crack
        /// rather than a choice between two pyramids. The decoy count is this mode's own
        /// difficulty lever, on top of the size and the emplacements.
        /// </para>
        /// <para>
        /// Which tower is real is rolled after all of them are placed rather than falling out
        /// of the order they were placed in. <see cref="LevelEdits.AddTower"/> makes a side's
        /// first tower the real one, which is the right rule for somebody building a map by
        /// hand and would put a generated map's flag in the same relative position every time.
        /// </para>
        /// <para>
        /// <strong>A solo map gets no <see cref="Ramparts"/>, deliberately.</strong> Green's
        /// front is the one piece of ground on a solo map that nothing ever attacks, so a
        /// gated wall run across it would be a defence against nobody; and the place a wall
        /// genuinely belongs here - ringing each of brown's fortresses - wants a run laid
        /// against a tower rather than settled away from one, which
        /// <see cref="FortressRoom"/> currently makes impossible. That is a piece of design
        /// work rather than a missing call, and it is the obvious next thing to do with this.
        /// </para>
        /// </remarks>
        private static void SoloProps(
            LevelDefinition level,
            Frame frame,
            MapOptions options,
            List<Placed> taken,
            Dice dice)
        {
            Tier tier = TierFor(options.Difficulty);
            Dice home = dice.Branch(3);
            Dice enemy = dice.Branch(4);

            Bunker(level, Team.Green, frame, taken, home);
            Depots(level, Team.Green, frame, taken, home);
            Scenery(level, Team.Green, frame, tier, taken, home);

            var fortresses = new List<Vector3>();
            var raised = new List<int>();

            for (int tower = 0; tower < tier.SoloTowers; tower++)
            {
                Vector3 wanted = Where(
                    frame,
                    Team.Brown,
                    enemy.Between(0.34f, 0.66f),
                    enemy.Between(-0.45f, 0.45f));

                Settle(level, wanted, LevelValidation.ShoreMargin, FortressRoom, taken, enemy,
                    out Vector3 at);
                int placed = LevelEdits.AddTower(level, Team.Brown, at);
                if (placed >= 0)
                {
                    raised.Add(placed);
                    fortresses.Add(at);
                }
            }

            if (raised.Count > 0)
            {
                LevelEdits.MakeRealTower(level, raised[enemy.Upto(raised.Count)]);
            }

            Garrison(level, fortresses, tier.SoloTurrets, taken, enemy);
            Depots(level, Team.Brown, frame, taken, enemy);
            Scenery(level, Team.Brown, frame, tier, taken, enemy);
        }

        /// <summary>
        /// Rings a solo map's flag towers with emplacements.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="fortresses">Where the towers ended up.</param>
        /// <param name="count">How many emplacements there are altogether.</param>
        /// <param name="taken">Spots already used.</param>
        /// <param name="dice">The enemy side's dice.</param>
        /// <remarks>
        /// Dealt round the towers rather than piled on the first, so an odd count leaves one
        /// tower a little softer than the rest - which is a map with a way in rather than an
        /// accounting error.
        /// </remarks>
        private static void Garrison(
            LevelDefinition level,
            List<Vector3> fortresses,
            int count,
            List<Placed> taken,
            Dice dice)
        {
            if (fortresses.Count == 0)
            {
                return;
            }

            for (int gun = 0; gun < count; gun++)
            {
                Vector3 tower = fortresses[gun % fortresses.Count];
                float around = dice.Between(0.0f, 360.0f) * Mathf.Deg2Rad;
                float radius = dice.Between(13.0f, 19.0f);
                var wanted = new Vector3(
                    tower.x + (Mathf.Cos(around) * radius),
                    0.0f,
                    tower.z + (Mathf.Sin(around) * radius));

                if (Settle(level, wanted, LevelValidation.ShoreMargin, TurretRoom, taken, dice,
                    out Vector3 at))
                {
                    Emplacement(level, Team.Brown, at);
                }
            }
        }

        /// <summary>
        /// Puts one side's bunker down.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="side">Whose bunker.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="taken">Spots already used.</param>
        /// <param name="dice">This side's dice.</param>
        private static void Bunker(
            LevelDefinition level, Team side, Frame frame, List<Placed> taken, Dice dice)
        {
            Vector3 wanted = Where(
                frame, side, BunkerOut + dice.Spread(0.03f), dice.Spread(0.05f));

            Settle(level, wanted, LevelValidation.BunkerShoreMargin, BunkerRoom, taken, dice,
                out Vector3 at);
            LevelEdits.AddBunker(level, side, at);
        }

        /// <summary>
        /// Puts one side's real tower and its decoy down.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="side">Whose towers.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="taken">Spots already used.</param>
        /// <param name="dice">This side's dice.</param>
        /// <remarks>
        /// Which of the two is real is rolled rather than left to
        /// <see cref="LevelEdits.AddTower"/>'s first-one-wins rule, for the reason
        /// <see cref="SoloProps"/> gives: a generated map whose flag is always on the tower
        /// drawn first is a generated map with a tell.
        /// </remarks>
        private static void Towers(
            LevelDefinition level, Team side, Frame frame, List<Placed> taken, Dice dice)
        {
            float lean = dice.Spread(0.03f);
            var raised = new List<int>();

            for (int tower = 0; tower < 2; tower++)
            {
                float across = (tower == 0 ? -TowerAcross : TowerAcross) + dice.Spread(0.04f);
                Vector3 wanted = Where(
                    frame, side, TowerOut + lean + dice.Spread(0.03f), across);

                Settle(level, wanted, LevelValidation.ShoreMargin, TowerRoom, taken, dice,
                    out Vector3 at);
                int placed = LevelEdits.AddTower(level, side, at);
                if (placed >= 0)
                {
                    raised.Add(placed);
                }
            }

            if (raised.Count > 0)
            {
                LevelEdits.MakeRealTower(level, raised[dice.Upto(raised.Count)]);
            }
        }

        /// <summary>
        /// Puts a fuel depot and an ammunition depot on one side's ground.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="side">Whose ground.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="taken">Spots already used.</param>
        /// <param name="dice">This side's dice.</param>
        /// <remarks>
        /// One of each, on opposite flanks, because a map wants somewhere to refuel and
        /// somewhere to rearm away from home - <see cref="LevelValidation"/> insists on at
        /// least one of each somewhere - and two of them in the same field would be one stop
        /// rather than a choice.
        /// </remarks>
        private static void Depots(
            LevelDefinition level, Team side, Frame frame, List<Placed> taken, Dice dice)
        {
            bool fuelWest = dice.Chance(0.5f);

            Depot(
                level, StructureKind.DepotFuel, "Fuel depot", side, frame,
                fuelWest ? -DepotAcross : DepotAcross, taken, dice);
            Depot(
                level, StructureKind.DepotAmmo, "Ammo depot", side, frame,
                fuelWest ? DepotAcross : -DepotAcross, taken, dice);
        }

        /// <summary>
        /// Puts one depot down.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="kind">Which depot.</param>
        /// <param name="called">What to call it in the hierarchy.</param>
        /// <param name="side">Whose ground it stands on.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="across">How far either side of the centre line.</param>
        /// <param name="taken">Spots already used.</param>
        /// <param name="dice">This side's dice.</param>
        private static void Depot(
            LevelDefinition level,
            StructureKind kind,
            string called,
            Team side,
            Frame frame,
            float across,
            List<Placed> taken,
            Dice dice)
        {
            Vector3 wanted = Where(
                frame, side, DepotOut + dice.Spread(0.05f), across + dice.Spread(0.05f));

            Settle(level, wanted, LevelValidation.ShoreMargin, DepotRoom, taken, dice,
                out Vector3 at);
            int placed = LevelEdits.AddStructure(level, kind, at);
            if (placed < 0)
            {
                return;
            }

            level.Structures[placed].Name = Name(called, side);
            level.Structures[placed].YawDegrees = dice.Between(0.0f, 360.0f);
        }

        /// <summary>
        /// Puts one side's emplacements along its front.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="side">Whose emplacements.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="count">How many.</param>
        /// <param name="taken">Spots already used.</param>
        /// <param name="dice">This side's dice.</param>
        /// <remarks>
        /// On a channel they are dealt round the crossings, which is where the shipped map
        /// puts its four and for the reason it gives: an emplacement behind a bridgehead makes
        /// crossing a decision taken under fire rather than one refused before it starts.
        /// Where there are no crossings there is no such place, so they spread across the
        /// front instead.
        /// </remarks>
        private static void Turrets(
            LevelDefinition level,
            Team side,
            Frame frame,
            int count,
            List<Placed> taken,
            Dice dice)
        {
            float[] crossings = Crossings(frame);

            for (int gun = 0; gun < count; gun++)
            {
                float across = crossings.Length > 0
                    ? (crossings[gun % crossings.Length] / frame.Extent) + dice.Spread(0.06f)
                    : dice.Between(-0.45f, 0.45f);

                Vector3 wanted = Where(frame, side, FrontOut + dice.Spread(0.05f), across);

                // Skipped rather than forced when there is nowhere for it, exactly as scenery
                // is: an emplacement is how heavily a map is defended rather than something it
                // owes, and one in the water defends nothing and fails the whole map.
                if (Settle(level, wanted, LevelValidation.ShoreMargin, TurretRoom, taken, dice,
                    out Vector3 at))
                {
                    Emplacement(level, side, at);
                }
            }
        }

        /// <summary>
        /// Puts one emplacement down, facing the other side.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="side">Whose it is.</param>
        /// <param name="at">Where it stands.</param>
        /// <remarks>
        /// <para>
        /// An emplacement always belongs to somebody. One with no side has nobody to shoot at
        /// and stands there looking exactly like one that works, which is a mistake only
        /// <see cref="LevelValidation"/> catches - so the side goes in at the moment of
        /// placing rather than being something a later pass remembers.
        /// </para>
        /// <para>
        /// The heading is the one thing here the dice do not touch. Where an emplacement
        /// stands is a roll; which way it looks is
        /// <see cref="LevelEdits.FacingTheEnemy"/>, the same answer a hand-placed one gets.
        /// It used to be that heading give or take twenty-five degrees, which was meant to
        /// stop a line of guns looking stamped out and instead made every generated map
        /// look like nobody had aimed them: at rest a side's barrels all point the same way
        /// or the emplacements read as scattered rather than sited.
        /// </para>
        /// </remarks>
        private static void Emplacement(LevelDefinition level, Team side, Vector3 at)
        {
            int placed = LevelEdits.AddStructure(level, StructureKind.Turret, at, side);
            if (placed < 0)
            {
                return;
            }

            level.Structures[placed].Name = Name("Turret", side);
            level.Structures[placed].YawDegrees = LevelEdits.FacingTheEnemy(side);
        }

        /// <summary>
        /// Lays one side's wall runs across the ground in front of its home.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="side">Whose runs.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="count">How many runs there are.</param>
        /// <param name="taken">Spots already used.</param>
        /// <param name="dice">This side's dice.</param>
        /// <remarks>
        /// <para>
        /// Dealt round the crossings like the emplacements are, and for the same reason: a
        /// crossing is the one place on a map where an attacker's route is already decided,
        /// so it is the one place where a twenty-metre barrier is worth the concrete. Where
        /// there are no crossings there is no such place, and the runs spread along the front
        /// instead.
        /// </para>
        /// <para>
        /// <strong>The middle is settled and the rest is not.</strong> Only the gate has to
        /// find somewhere sensible; the segments either side of it have to be exactly one
        /// segment apart or the run is not a run — see <see cref="Rampart"/>. That is the
        /// whole reason this could not be one more loop in <see cref="Scenery"/>:
        /// <see cref="Settle"/> exists to keep things apart, and a wall only reads as a wall
        /// when it touches itself.
        /// </para>
        /// </remarks>
        private static void Ramparts(
            LevelDefinition level,
            Team side,
            Frame frame,
            int count,
            List<Placed> taken,
            Dice dice)
        {
            float[] crossings = Crossings(frame);

            // One run per crossing at most. Two runs dealt the same crossing would want the
            // same spot with only the depth jitter between them, and land as a double wall
            // four metres apart - which is not two defences, it is one defence drawn twice.
            // An asymmetrical channel can come out with as few as two crossings, so this is
            // reachable at the hard setting rather than theoretical.
            int runs = crossings.Length > 0 ? Mathf.Min(count, crossings.Length) : count;

            for (int run = 0; run < runs; run++)
            {
                // Dead centre on the crossing, with no jitter across at all - which is the
                // opposite of what every other placement here does and is the whole point of
                // this one. The gate is the middle segment, so centring the run on the road
                // puts the gate on the road; nudging it even a few metres sideways puts a
                // *wall* on the road instead, and the side that built it then has to shoot
                // its own rampart to use its own causeway. The variety this gives up comes
                // back as the depth jitter below and the rolled length.
                float across = crossings.Length > 0
                    ? crossings[run % crossings.Length] / frame.Extent
                    : dice.Between(-0.40f, 0.40f);

                float outward = RampartOut + dice.Spread(0.03f);

                // The run lies across the line of advance, so its heading is the one that
                // keeps the same distance from home. Taken as the difference between two
                // points either side of it rather than as world X, because on a lagoon that
                // heading is a tangent to the ring and on everything else it is X - and Where
                // is the one place that fold is written down. Measured off the asked-for spot
                // rather than the settled one, so a run that had to shuffle inland still lies
                // the way the map wanted it to.
                Vector3 along = Where(frame, side, outward, across + 0.05f)
                    - Where(frame, side, outward, across - 0.05f);

                // Skipped rather than forced when there is nowhere for it, exactly as an
                // emplacement is: a wall is how heavily a map is defended rather than
                // something it owes, and one in the sea fails the whole map.
                if (!Settle(level, Where(frame, side, outward, across), LevelValidation.ShoreMargin,
                    RampartRoom, taken, dice, out Vector3 at))
                {
                    continue;
                }

                // Settle reserves the spot it found, and the gate is about to ask whether that
                // spot is free. Taking the reservation back is what stops the run being
                // crowded out by its own placeholder - and it is safe to take the last one,
                // because Settle appends exactly one entry and nothing has run since.
                taken.RemoveAt(taken.Count - 1);

                Rampart(level, side, at, along, 3 + (2 * dice.Upto(3)), taken);
            }
        }

        /// <summary>
        /// Lays a run of wall outward from a gate belonging to one side.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="side">Whose gate stands in the middle of it.</param>
        /// <param name="middle">Where the gate goes.</param>
        /// <param name="along">Which way the run lies; its length is ignored.</param>
        /// <param name="segments">How long the run is, gate included. Odd.</param>
        /// <param name="taken">Spots already used.</param>
        /// <returns>How many segments were actually laid, or zero when none were.</returns>
        /// <remarks>
        /// <para>
        /// <strong>Grown outward from the gate, not laid end to end.</strong> A run that meets
        /// the water halfway comes out short at that end with its gate still in it; laid from
        /// one end, the arm that ran out of land would be the one carrying the gate, and the
        /// side that built the wall would have walled itself in.
        /// </para>
        /// <para>
        /// The gate is the middle segment, which is the only place it is worth having: a gate
        /// at the end of a run is one an attacker drives round rather than one they have to
        /// answer. It is also why the count is odd — an even run has no middle.
        /// </para>
        /// <para>
        /// The segments are placed rather than settled, and the pitch is exactly
        /// <see cref="LevelEdits.SegmentLength"/>: two neighbours have to butt into one wall
        /// with a single pier at the join, which is the whole difference between a wall and a
        /// row of boxes. They still go into <paramref name="taken"/>, so everything placed
        /// after keeps its own distance from the finished run.
        /// </para>
        /// </remarks>
        private static int Rampart(
            LevelDefinition level,
            Team side,
            Vector3 middle,
            Vector3 along,
            int segments,
            List<Placed> taken)
        {
            var step = new Vector3(along.x, 0.0f, along.z);
            if (segments < 1 || step.sqrMagnitude < 0.0001f)
            {
                return 0;
            }

            step = step.normalized;

            // Unity's yaw is clockwise from +Z and a segment is modelled running along its own
            // X - see prop_wall.py's facing note - so this is the heading that turns world X
            // onto the run. A wall reads the same from either end, so the opposite heading
            // would do just as well.
            float yaw = Mathf.Repeat(Mathf.Atan2(-step.z, step.x) * Mathf.Rad2Deg, 360.0f);

            // No gate, no run. A run the generator meant to be somebody's, laid without the
            // one segment that makes it theirs, is a plain wall across their own approach -
            // which is worse for the side that built it than no wall at all, and looks
            // exactly like a wall that is working.
            if (!Lay(level, StructureKind.Door, side, Name("Gate", side), middle, yaw, taken))
            {
                return 0;
            }

            int laid = 1;

            foreach (int way in new[] { -1, 1 })
            {
                for (int arm = 1; arm <= (segments - 1) / 2; arm++)
                {
                    Vector3 at = middle + (step * (way * arm * LevelEdits.SegmentLength));
                    if (!Lay(level, StructureKind.Wall, Team.None, Name("Rampart", side), at, yaw, taken))
                    {
                        break;
                    }

                    laid++;
                }
            }

            if (laid > 1)
            {
                return laid;
            }

            // A gate with no wall either side of it is not a gate, it is a five-metre kerb
            // with a door in it that anybody walks round - and it is worse than nothing,
            // because it costs its owner sixty hit points of their own line for no barrier at
            // all. Both arms failing at once means the middle was the only dry, clear ground
            // there was, so the run comes out rather than coming out wrong. Taking the last
            // structure and the last reservation is safe for the same reason as above:
            // nothing has been placed since the gate.
            LevelEdits.Remove(
                level, new EditSelection(EditTarget.Structure, level.Structures.Length - 1));
            taken.RemoveAt(taken.Count - 1);
            return 0;
        }

        /// <summary>
        /// Puts one segment of a run down, if there is dry ground for it.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="kind">A wall or a gate.</param>
        /// <param name="side">Whose it is; nobody, for a wall.</param>
        /// <param name="called">What to call it in the hierarchy.</param>
        /// <param name="at">Where it stands.</param>
        /// <param name="yaw">Which way the run lies.</param>
        /// <param name="taken">Spots already used.</param>
        /// <returns><c>true</c> when a segment was laid.</returns>
        /// <remarks>
        /// A wall belongs to nobody even when it is part of somebody's rampart, because that
        /// is what <see cref="StructureTuning.BelongsToASide"/> says and a level that disagrees
        /// is refused. The run's side survives in the segment's <em>name</em>, which is also
        /// what <see cref="Recoloured"/> flips when a mirrored map turns the run over.
        /// </remarks>
        private static bool Lay(
            LevelDefinition level,
            StructureKind kind,
            Team side,
            string called,
            Vector3 at,
            float yaw,
            List<Placed> taken)
        {
            if (!level.IsOnLand(at, LevelValidation.ShoreMargin)
                || Crowded(taken, at, RampartRoom))
            {
                return false;
            }

            int placed = LevelEdits.AddStructure(level, kind, at, side);
            if (placed < 0)
            {
                return false;
            }

            level.Structures[placed].Name = called;
            level.Structures[placed].YawDegrees = yaw;
            taken.Add(new Placed(at, RampartRoom));
            return true;
        }

        /// <summary>
        /// Scatters one side's buildings and trees over its half of the map.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="side">Whose half.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="tier">How much of it there is.</param>
        /// <param name="taken">Spots already used.</param>
        /// <param name="dice">This side's dice.</param>
        /// <remarks>
        /// <strong>Scenery that finds nowhere to stand is not placed.</strong> Nothing about a
        /// map requires the twentieth tree, and the alternative - putting it where it was
        /// wanted anyway - is a prop in the sea, which is a fault the whole map then gets
        /// re-rolled over. A tier is how much cover a map is aiming for rather than a count it
        /// owes, so a crowded corner of a hard island comes out one tree short and nobody can
        /// tell. The things a map genuinely owes - its bunker, its towers, its depots - are
        /// placed whatever happens, so that the fault names the thing to drag.
        /// </remarks>
        private static void Scenery(
            LevelDefinition level,
            Team side,
            Frame frame,
            Tier tier,
            List<Placed> taken,
            Dice dice)
        {
            for (int block = 0; block < tier.Buildings; block++)
            {
                Vector3 wanted = Where(
                    frame,
                    side,
                    dice.Between(0.16f, 0.68f),
                    dice.Between(-0.48f, 0.48f));

                if (!Settle(level, wanted, LevelValidation.ShoreMargin, BuildingRoom, taken, dice,
                    out Vector3 at))
                {
                    continue;
                }

                int placed = LevelEdits.AddStructure(
                    level,
                    dice.Chance(0.5f) ? StructureKind.BuildingA : StructureKind.BuildingB,
                    at);
                if (placed >= 0)
                {
                    level.Structures[placed].YawDegrees = dice.Between(0.0f, 360.0f);
                }
            }

            for (int tree = 0; tree < tier.Trees; tree++)
            {
                Vector3 wanted = Where(
                    frame,
                    side,
                    dice.Between(0.14f, 0.74f),
                    dice.Between(-0.52f, 0.52f));

                if (!Settle(level, wanted, LevelValidation.ShoreMargin, TreeRoom, taken, dice,
                    out Vector3 at))
                {
                    continue;
                }

                int placed = LevelEdits.AddStructure(level, StructureKind.Tree, at);
                if (placed >= 0)
                {
                    level.Structures[placed].YawDegrees = dice.Between(0.0f, 360.0f);
                }
            }
        }

        /// <summary>
        /// Finds a spot near the one a layout asked for that something can actually stand on.
        /// </summary>
        /// <param name="level">The level, with all its land drawn.</param>
        /// <param name="wanted">Where the layout would like it.</param>
        /// <param name="margin">Metres of dry land it needs around it.</param>
        /// <param name="room">Metres it wants kept clear of everything else.</param>
        /// <param name="taken">Spots already used; the one chosen is added to it.</param>
        /// <param name="dice">Dice, for which way to start looking.</param>
        /// <param name="found">Where it ended up. The wanted spot when nowhere worked.</param>
        /// <returns><c>true</c> when somewhere good was found.</returns>
        /// <remarks>
        /// <para>
        /// <strong>The one place placement rules live.</strong> Measured against
        /// <see cref="LevelDefinition.IsOnLand"/>, which reads the realised coast rather than
        /// the drawn rectangles - so something a wandering coastline would leave standing in
        /// the sea is never placed there in the first place, and something a metre inside the
        /// seam between two overlapping rectangles is not refused by both of them.
        /// </para>
        /// <para>
        /// Rings outward at a widening step from a rolled heading, so two things asking for
        /// the same spot do not both walk the same way out of it. It gives up at not quite
        /// half the world, which is far enough to rescue a bad draw and short enough that a
        /// tree meant for the north shore never turns up on the south one.
        /// </para>
        /// <para>
        /// A failure still records the spot and still hands it back, and the caller still
        /// places there. That is deliberate: a map missing its bunker fails validation with a
        /// sentence about a missing bunker, which is no use to anybody, while a map with its
        /// bunker in the sea fails with a sentence naming exactly what to drag.
        /// </para>
        /// </remarks>
        private static bool Settle(
            LevelDefinition level,
            Vector3 wanted,
            float margin,
            float room,
            List<Placed> taken,
            Dice dice,
            out Vector3 found)
        {
            float turn = dice.Between(0.0f, Mathf.PI * 2.0f);
            float reach = Mathf.Abs(level.Bounds.HalfExtent) * 0.45f;

            for (float away = 0.0f; away <= reach; away = away < 1.0f ? 3.0f : away * 1.5f)
            {
                // A ring of twelve headings is four metres apart at three metres out and forty
                // metres apart at seventy, which is coarse enough for a wide ring to step over
                // a headland entirely. Kept to roughly one heading per two metres of
                // circumference instead, so the search does not get blinder the further out it
                // has to look - which is exactly when it needs to see.
                int headings = away <= 0.0f ? 1 : Mathf.Clamp(Mathf.CeilToInt(away), 8, 32);
                for (int step = 0; step < headings; step++)
                {
                    float angle = turn + (step * Mathf.PI * 2.0f / headings);
                    var at = new Vector3(
                        wanted.x + (Mathf.Cos(angle) * away),
                        0.0f,
                        wanted.z + (Mathf.Sin(angle) * away));

                    if (!level.IsOnLand(at, margin) || Crowded(taken, at, room))
                    {
                        continue;
                    }

                    found = at;
                    taken.Add(new Placed(at, room));
                    return true;
                }
            }

            found = wanted;
            taken.Add(new Placed(wanted, room));
            return false;
        }

        /// <summary>
        /// Reports whether a spot is too close to something already placed.
        /// </summary>
        /// <param name="taken">Spots already used.</param>
        /// <param name="at">The spot being tried.</param>
        /// <param name="room">Metres the new thing wants kept clear.</param>
        /// <returns><c>true</c> when something is inside either of their claims.</returns>
        /// <remarks>
        /// The wider of the two claims wins, so a tree cannot creep up on a bunker just
        /// because a tree only asks for five metres.
        /// </remarks>
        private static bool Crowded(List<Placed> taken, Vector3 at, float room)
        {
            foreach (Placed spot in taken)
            {
                float needed = Mathf.Max(room, spot.Room);
                if ((at - spot.At).sqrMagnitude < needed * needed)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns where on the map a spot described in a side's own terms actually is.
        /// </summary>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="side">Whose terms.</param>
        /// <param name="outward">One deep in this side's home ground, nothing at the seam.</param>
        /// <param name="across">Across the map, as a share of the world, from its centre line.</param>
        /// <returns>A point on the ground plane.</returns>
        /// <remarks>
        /// <para>
        /// Every layout is placed in the same two numbers, which is what stops there being
        /// three copies of "where does a bunker go". On an island or a channel it is what it
        /// reads as: so far out, so far across.
        /// </para>
        /// <para>
        /// A lagoon is the interesting one. Its middle is water, so the same two numbers are
        /// folded onto the ring: the point keeps its bearing from the origin and most of its
        /// distance is squeezed out. Deep home stays deep home, out toward the seam stays out
        /// toward the seam, and everything lands on the band of ground that actually exists.
        /// </para>
        /// <para>
        /// <strong>Squeezed rather than projected outright</strong>, and the difference is
        /// visible from the first render. Snapping every point to the ring's exact radius puts
        /// forty trees on one perfect circle, which reads as a crop mark rather than as
        /// woodland. Keeping a third of what was left of the distance spreads them across the
        /// band instead - a spot the layout wanted near the middle comes out on the inner
        /// shore, one it wanted out at the world's edge comes out on the outer.
        /// </para>
        /// </remarks>
        private static Vector3 Where(Frame frame, Team side, float outward, float across)
        {
            var at = new Vector3(
                across * frame.Extent, 0.0f, Toward(side) * outward * frame.Extent);

            if (frame.Layout != MapLayout.Lagoon)
            {
                return at;
            }

            float bearing = new Vector2(at.x, at.z).magnitude;
            if (bearing < 0.01f)
            {
                return new Vector3(0.0f, 0.0f, Toward(side) * frame.Ring);
            }

            float band = frame.Blob * 0.6f;
            float radius = frame.Ring + Mathf.Clamp((bearing - frame.Ring) * 0.30f, -band, band);
            float onto = radius / bearing;
            return new Vector3(at.x * onto, 0.0f, at.z * onto);
        }

        /// <summary>
        /// Turns every prop from an index onward half a turn, into the other side's hands.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="structuresFrom">First structure to turn.</param>
        /// <param name="towersFrom">First tower to turn.</param>
        /// <param name="bunkersFrom">First bunker to turn.</param>
        /// <remarks>
        /// <para>
        /// A turned tower, bunker or emplacement belongs to the <em>other</em> side, because
        /// the thing at the opposite end of a symmetrical map is the other side's version of
        /// it. Copying the side across instead would hand one player both sets on a map that
        /// still looked symmetrical, which is the worst kind of asymmetry: the invisible kind.
        /// </para>
        /// <para>
        /// The flag is copied explicitly rather than left to
        /// <see cref="LevelEdits.AddTower"/>'s first-one-wins rule. That rule is right for a
        /// hand-built map and wrong here: the first tower turned onto an empty side would be
        /// promoted whether or not its twin was the real one, and half of all mirrored maps
        /// would quietly put the two flags on towers that are not each other's opposite
        /// numbers.
        /// </para>
        /// </remarks>
        private static void TurnProps(
            LevelDefinition level, int structuresFrom, int towersFrom, int bunkersFrom)
        {
            int structures = level.Structures.Length;
            for (int index = structuresFrom; index < structures; index++)
            {
                LevelStructure structure = level.Structures[index];
                if (structure == null)
                {
                    continue;
                }

                int placed = LevelEdits.AddStructure(
                    level,
                    structure.Structure,
                    LevelEdits.Turned(structure.Position),
                    LevelEdits.Opposite(structure.Team));
                if (placed < 0)
                {
                    continue;
                }

                LevelStructure copy = level.Structures[placed];
                copy.Name = Recoloured(structure.Name);
                copy.YawDegrees = Mathf.Repeat(structure.YawDegrees + 180.0f, 360.0f);
                copy.FuelRate = structure.FuelRate;
                copy.AmmoRate = structure.AmmoRate;
                copy.SupplyRadius = structure.SupplyRadius;
            }

            int towers = level.Towers.Length;
            for (int index = towersFrom; index < towers; index++)
            {
                LevelTower tower = level.Towers[index];
                if (tower == null)
                {
                    continue;
                }

                int raised = LevelEdits.AddTower(
                    level, LevelEdits.Opposite(tower.Side), LevelEdits.Turned(tower.Position));
                if (raised < 0)
                {
                    continue;
                }

                level.Towers[raised].YawDegrees = Mathf.Repeat(tower.YawDegrees + 180.0f, 360.0f);

                if (tower.HoldsTheFlag)
                {
                    LevelEdits.MakeRealTower(level, raised);
                }
                else
                {
                    level.Towers[raised].HoldsTheFlag = false;
                }
            }

            int bunkers = level.Bunkers.Length;
            for (int index = bunkersFrom; index < bunkers; index++)
            {
                LevelBunker bunker = level.Bunkers[index];
                if (bunker == null)
                {
                    continue;
                }

                int built = LevelEdits.AddBunker(
                    level, LevelEdits.Opposite(bunker.Side), LevelEdits.Turned(bunker.Position));
                if (built < 0)
                {
                    continue;
                }

                LevelBunker twin = level.Bunkers[built];
                twin.YawDegrees = Mathf.Repeat(bunker.YawDegrees + 180.0f, 360.0f);
                twin.SupplyRadius = bunker.SupplyRadius;
                twin.SupplyRate = bunker.SupplyRate;
            }
        }

        /// <summary>
        /// Returns the x of every crossing on the map, or nothing when it has none.
        /// </summary>
        /// <param name="frame">The shared numbers.</param>
        /// <returns>Where the causeways and bridges are, across the map.</returns>
        private static float[] Crossings(Frame frame)
        {
            if (frame.Layout != MapLayout.Channel)
            {
                return Array.Empty<float>();
            }

            var all = new float[frame.Causeways.Length + frame.Bridges.Length];
            Array.Copy(frame.Causeways, all, frame.Causeways.Length);
            Array.Copy(frame.Bridges, 0, all, frame.Causeways.Length, frame.Bridges.Length);
            return all;
        }

        /// <summary>
        /// Returns which way up the map a side's home is.
        /// </summary>
        /// <param name="side">The side.</param>
        /// <returns>Minus one for green, plus one for brown.</returns>
        /// <remarks>
        /// Green is south and brown is north on every map this game has, including the shipped
        /// one, so a generator that put them anywhere else would be generating maps for a
        /// different game's camera.
        /// </remarks>
        private static float Toward(Team side) => side == Team.Brown ? 1.0f : -1.0f;

        /// <summary>
        /// Draws one ellipse of land, kept inside the world.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="name">What to call it.</param>
        /// <param name="ground">What it is made of.</param>
        /// <param name="centre">Its middle, on the ground plane.</param>
        /// <param name="acrossHalf">Half its width, in metres.</param>
        /// <param name="upHalf">Half its depth, in metres.</param>
        private static void Ellipse(
            LevelDefinition level,
            string name,
            SurfaceKind ground,
            Vector3 centre,
            float acrossHalf,
            float upHalf)
            => Piece(level, name, ground, LandShape.Ellipse, centre, acrossHalf, upHalf);

        /// <summary>
        /// Draws one rectangle of land, kept inside the world.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="name">What to call it.</param>
        /// <param name="ground">What it is made of.</param>
        /// <param name="centre">Its middle, on the ground plane.</param>
        /// <param name="acrossHalf">Half its width, in metres.</param>
        /// <param name="upHalf">Half its depth, in metres.</param>
        private static void Rect(
            LevelDefinition level,
            string name,
            SurfaceKind ground,
            Vector3 centre,
            float acrossHalf,
            float upHalf)
            => Piece(level, name, ground, LandShape.Rectangle, centre, acrossHalf, upHalf);

        /// <summary>
        /// Draws one piece of land, kept inside the world.
        /// </summary>
        /// <param name="level">The level being built.</param>
        /// <param name="name">What to call it.</param>
        /// <param name="ground">What it is made of.</param>
        /// <param name="shape">What outline it is cut to.</param>
        /// <param name="centre">Its middle, on the ground plane.</param>
        /// <param name="acrossHalf">Half its width, in metres.</param>
        /// <param name="upHalf">Half its depth, in metres.</param>
        /// <remarks>
        /// <para>
        /// The clamp is what makes "runs off the edge of the world" a rule a generated map
        /// cannot break rather than one it is scored on. A natural coast is drawn where the
        /// file says and then wanders, so it is held back by everything the wobble can add -
        /// exactly the room <see cref="LevelValidation"/> demands of it - while a built one is
        /// exactly where it was put and needs none.
        /// </para>
        /// <para>
        /// It fires almost never. Every layout sizes itself to fit, and a shape that reaches
        /// the clamp comes out a little squarer rather than a little missing.
        /// </para>
        /// </remarks>
        private static void Piece(
            LevelDefinition level,
            string name,
            SurfaceKind ground,
            LandShape shape,
            Vector3 centre,
            float acrossHalf,
            float upHalf)
        {
            float room = SurfaceTuning.For(ground).NaturalEdge ? SurfaceNoise.Amplitude : 0.0f;
            float limit = Mathf.Abs(level.Bounds.HalfExtent) - room;

            int drawn = LevelEdits.AddLand(
                level,
                new Vector3(
                    Mathf.Clamp(centre.x - acrossHalf, -limit, limit),
                    0.0f,
                    Mathf.Clamp(centre.z - upHalf, -limit, limit)),
                new Vector3(
                    Mathf.Clamp(centre.x + acrossHalf, -limit, limit),
                    0.0f,
                    Mathf.Clamp(centre.z + upHalf, -limit, limit)),
                name);
            if (drawn < 0)
            {
                return;
            }

            level.Land[drawn].Surface = ground.ToString();
            level.Land[drawn].Shape = shape.ToString();
        }

        /// <summary>
        /// Names something after the side it belongs to.
        /// </summary>
        /// <param name="what">What it is.</param>
        /// <param name="side">Whose it is.</param>
        /// <returns>A name for the hierarchy and the editor's own lists.</returns>
        private static string Name(string what, Team side)
        {
            switch (side)
            {
                case Team.Green:
                    return $"{what} (green)";
                case Team.Brown:
                    return $"{what} (brown)";
                default:
                    return what;
            }
        }

        /// <summary>
        /// Returns a name with its side swapped for the other one.
        /// </summary>
        /// <param name="name">The name, which may not mention a side at all.</param>
        /// <returns>The opposite number's name.</returns>
        /// <remarks>
        /// A turned copy of the green shore is the brown shore, and a hierarchy in which both
        /// are called "Shore (green)" is one where the only way to tell them apart is to read
        /// their coordinates. Every name this class writes is its own, so the swap is a plain
        /// substitution rather than a guess about somebody else's text.
        /// </remarks>
        private static string Recoloured(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            if (name.Contains("(green)"))
            {
                return name.Replace("(green)", "(brown)");
            }

            return name.Contains("(brown)") ? name.Replace("(brown)", "(green)") : name;
        }

        /// <summary>
        /// Rolls a layout.
        /// </summary>
        /// <param name="dice">The map's dice.</param>
        /// <returns>One of the three.</returns>
        private static MapLayout Rolled(Dice dice)
        {
            switch (dice.Upto(3))
            {
                case 0:
                    return MapLayout.Island;
                case 1:
                    return MapLayout.Channel;
                default:
                    return MapLayout.Lagoon;
            }
        }

        /// <summary>
        /// Rolls a name for a map.
        /// </summary>
        /// <param name="layout">What shape of ground it is.</param>
        /// <param name="dice">The map's dice.</param>
        /// <returns>Two words that are at least about this map.</returns>
        private static string Named(MapLayout layout, Dice dice)
        {
            string[] nouns;
            switch (layout)
            {
                case MapLayout.Channel:
                    nouns = ChannelNouns;
                    break;
                case MapLayout.Lagoon:
                    nouns = LagoonNouns;
                    break;
                default:
                    nouns = IslandNouns;
                    break;
            }

            return $"{Adjectives[dice.Upto(Adjectives.Length)]} {nouns[dice.Upto(nouns.Length)]}";
        }

        /// <summary>
        /// Writes down what the map is, in the one place a level file has room for it.
        /// </summary>
        /// <param name="name">What the map is called.</param>
        /// <param name="frame">The shared numbers.</param>
        /// <param name="options">Settled options.</param>
        /// <returns>A description.</returns>
        /// <remarks>
        /// JSON has no comments and this field is the level format's answer to that, so a
        /// generated map says what it was generated from: the seed will draw it again, and
        /// somebody opening it in a year should not have to work out from the coordinates
        /// whether the two halves were meant to match.
        /// </remarks>
        private static string Described(string name, Frame frame, MapOptions options)
        {
            string ground;
            switch (frame.Layout)
            {
                case MapLayout.Channel:
                    ground = $"Two shores facing each other across a channel, joined by "
                        + $"{Count(frame.Causeways.Length, "causeway", "causeways")} nobody can "
                        + $"take away and {Count(frame.Bridges.Length, "bridge", "bridges")} "
                        + "anybody can drop. The causeways are the map's promise that it stays "
                        + "winnable once the bridges are gone.";
                    break;
                case MapLayout.Lagoon:
                    ground = "A ring of land around open water, with a bunker at each end of "
                        + "it. There is no way through the middle, so committing to a flank is "
                        + "the whole decision and turning round costs the long way home.";
                    break;
                default:
                    ground = "One landmass with both bunkers on it. Nothing stops a straight "
                        + "run at the other side except what is standing in the way, so this "
                        + "is a map about cover rather than about crossings.";
                    break;
            }

            Tier tier = TierFor(options.Difficulty);
            string defence = options.IsSolo
                ? string.Empty
                : $"Each side gets {Count(tier.Turrets, "emplacement", "emplacements")} and "
                    + $"{Count(tier.Ramparts, "wall run", "wall runs")}, laid across the ground "
                    + "behind the guns with a gate in the middle of each. A gate drops into the "
                    + "floor for the side that built it, stands there like the wall for the "
                    + "other, and is the softest part of the run to break through. ";

            string sides = options.IsSolo
                ? "One side. Green has a bunker and no flag of its own; brown is a field of "
                    + "towers guarded by emplacements, one of them real. The second seat is "
                    + "left empty when you play it, and the editor's Problems panel judges "
                    + "the map by one-player rules rather than a match's."
                : options.Symmetry == MapSymmetry.Mirrored
                    ? "Both halves are the same shape: everything is placed in pairs rotated "
                        + "half a turn about the origin, so neither side has a shorter run to "
                        + "the other's flag."
                    : "The two halves were drawn separately and are not the same shape. Both "
                        + "sides still get a bunker, a real tower, a decoy and the same count "
                        + "of everything else, and both are joined by the same crossing.";

            return $"{name}. Generated from seed {options.Seed} at the "
                + $"{options.Difficulty.ToString().ToLowerInvariant()} setting, which is a world "
                + $"{frame.Extent * 2.0f:0} m across. {ground} {defence}{sides} Asking for seed "
                + $"{options.Seed} again at these settings draws this map again, down to the "
                + "trees. A generated map is a starting point rather than a finished one: the "
                + "editor's Problems panel is the authority on whether this one is playable, "
                + "and every tool works on it exactly as on a hand-authored map.";
        }

        /// <summary>
        /// Spells a count with the right noun after it.
        /// </summary>
        /// <param name="many">How many.</param>
        /// <param name="one">What one of them is called.</param>
        /// <param name="some">What several of them are called.</param>
        /// <returns>The count and the noun.</returns>
        private static string Count(int many, string one, string some)
            => $"{many} {(many == 1 ? one : some)}";

        /// <summary>
        /// Returns how much of everything one difficulty puts on a map.
        /// </summary>
        /// <param name="difficulty">The setting.</param>
        /// <returns>The counts.</returns>
        /// <remarks>
        /// <para>
        /// Counts only. A turret's health, its rate of fire and its reach are global constants
        /// rather than fields of a level file - see <see cref="MapDifficulty"/> - so what a
        /// harder map has is more ground to cross and more guns covering it, not tougher guns.
        /// </para>
        /// <para>
        /// A solo map's numbers are separate and much larger, because on a solo map the
        /// emplacements are the entire opposition rather than a garrison behind one. The decoy
        /// count is the lever that mode has and a match does not: more towers to open before
        /// the flag turns up.
        /// </para>
        /// </remarks>
        private static Tier TierFor(MapDifficulty difficulty)
        {
            switch (difficulty)
            {
                case MapDifficulty.Easy:
                    return new Tier(1, 1, 9, 3, 3, 4);
                case MapDifficulty.Hard:
                    return new Tier(4, 3, 20, 6, 6, 11);
                default:
                    return new Tier(2, 2, 14, 4, 4, 7);
            }
        }

        /// <summary>
        /// Mixes a seed and an attempt number into a seed of their own.
        /// </summary>
        /// <param name="seed">The asked-for seed.</param>
        /// <param name="attempt">Which attempt.</param>
        /// <returns>A seed for that attempt.</returns>
        /// <remarks>
        /// So that a re-roll is a different map rather than the same one drawn again, and so
        /// that asking twice for one seed gets one map however many re-rolls it took.
        /// </remarks>
        private static int Blend(int seed, int attempt)
        {
            unchecked
            {
                uint mixed = ((uint)seed * 2654435761u) + ((uint)attempt * 40503u) + 2166136261u;
                mixed ^= mixed >> 15;
                mixed *= 2246822519u;
                return (int)(mixed ^ (mixed >> 13));
            }
        }

        /// <summary>
        /// The numbers both halves of a map have to agree about.
        /// </summary>
        /// <remarks>
        /// Rolled once, up front, by <see cref="Plan"/>, and read-only to everything after it.
        /// A causeway belongs to neither side and to both, so it cannot be rolled inside
        /// either side's own pass without the other side having to guess what it rolled.
        /// </remarks>
        private sealed class Frame
        {
            /// <summary>What shape of ground this map is.</summary>
            public MapLayout Layout;

            /// <summary>Half-width of the world, in metres.</summary>
            public float Extent;

            /// <summary>Where across the map the causeways run.</summary>
            public float[] Causeways;

            /// <summary>Where across the map the bridges stand.</summary>
            public float[] Bridges;

            /// <summary>Radius a lagoon's ring of land is centred on, in metres.</summary>
            public float Ring;

            /// <summary>Half-size of one blob of a lagoon's ring, in metres.</summary>
            public float Blob;
        }

        /// <summary>
        /// How much of everything a difficulty puts on a map.
        /// </summary>
        private readonly struct Tier
        {
            /// <summary>Emplacements a side gets on a two-sided map.</summary>
            public readonly int Turrets;

            /// <summary>Wall runs a side gets on a two-sided map, each with a gate in it.</summary>
            public readonly int Ramparts;

            /// <summary>Trees a side gets.</summary>
            public readonly int Trees;

            /// <summary>Buildings a side gets.</summary>
            public readonly int Buildings;

            /// <summary>Flag towers the enemy gets on a solo map, real one included.</summary>
            public readonly int SoloTowers;

            /// <summary>Emplacements the enemy gets on a solo map, altogether.</summary>
            public readonly int SoloTurrets;

            /// <summary>
            /// Makes one tier.
            /// </summary>
            /// <param name="turrets">Emplacements a side gets on a two-sided map.</param>
            /// <param name="ramparts">Wall runs a side gets on a two-sided map.</param>
            /// <param name="trees">Trees a side gets.</param>
            /// <param name="buildings">Buildings a side gets.</param>
            /// <param name="soloTowers">Flag towers the enemy gets on a solo map.</param>
            /// <param name="soloTurrets">Emplacements the enemy gets on a solo map.</param>
            public Tier(
                int turrets, int ramparts, int trees, int buildings, int soloTowers, int soloTurrets)
            {
                Turrets = turrets;
                Ramparts = ramparts;
                Trees = trees;
                Buildings = buildings;
                SoloTowers = soloTowers;
                SoloTurrets = soloTurrets;
            }
        }

        /// <summary>
        /// One shape of ground, worked out but not yet written.
        /// </summary>
        /// <remarks>
        /// The generator draws the whole map in these before it writes a single rectangle, so
        /// that the rectangles can come out in surface order rather than in the order somebody
        /// happened to think of them - see <see cref="Ground"/> for why that matters. It also
        /// makes turning a half-map into a whole one a copy of a list rather than a second
        /// pass over a level file.
        /// </remarks>
        private readonly struct Patch
        {
            /// <summary>What it is, for the hierarchy.</summary>
            public readonly string What;

            /// <summary>What the grass laid inside it is called, or empty when it gets none.</summary>
            public readonly string Turf;

            /// <summary>Whose half it is on, or nobody for the ground both halves share.</summary>
            public readonly Team Side;

            /// <summary>Its middle, on the ground plane.</summary>
            public readonly Vector3 At;

            /// <summary>Half its width, in metres.</summary>
            public readonly float Across;

            /// <summary>Half its depth, in metres.</summary>
            public readonly float Up;

            /// <summary>Metres of itself left showing around the grass inside it.</summary>
            public readonly float Beach;

            /// <summary>
            /// Works out one shape.
            /// </summary>
            /// <param name="what">What it is.</param>
            /// <param name="turf">What the grass inside it is called, or empty for none.</param>
            /// <param name="side">Whose half it is on.</param>
            /// <param name="at">Its middle.</param>
            /// <param name="across">Half its width, in metres.</param>
            /// <param name="up">Half its depth, in metres.</param>
            /// <param name="beach">Metres of itself left showing around its grass.</param>
            public Patch(
                string what, string turf, Team side, Vector3 at, float across, float up, float beach)
            {
                What = what;
                Turf = turf;
                Side = side;
                At = at;
                Across = across;
                Up = up;
                Beach = beach;
            }

            /// <summary>
            /// Returns this shape rotated half a turn about the origin, on the other side.
            /// </summary>
            /// <returns>Its opposite number.</returns>
            public Patch Turned()
                => new Patch(
                    What, Turf, LevelEdits.Opposite(Side), LevelEdits.Turned(At), Across, Up, Beach);
        }

        /// <summary>
        /// A spot on the map something is already standing on, and how much of it it wants.
        /// </summary>
        private readonly struct Placed
        {
            /// <summary>Where it stands.</summary>
            public readonly Vector3 At;

            /// <summary>Metres around it that nothing else may use.</summary>
            public readonly float Room;

            /// <summary>
            /// Records one spot.
            /// </summary>
            /// <param name="at">Where it stands.</param>
            /// <param name="room">Metres around it that nothing else may use.</param>
            public Placed(Vector3 at, float room)
            {
                At = at;
                Room = room;
            }
        }
    }
}
