using System;
using System.Collections.Generic;
using UnityEngine;

namespace IronFlag.Levels
{
    /// <summary>
    /// What one surface is: what it is painted, what it does to a vehicle standing on it,
    /// and what its coastline is allowed to look like.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A table in one file, for the same reason as
    /// <see cref="IronFlag.Vehicles.VehicleTuning.For"/>,
    /// <see cref="IronFlag.Combat.WeaponTuning.For"/> and
    /// <see cref="IronFlag.Destruction.StructureTuning.For"/>: a handful of rows balanced by
    /// being read against each other is something a diff can show and a handful of assets
    /// cannot. Adding a surface is one enum member, one case here, and
    /// <c>Tools &gt; IronFlag &gt; Build Level Catalog</c>.
    /// </para>
    /// <para>
    /// A level says <em>where</em> each surface is; this says what it does. That is the same
    /// split <see cref="LevelStructure"/> draws against
    /// <see cref="IronFlag.Destruction.StructureTuning"/> - a level places a building, it
    /// does not decide how tough a building is - and it is what keeps two maps from being
    /// two games.
    /// </para>
    /// <para>
    /// The one thing that argues the other way is the in-game level editor, which would
    /// like to add a surface without a recompile. That is not worth designing for yet, and
    /// the seam is cheap if it ever is: <see cref="For"/> is a single function to swap for a
    /// lookup into a loaded table.
    /// </para>
    /// <para>
    /// <strong>Colours are written the way URP takes them</strong>, which is gamma - a
    /// <c>_BaseColor</c> is an sRGB value even though the project renders linear, so 0.15
    /// here means the 38 a colour picker would show and not the 98 that squaring it back
    /// would. The number in each comment is the other one worth knowing: what the surface
    /// actually came out as in the map shot, measured rather than predicted, on the
    /// nought-to-255 scale. Value contrast, not hue, is what a player reads at thirty-four
    /// metres - M7 established that the hard way, with a sea that was only a different
    /// colour from the land and all but vanished - so a new row is argued in that column or
    /// it is not argued at all.
    /// </para>
    /// <para>
    /// <strong>Those measured numbers were taken before there was a tone curve</strong>, and
    /// they have not been re-measured since <see cref="IronFlag.Core.PostTuning"/> arrived.
    /// Neutral tone mapping rolls the top of the range off, so the bright end of the ramp now
    /// comes out lower than the comments below claim - sand measures 168 on the map shot where
    /// this says 190, while asphalt only moves from 117 to 115 and the two waters barely move
    /// at all. The ordering and the value gaps that the ramp is actually argued on all survive;
    /// what is stale is the absolute figure at the top. Re-measuring the whole ramp off a fresh
    /// map shot is the follow-up, and it belongs with a change to these colours rather than
    /// with the change that moved them.
    /// </para>
    /// <para>
    /// Those measured numbers are not a function of the colour alone:
    /// <see cref="Smoothness"/> moves them, and by more than you would expect. The ground is
    /// one enormous flat plane under a sun at 52 degrees, so a rough surface scatters more of
    /// it back at a camera looking straight down than a smooth one does - the two matte
    /// waters come out around a third brighter than their colours alone would put them.
    /// Change a smoothness and the ramp has to be measured again.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// SurfaceTuning surface = SurfaceTuning.For(piece.Ground);
    /// slab.GetComponent&lt;Renderer&gt;().sharedMaterial = catalog.MaterialFor(piece.Ground);
    /// </code>
    /// </example>
    [Serializable]
    public sealed class SurfaceTuning
    {
        /// <summary>How much of a surface's colour the bank below it keeps.</summary>
        /// <remarks>
        /// <para>
        /// The coastline is a wall now rather than the side of a box, so it needs a colour,
        /// and it is this one rather than a sixth row in the table: a bank is not a surface
        /// - nothing stands on it, nothing drives on it, nothing about it has grip - it is
        /// the same ground seen from the side. Deriving it means the map cannot grow a
        /// colour that is not in the ramp, and a repaint of a surface repaints its coast
        /// with it.
        /// </para>
        /// <para>
        /// One number for every surface rather than a column, because there is no argument
        /// yet for a sand coast being a different darkness from an asphalt one. It is a
        /// column's worth of work to split if there ever is.
        /// </para>
        /// <para>
        /// Note that the bank is already darker than the ground above it before this is
        /// applied: it is a vertical face under a sun at 52 degrees, so it catches about
        /// four-fifths of what the flat top does at best and none of it at worst. This is a
        /// step on top of that, and like every other number here it is measured off a render
        /// rather than predicted.
        /// </para>
        /// </remarks>
        public const float BankShade = 0.7f;

        /// <summary>What it is painted, as URP takes a base colour: sRGB, not linear.</summary>
        [Tooltip("What it is painted, as URP takes a base colour: sRGB, not linear.")]
        public Color Colour = Color.grey;

        /// <summary>
        /// Which layer of the stack it is drawn in, lowest first.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The map is a stack of flat sheets rather than one sheet cut into pieces: open sea
        /// at the bottom, then the shelf, then sand, then grass, then whatever was built.
        /// Each sheet is drawn over every sheet above it in the stack as well as its own
        /// cells, so the boundaries between them are automatic and exact - there is no
        /// shared edge to stitch and no crack for the sea to show through, because every
        /// gap is already filled by the thing that belongs just outside it.
        /// </para>
        /// <para>
        /// Read as an order rather than as heights. Which sheet sits on top of which is what
        /// this says; how far apart they are drawn is
        /// <see cref="LevelBuilder"/>'s business, and a sheet nobody's surface occupies is
        /// not drawn at all.
        /// </para>
        /// </remarks>
        [Tooltip("Which layer of the stack it is drawn in, lowest first.")]
        public int Layer;

        /// <summary>How glossy it is: zero for matte, one for a mirror.</summary>
        /// <remarks>
        /// Low all round, and zero on both waters. A gloss highlight under a sun at 52
        /// degrees is what made the first sea on this map read <em>lighter</em> than the land
        /// it is meant to contrast with, and the ground is one enormous flat plane seen from
        /// a fixed angle - which is the shape that shows a specular lobe the worst.
        /// </remarks>
        [Range(0.0f, 1.0f)]
        [Tooltip("How glossy it is. Low all round; both waters are matte on purpose.")]
        public float Smoothness = 0.1f;

        /// <summary>Multiplies top speed, acceleration and turn rate.</summary>
        /// <remarks>
        /// <para>
        /// Not braking, which is deliberate: soft ground that also took the brakes away
        /// would make a beach a death trap rather than a slow lane, and a vehicle crossing
        /// from a road onto sand needs to be able to shed the difference.
        /// </para>
        /// <para>
        /// How much of this a given vehicle actually feels is
        /// <see cref="IronFlag.Vehicles.VehicleTuning.SurfaceSensitivity"/>, and the two
        /// together are <see cref="IronFlag.Vehicles.GroundVehicleMotion.Traction"/>. So the
        /// number here is what the ground offers rather than what any particular vehicle
        /// gets: sand takes a fifth off the jeep and a twentieth off the tank out of this one
        /// figure.
        /// </para>
        /// </remarks>
        [Tooltip("Multiplies top speed, acceleration and turn rate. Never braking.")]
        public float Grip = 1.0f;

        /// <summary>Multiplies the engine's demand on the fuel pool.</summary>
        /// <remarks>
        /// The working half of the demand only - an engine idling on a beach is doing the
        /// same work as one idling on a road - and paid in full by every vehicle, unlike
        /// <see cref="Grip"/>. See
        /// <see cref="IronFlag.Supply.VehicleSupply.DrawFor(float, float, float)"/>.
        /// </remarks>
        [Tooltip("Multiplies the working part of the engine's demand on the fuel pool.")]
        public float FuelDraw = 1.0f;

        /// <summary>Whether a vehicle standing here is lost.</summary>
        /// <remarks>
        /// Also the answer to "may a level paint a rectangle of land with this?", which is
        /// the same question asked from the other end: the two surfaces that drown you are
        /// exactly the two that are not ground.
        /// </remarks>
        [Tooltip("Whether a vehicle standing here is lost.")]
        public bool Drowns;

        /// <summary>Whether its coastline is displaced by noise, or kept exactly as drawn.</summary>
        /// <remarks>
        /// What makes a road read as built: dead straight and hard-edged, precisely because
        /// everything around it is not. A surface that says no here keeps its edges to the
        /// millimetre and holds the water in front of them still as well - see
        /// <see cref="SurfaceNoise.Guard"/> - which is what keeps a 12 m causeway 12 m wide
        /// and each bridgehead's narrows at the 13 m a test measured the jeep's jump
        /// against.
        /// </remarks>
        [Tooltip("Whether its coastline is displaced by noise, or kept exactly as drawn.")]
        public bool NaturalEdge = true;

        /// <summary>What it gives its own coastline up to, or nothing.</summary>
        /// <remarks>
        /// <para>
        /// Two surfaces on this map are two things at their edges: open country is a beach
        /// where it meets the water, and the open sea is a pale shelf where it meets the
        /// land. Written as one column rather than as a beach rule and a shelf rule, because
        /// it is one sentence twice - a surface hands its outermost metres to another one -
        /// and two rules that say the same thing are two rules that can stop saying it.
        /// </para>
        /// <para>
        /// It is also what makes a beach and a shelf free to author and impossible to
        /// forget. Neither is drawn in a level file; both are derived from distance to the
        /// realised coast by <see cref="SurfaceField"/>, so every island has them and no
        /// island has to remember them.
        /// </para>
        /// </remarks>
        [Tooltip("What it gives its own coastline up to: sand for grass, the shelf for the sea.")]
        public SurfaceKind RimSurface = SurfaceKind.None;

        /// <summary>How many metres of its coastline it gives up, or zero.</summary>
        /// <remarks>
        /// Measured from the water's edge, inland for a beach and out to sea for a shelf.
        /// The two numbers are the most likely in this table to be wrong and the cheapest in
        /// it to change, and they are read against each other: the shelf is the wider of the
        /// two because it has the harder job - a beach only has to mark the waterline,
        /// whereas the shelf is what makes an island sit <em>in</em> the water rather than
        /// on top of it.
        /// </remarks>
        [Tooltip("Metres of its coastline it gives up to its rim surface, or zero.")]
        public float RimWidth;

        /// <summary>What the bank below it is painted.</summary>
        /// <remarks>
        /// Its own colour, taken down by <see cref="BankShade"/>. Derived rather than
        /// written so that a coast cannot be a colour the surface above it is not; see
        /// <see cref="BankShade"/> for why the bank is not a row of its own.
        /// </remarks>
        public Color Bank => new Color(
            Colour.r * BankShade, Colour.g * BankShade, Colour.b * BankShade, Colour.a);

        /// <summary>
        /// Returns the numbers for one surface.
        /// </summary>
        /// <param name="kind">Surface to look up.</param>
        /// <returns>
        /// A fresh copy, so callers can stamp and edit it. An unrecognised surface -
        /// including <see cref="SurfaceKind.None"/> - answers with the
        /// <see cref="SurfaceKind.Grass"/> row, because the fallback for a surface has to be
        /// something you can drive on.
        /// </returns>
        /// <example>
        /// <code>
        /// SurfaceTuning surface = SurfaceTuning.For(SurfaceKind.Asphalt);
        /// float top = tuning.MaxSpeed * surface.Grip;
        /// </code>
        /// </example>
        public static SurfaceTuning For(SurfaceKind kind)
        {
            switch (kind)
            {
                case SurfaceKind.Sand:
                    return new SurfaceTuning
                    {
                        // Comes out at 190, the lightest thing on the map by half again,
                        // which is what a beach is - and within a couple of points of what
                        // the reference shot's sand measures, which is the one colour on
                        // this map there is an original to check against.
                        Colour = new Color(0.76f, 0.66f, 0.45f),
                        Smoothness = 0.18f,

                        // The bottom of the land stack, so on most maps the sand sheet is
                        // the whole island and everything else is drawn over it. That is
                        // deliberate rather than incidental: sand is what a coast is made
                        // of, so the sheet whose edge is the coastline is the right one to
                        // have painted sand.
                        Layer = 2,

                        // A fifth of the jeep off its top speed, and thirstier for it. Sand
                        // is the surface that makes the road worth using and the tank worth
                        // driving, so it is the row that has to cost something real. The two
                        // numbers pull the same way on purpose: the ground that slows you is
                        // the ground that drinks, so a beach costs time and range together
                        // and the tank - which shrugs off the first - still pays the second.
                        Grip = 0.80f,
                        FuelDraw = 1.15f,
                        NaturalEdge = true,
                    };

                case SurfaceKind.Asphalt:
                    return new SurfaceTuning
                    {
                        // Comes out at 117 against grass at 73: a full half again brighter
                        // than the country it cuts through, and desaturated, so it reads as
                        // built at a glance. Still a clear step below the sand at 190, which
                        // is what will keep the two apart when Phase E rims the shores.
                        Colour = new Color(0.37f, 0.375f, 0.39f),
                        Smoothness = 0.30f,

                        // The top of the stack. A road is the last thing laid down on a
                        // map, which is both how it is drawn and how it got there.
                        Layer = 4,

                        // Above 1.0 on purpose, and the whole point of having roads: the
                        // fastest line across the map should be a line somebody drew, so
                        // that both players know where it is. A road is a fast lane and a
                        // fast lane is an ambush.
                        Grip = 1.06f,
                        FuelDraw = 0.95f,

                        // The one row that is false, and it is not decoration. A built edge
                        // is kept exactly as drawn, which is what keeps a 16 m causeway 16 m
                        // wide and the 13 m channel at each bridgehead exact once Phase C
                        // starts pushing coastlines around.
                        NaturalEdge = false,
                    };

                case SurfaceKind.ShallowWater:
                    return new SurfaceTuning
                    {
                        // Comes out at 53 against an open sea at 38. The first pass at this
                        // measured 48, which is a fifth above the sea and not enough: a shelf
                        // that has to be looked for is not doing the one job it has, which is
                        // to make the island sit in the water rather than on top of it. It
                        // stays far nearer the sea than the sand, though - a pale halo in the
                        // gap between the darkest thing on the map and the lightest is
                        // exactly how the first sea went wrong.
                        Colour = new Color(0.09f, 0.15f, 0.24f),
                        Smoothness = 0.0f,

                        // Over the open sea and under everything else: the shelf is drawn
                        // right in under the island's edge, so the coastline is one line
                        // rather than two that have to agree.
                        Layer = 1,

                        // It drowns you exactly as the open sea does: that is what the
                        // original did, it is what makes an amphibious jeep a real upgrade
                        // rather than a nicety, and it is what makes a blown bridge a kill
                        // zone as well as a severed route. The shelf ships as pure
                        // appearance, so this pass changes no balance and breaks no test,
                        // and the experiment is one word in this row later.
                        Drowns = true,
                        NaturalEdge = true,
                    };

                case SurfaceKind.DeepWater:
                    return new SurfaceTuning
                    {
                        // M7's sea, unchanged to the third decimal, because its darkness is
                        // a measured result rather than a preference: it is what took the
                        // bank from invisible to twice the contrast, on a map where driving
                        // over one costs a vehicle. Comes out at 38, the darkest thing on
                        // the map, which is the anchor the other four are read against.
                        Colour = new Color(0.035f, 0.075f, 0.135f),
                        Smoothness = 0.0f,
                        Drowns = true,

                        // The bottom of everything, and the only layer not cut to a shape:
                        // the open sea is the slab the whole world is laid on, so no gap
                        // anywhere can show something that is not water.
                        Layer = 0,

                        // The open sea has no coastline of its own. The coast is the land's
                        // edge, and it is the land's row that decides whether it wanders.
                        NaturalEdge = true,

                        // Five metres of shelf against four of beach. Wider than the beach
                        // because it is the band that does the work - an island with no
                        // shelf sits on the water like a sticker - and narrower than half
                        // the narrowest water on the shipped map, so that even a thirteen
                        // metre crossing keeps a line of open sea down the middle of it and
                        // does not read as a ford somebody could drive.
                        RimSurface = SurfaceKind.ShallowWater,
                        RimWidth = 5.0f,
                    };

                default:
                    return new SurfaceTuning
                    {
                        // Comes out at 73 against a sea at 38, which is within a hair of
                        // the contrast M7 bought at the coastline with a flat grey ground,
                        // and the reason this is the interior rather than sand: green reads
                        // as "drivable open ground" before it reads as anything else. Grip
                        // and thirst are 1.0 by definition - every other row is argued
                        // against this one.
                        Colour = new Color(0.15f, 0.25f, 0.10f),
                        Smoothness = 0.10f,
                        Layer = 3,
                        Grip = 1.0f,
                        FuelDraw = 1.0f,
                        NaturalEdge = true,

                        // Four metres of sand at the waterline: about a jeep and a half,
                        // wide enough to see from thirty-four metres up and narrow enough
                        // not to be a lane anybody drives down.
                        RimSurface = SurfaceKind.Sand,
                        RimWidth = 4.0f,
                    };
            }
        }

        /// <summary>
        /// Returns every surface the game has, in the order they are declared.
        /// </summary>
        /// <returns>Every member of <see cref="SurfaceKind"/> except the empty one.</returns>
        /// <remarks>
        /// What the level catalog builds a material for, and what a test walks to check that
        /// no row was forgotten. A palette of the surfaces a level may <em>paint land with</em>
        /// is this list filtered on <see cref="Drowns"/>, rather than a second list somebody
        /// has to keep in step with this one.
        /// </remarks>
        public static SurfaceKind[] Roster()
            => new[]
            {
                SurfaceKind.Grass,
                SurfaceKind.Sand,
                SurfaceKind.Asphalt,
                SurfaceKind.ShallowWater,
                SurfaceKind.DeepWater,
            };

        /// <summary>
        /// Returns the surfaces drawn over one of the two things a map is made of, lowest
        /// layer first.
        /// </summary>
        /// <param name="drowns">
        /// <c>true</c> for the water, <c>false</c> for the ground. The same question as
        /// "would this drown you", asked once, so there is no second list of which surfaces
        /// are land to keep in step with the one in the table.
        /// </param>
        /// <returns>Those surfaces, in <see cref="Layer"/> order.</returns>
        /// <remarks>
        /// The order the sheets of a map are laid down in, and therefore the order in which
        /// each one's cells are added to every sheet below it. Walked by the builder that
        /// draws the map and by the tests that check the stack still covers itself, so that
        /// a new surface takes its place in both by having a layer number.
        /// </remarks>
        public static SurfaceKind[] Stack(bool drowns)
        {
            var stacked = new List<SurfaceKind>();
            foreach (SurfaceKind kind in Roster())
            {
                if (For(kind).Drowns == drowns)
                {
                    stacked.Add(kind);
                }
            }

            stacked.Sort((first, second) => For(first).Layer.CompareTo(For(second).Layer));
            return stacked.ToArray();
        }

        /// <summary>
        /// Returns every surface that some other surface rims itself with.
        /// </summary>
        /// <returns>
        /// The rims, in <see cref="Roster"/> order and without repeats; empty when nothing
        /// in the table rims itself with anything.
        /// </returns>
        /// <remarks>
        /// The surfaces no level file draws and every map has anyway. The builder derives them
        /// per cell, from each cell's own row of the table, and never needs the distinct set -
        /// it is the tests that check a coastline actually grew one of everything that walk
        /// this, rather than naming sand and the shelf, so that a third rim in the table does
        /// not need a fourth place to be mentioned.
        /// </remarks>
        public static SurfaceKind[] Rims()
        {
            var rims = new List<SurfaceKind>();
            foreach (SurfaceKind kind in Roster())
            {
                SurfaceTuning surface = For(kind);
                if (surface.RimSurface != SurfaceKind.None
                    && surface.RimWidth > 0.0f
                    && !rims.Contains(surface.RimSurface))
                {
                    rims.Add(surface.RimSurface);
                }
            }

            return rims.ToArray();
        }
    }
}
