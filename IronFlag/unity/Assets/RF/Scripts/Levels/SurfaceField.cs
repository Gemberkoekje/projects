using System;
using System.Collections.Generic;
using UnityEngine;

namespace IronFlag.Levels
{
    /// <summary>
    /// The map, rasterised once: what every square metre of it is made of, and how far that
    /// square metre is from the nearest coastline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is the single source of truth for three questions</strong> that would
    /// otherwise be answered three different ways: what does the ground look like here, what
    /// is a vehicle standing on, and is this dry land. The rectangles in a level file are
    /// what somebody <em>drew</em>; this is what the map actually <em>is</em> once the
    /// derived parts of it - the beach that rims every island, the shelf that rims every
    /// beach, and the coastline pushed about by noise - have been worked out. A level that
    /// answered "is this land" from the rectangles and drew its coast from somewhere else
    /// would eventually put a bunker in the sea and validate clean.
    /// </para>
    /// <para>
    /// Three arrays over the same grid. One holds the surface of each cell. One holds the
    /// <em>signed</em> distance from each cell to the realised coastline, positive on land
    /// and negative in the water, which is both what
    /// <see cref="LevelDefinition.IsOnLand"/> measures a margin against and what decides how
    /// far a beach and a shelf reach in from the water's edge. The third - see
    /// <see cref="Outline"/> - is the shapes the level file drew with the wobble already
    /// added, and it is what the coastline is actually <em>cut</em> from.
    /// </para>
    /// <para>
    /// Those last two are not the same number, and the difference is the whole point of
    /// having both. The measured one has no seam where two rectangles meet, which is what a
    /// placement margin needs and what an outline cannot give it; the drawn one is exact
    /// between one cell and the next, which is what a built edge needs and what a
    /// measurement rounded to a grid cannot give it.
    /// </para>
    /// <para>
    /// Built from a <see cref="LevelDefinition"/> and nothing else, with no randomness in
    /// it, so the copy of a map baked into a scene and the copy the loader builds from the
    /// file are the same map - which the sandbox wiring tests compare prop for prop, and
    /// which anything drawing on <see cref="UnityEngine.Random"/> would break, and break
    /// intermittently.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// SurfaceField field = level.Field;
    /// SurfaceKind under = field.At(vehicle.transform.position);
    /// bool dry = field.IsLand(at);
    /// </code>
    /// </example>
    public sealed class SurfaceField
    {
        /// <summary>How fine the map is measured, in metres.</summary>
        /// <remarks>
        /// A three-way trade between memory, how fine a coastline can wiggle, and how long
        /// anything that walks the whole grid takes. One metre is about a third of the
        /// widest vehicle in the game, which is fine enough that a beach four metres wide
        /// has four steps in it and coarse enough that a two-hundred-and-forty-metre world
        /// is a surface and a distance for each of fifty-seven thousand cells, or about half
        /// a megabyte.
        /// </remarks>
        public const float CellSize = 1.0f;

        /// <summary>Most cells the grid is allowed across, whatever the world's size.</summary>
        /// <remarks>
        /// A level's half-extent is typed into an editor, so it is a number a person can get
        /// wrong by three digits. Without a cap, a slip like that is not a bad map, it is an
        /// allocation that takes the process with it. Past this the cells get bigger rather
        /// than more numerous, and everything goes on working, only coarser.
        /// </remarks>
        public const int MostCellsAcross = 512;

        /// <summary>How much of a flattened map is about the world rather than the land.</summary>
        private const int Head = 2;

        /// <summary>How many numbers of a flattened map one piece of land is worth.</summary>
        private const int Told = 6;

        /// <summary>How many times a counted distance is blurred before it is used.</summary>
        /// <remarks>
        /// Two passes of a five-point blur, which is about a cell's worth of smoothing:
        /// enough to take a one-metre staircase down to something under a handspan, and
        /// little enough that a boundary is still within half a cell of where the cells put
        /// it. See <see cref="Soften"/>.
        /// </remarks>
        private const int Passes = 2;

        /// <summary>Squared distance standing for "nothing to measure from, this way".</summary>
        /// <remarks>
        /// Reckoned per grid, and larger than any real squared distance across it, which is
        /// what makes it lose every comparison - and no larger than it has to be. A sentinel
        /// of 1e9 would swamp the arithmetic that finds the lower envelope and leave the far
        /// side of an empty map measured in rounding error.
        /// </remarks>
        private const float Unreachable = 4.0f;

        /// <summary>What each cell is made of, row-major from the south-west corner.</summary>
        private readonly SurfaceKind[] cells;

        /// <summary>
        /// Metres to the coastline from each cell, positive on land and negative in water.
        /// </summary>
        private readonly float[] coast;

        /// <summary>The drawn-and-displaced shape of the land, sampled at each cell.</summary>
        private readonly float[] drawn;

        /// <summary>The land this was built from, flattened, for <see cref="Describes"/>.</summary>
        private readonly float[] shape;

        /// <summary>
        /// The seed this was built from, for <see cref="Describes"/>. Kept as an <c>int</c>
        /// rather than read out of <see cref="shape"/>: a seed beyond a float's 24-bit exact
        /// range would compare equal to a different seed if it went through that array.
        /// </summary>
        private readonly int seed;

        /// <summary>Cells across the grid, which is square.</summary>
        private readonly int side;

        /// <summary>Half-width of the world, in metres.</summary>
        private readonly float extent;

        /// <summary>Size of one cell, in metres.</summary>
        private readonly float cell;

        private SurfaceField(
            SurfaceKind[] surfaces,
            float[] distances,
            float[] outline,
            float[] land,
            int levelSeed,
            int cellsAcross,
            float halfExtent,
            float cellSize)
        {
            cells = surfaces;
            coast = distances;
            drawn = outline;
            shape = land;
            seed = levelSeed;
            side = cellsAcross;
            extent = halfExtent;
            cell = cellSize;
        }

        /// <summary>Cells across the grid, which is square.</summary>
        public int Side => side;

        /// <summary>Size of one cell, in metres.</summary>
        /// <remarks>
        /// <see cref="CellSize"/>, unless the world is too big to cover with that many of
        /// them; see <see cref="MostCellsAcross"/>.
        /// </remarks>
        public float Cell => cell;

        /// <summary>Half-width of the world this covers, in metres.</summary>
        public float Extent => extent;

        /// <summary>
        /// Rasterises a whole map.
        /// </summary>
        /// <param name="level">The map to measure.</param>
        /// <returns>The field. Never <c>null</c>: a level with no land at all is all sea.</returns>
        /// <remarks>
        /// <para>
        /// Four steps, in this order, and the order is the whole of the arrangement:
        /// </para>
        /// <list type="number">
        /// <item><description>
        /// The shapes a level drew are measured at every cell and the wobble is added to
        /// them - except within <see cref="SurfaceNoise.Guard"/> of anything built, which
        /// is kept exactly where the file put it. What comes out is
        /// <see cref="Outline"/>: where the water's edge really runs.
        /// </description></item>
        /// <item><description>
        /// Every cell on the land side of that takes a surface: the last shape in the file
        /// that covers it, so overlap is paint order and a road laid over a shore is written
        /// after it - or, for the metre or so of land the wobble made where no shape covers
        /// anything, whichever shape is nearest. Everything else is sea.
        /// </description></item>
        /// <item><description>
        /// The distance to the coastline is measured from the realised land rather than from
        /// the shapes, so an overlap reads as one landmass and never as a seam.
        /// </description></item>
        /// <item><description>
        /// Every surface that rims itself hands its outermost metres to whatever it rims
        /// itself with: grass gives up its coast to sand, the open sea gives up its coast to
        /// the shelf. That is the step that puts a beach around an island nobody drew one
        /// on, and it is one rule reading one column of <see cref="SurfaceTuning"/> rather
        /// than a beach rule and a shelf rule that could drift apart.
        /// </description></item>
        /// </list>
        /// </remarks>
        public static SurfaceField Build(LevelDefinition level)
        {
            float halfExtent = HalfExtentOf(level);
            float cellSize = Mathf.Max(CellSize, halfExtent * 2.0f / MostCellsAcross);
            int cellsAcross = Mathf.Max(1, Mathf.CeilToInt(halfExtent * 2.0f / cellSize));

            SurfaceTuning[] table = Table();
            var surfaces = new SurfaceKind[cellsAcross * cellsAcross];
            var outline = new float[surfaces.Length];
            var wet = new bool[surfaces.Length];
            var dry = new bool[surfaces.Length];

            Cut(level, table, surfaces, outline, cellsAcross, halfExtent, cellSize);

            for (int index = 0; index < surfaces.Length; index++)
            {
                bool drowns = table[(int)surfaces[index]].Drowns;
                wet[index] = drowns;
                dry[index] = !drowns;
            }

            float[] toWater = Spread(wet, cellsAcross);
            float[] toLand = Spread(dry, cellsAcross);
            var distances = new float[surfaces.Length];

            for (int index = 0; index < distances.Length; index++)
            {
                // Half a cell comes off the measured distance, because a cell whose
                // neighbour is wet is a whole cell from that neighbour's centre and half a
                // cell from the water's edge - and it is the edge that a placement margin, a
                // beach and a shelf are every one of them measured from.
                float steps = Mathf.Sqrt(dry[index] ? toWater[index] : toLand[index]);
                float metres = (steps - 0.5f) * cellSize;
                distances[index] = dry[index] ? metres : -metres;
            }

            Rim(surfaces, distances, table);

            return new SurfaceField(
                surfaces, distances, outline, Shape(level), level == null ? 0 : level.Seed,
                cellsAcross, halfExtent, cellSize);
        }

        /// <summary>
        /// Returns what is at a point on the map.
        /// </summary>
        /// <param name="at">Point to look up; its height is ignored.</param>
        /// <returns>
        /// The surface, or <see cref="SurfaceKind.DeepWater"/> off the edge of the world,
        /// which is what is out there.
        /// </returns>
        public SurfaceKind At(Vector3 at)
        {
            int x = ColumnOf(at.x);
            int z = ColumnOf(at.z);
            return Holds(x, z) ? cells[(z * side) + x] : SurfaceKind.DeepWater;
        }

        /// <summary>
        /// Returns what is in one cell.
        /// </summary>
        /// <param name="x">Cell across the map, counted from the west.</param>
        /// <param name="z">Cell up the map, counted from the south.</param>
        /// <returns>The surface, or <see cref="SurfaceKind.DeepWater"/> outside the grid.</returns>
        public SurfaceKind At(int x, int z)
            => Holds(x, z) ? cells[(z * side) + x] : SurfaceKind.DeepWater;

        /// <summary>
        /// Returns where one gridline runs.
        /// </summary>
        /// <param name="index">Cell index; <c>0</c> is the west or south edge of the world.</param>
        /// <returns>Where that gridline is, in metres.</returns>
        /// <remarks>
        /// Cell <c>i</c> runs from <c>Edge(i)</c> to <c>Edge(i + 1)</c>, which is what
        /// anything building geometry out of these cells needs and what
        /// <see cref="At(int, int)"/> does not say.
        /// </remarks>
        public float Edge(int index) => (index * cell) - extent;

        /// <summary>
        /// Returns where one cell's middle is.
        /// </summary>
        /// <param name="index">Cell index, which may be one past either end of the grid.</param>
        /// <returns>Where the middle of that cell is, in metres.</returns>
        /// <remarks>
        /// Cell middles are where the field's numbers actually live, so they are the corners
        /// of the squares anything cutting a shape out of it works over - see
        /// <see cref="SurfaceMesh"/>. Indices either side of the grid answer too, which is
        /// what lets a shape that reaches the last row of cells be closed off rather than
        /// left hanging.
        /// </remarks>
        public float Middle(int index) => ((index + 0.5f) * cell) - extent;

        /// <summary>
        /// Reports whether a point is on dry land.
        /// </summary>
        /// <param name="at">Point to test; its height is ignored.</param>
        /// <returns><c>true</c> when what is there is something a vehicle could stand on.</returns>
        public bool IsLand(Vector3 at) => ToTheCoast(at) > 0.0f;

        /// <summary>
        /// Reports whether a point is on land with room to spare in every direction.
        /// </summary>
        /// <param name="at">Point to test; its height is ignored.</param>
        /// <param name="margin">Metres of clearance demanded from the nearest coastline.</param>
        /// <returns><c>true</c> when the point is that far inside the coast.</returns>
        /// <remarks>
        /// The question <see cref="LevelDefinition.IsOnLand"/> used to ask each rectangle
        /// separately, which was wrong wherever two of them meet: a point a metre inside the
        /// seam between a shore and the bridgehead built onto it is a long way from any
        /// water, and was refused by both rectangles for sitting within a margin of an edge.
        /// Measured against the realised coast, the seam is not there to be refused.
        /// </remarks>
        public bool IsLand(Vector3 at, float margin) => ToTheCoast(at) >= margin;

        /// <summary>
        /// Measures how far a point is from the water's edge.
        /// </summary>
        /// <param name="at">Point to measure; its height is ignored.</param>
        /// <returns>
        /// Metres to the nearest coastline: positive on land, negative in the water, and off
        /// the map as negative as the world is wide.
        /// </returns>
        /// <remarks>
        /// A signed distance field, and deliberately so: it is the one number a beach, a
        /// shelf, every placement margin and a displaced coastline all want, and measuring
        /// it once is what stops those four ending up with different opinions about where
        /// the coast is.
        /// </remarks>
        public float ToTheCoast(Vector3 at)
        {
            int x = ColumnOf(at.x);
            int z = ColumnOf(at.z);
            return Holds(x, z) ? coast[(z * side) + x] : -extent * 2.0f;
        }

        /// <summary>
        /// Measures how far a point is from the water's edge, read between the cells rather
        /// than at them.
        /// </summary>
        /// <param name="at">Point to measure; its height is ignored.</param>
        /// <returns>
        /// Metres to the nearest coastline: positive on land, negative in the water, and off
        /// the map as negative as the world is wide.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The same field <see cref="ToTheCoast"/> reads, bilinearly sampled instead of
        /// looked up. That is the whole difference, and it is a difference of purpose rather
        /// than of accuracy: a margin wants to know which cell it is in and how far inside
        /// that cell is, and cannot see the metre-wide step between one cell and the next.
        /// Something being <em>drawn</em> can - a foam line taken from
        /// <see cref="ToTheCoast"/> comes out as a staircase on exactly the grid the whole
        /// of <see cref="SurfaceMesh"/> exists to hide.
        /// </para>
        /// <para>
        /// Deliberately not a replacement for the other one. Every gameplay margin on this
        /// map is measured against <see cref="ToTheCoast"/> and has been tested at those
        /// numbers; making that method smooth would move all of them by up to half a cell to
        /// buy nothing any rule can see.
        /// </para>
        /// </remarks>
        public float Shore(Vector3 at)
        {
            // Cell centres sit half a cell in from each edge, so the point's position in
            // "which centre" space is half a cell behind its position in "which cell" space.
            float acrossX = ((at.x + extent) / cell) - 0.5f;
            float acrossZ = ((at.z + extent) / cell) - 0.5f;

            int west = Mathf.FloorToInt(acrossX);
            int south = Mathf.FloorToInt(acrossZ);
            float alongX = acrossX - west;
            float alongZ = acrossZ - south;

            float southWest = Nearest(west, south);
            float southEast = Nearest(west + 1, south);
            float northWest = Nearest(west, south + 1);
            float northEast = Nearest(west + 1, south + 1);

            return Mathf.Lerp(
                Mathf.Lerp(southWest, southEast, alongX),
                Mathf.Lerp(northWest, northEast, alongX),
                alongZ);
        }

        /// <summary>
        /// Returns the drawn-and-displaced shape of the land, one number per cell.
        /// </summary>
        /// <returns>
        /// Metres inside the coastline at each cell centre, positive on land, row-major from
        /// the south-west corner. A fresh copy, because it is a picture of the map rather
        /// than a handle on it.
        /// </returns>
        /// <remarks>
        /// <para>
        /// What the coastline is <em>cut</em> from, as against <see cref="ToTheCoast"/>,
        /// which is what a margin is <em>measured</em> against. This one is the shapes a
        /// level drew with the wobble added: exact for anything built, so a bridgehead whose
        /// edge falls half a metre off the grid comes out at the half metre rather than at
        /// the grid, and rounded to half a cell for anything natural, because that is the
        /// price of a coastline with no seam in it where two shapes meet. That other one is
        /// a distance to the realised coast, which is what a margin wants and what an
        /// outline cannot give it.
        /// </para>
        /// <para>
        /// Read by <see cref="SurfaceMesh"/> and by nothing else, so far. Anything wanting
        /// to know how far from the water something is wants <see cref="ToTheCoast"/>.
        /// </para>
        /// </remarks>
        public float[] Outline() => (float[])drawn.Clone();

        /// <summary>
        /// Returns how far inside one surface's layer every cell is.
        /// </summary>
        /// <param name="kind">Surface whose layer to measure.</param>
        /// <returns>
        /// Metres inside that layer at each cell centre, positive inside it, row-major from
        /// the south-west corner.
        /// </returns>
        /// <remarks>
        /// <para>
        /// A layer is not just the cells of one surface: it is those cells <em>and every
        /// surface drawn over it</em> - see <see cref="SurfaceTuning.Layer"/> - so that the
        /// sheets of a map cover each other completely and no gap between two of them can
        /// show anything but the one that belongs just outside.
        /// </para>
        /// <para>
        /// Two measurements, and the smaller wins. One is how far the cell is from the
        /// nearest cell of the same host - land or water - that the layer does <em>not</em>
        /// cover, which is what puts the beach's inland edge where the sand runs out. The
        /// other is how far it is inside the host itself, which is what puts every layer's
        /// edge at the water exactly on the coastline rather than half a cell off it. The
        /// water is deliberately not counted in the first: a road that ends at the sea has
        /// no inland edge there to be found, so it inherits the coastline instead, exactly
        /// as it should.
        /// </para>
        /// </remarks>
        public float[] Layer(SurfaceKind kind)
        {
            SurfaceTuning[] table = Table();
            bool wet = table[(int)kind].Drowns;
            int lowest = table[(int)kind].Layer;

            var covered = new bool[cells.Length];
            var beyond = new bool[cells.Length];

            for (int index = 0; index < cells.Length; index++)
            {
                SurfaceTuning here = table[(int)cells[index]];
                if (here.Drowns != wet)
                {
                    continue;
                }

                if (here.Layer >= lowest)
                {
                    covered[index] = true;
                }
                else
                {
                    beyond[index] = true;
                }
            }

            float[] toBeyond = Spread(beyond, side);
            float[] toCovered = Spread(covered, side);
            var counted = new float[cells.Length];

            for (int index = 0; index < counted.Length; index++)
            {
                float steps = Mathf.Sqrt(covered[index] ? toBeyond[index] : toCovered[index]);
                float metres = (steps - 0.5f) * cell;
                counted[index] = covered[index] ? metres : -metres;
            }

            Soften(counted, side);
            var reach = new float[cells.Length];

            for (int index = 0; index < reach.Length; index++)
            {
                float host = wet ? -drawn[index] : drawn[index];

                // Outside the host - a land layer over the sea, or a water one over the
                // island - the layer's edge is the coastline itself and nothing else, taken
                // measured rather than counted in cells. That is what puts the edge of a
                // road that runs down to the water exactly where the file wrote it instead
                // of half a cell inland with a stripe of beach showing past it.
                if (host <= 0.0f)
                {
                    reach[index] = host;
                    continue;
                }

                reach[index] = Mathf.Min(counted[index], host);
            }

            return reach;
        }

        /// <summary>
        /// Takes the staircase out of a distance counted in cells.
        /// </summary>
        /// <param name="counted">Distances to smooth, in metres, one per cell.</param>
        /// <param name="side">Cells across the grid.</param>
        /// <remarks>
        /// <para>
        /// A boundary between two surfaces that is not the coastline - the inland edge of a
        /// beach, say - is decided a whole cell at a time, so where it runs nearly along the
        /// grid it comes out as long straight runs with a one-metre step every so often. The
        /// steps are real and they are the most visible thing on a map about the only
        /// boundary on it with a strong contrast across it.
        /// </para>
        /// <para>
        /// One blur of the distance rather than of the picture, which is the difference
        /// between softening a boundary and moving it: a blur leaves a field that is already
        /// straight exactly where it was - it is linear, and averaging a straight line gives
        /// the same line - and only rounds off where the cells disagree with each other,
        /// which is precisely the staircase. It cannot move a coastline either, because the
        /// coastline is not in here: <see cref="Layer"/> takes this against the measured
        /// outline afterwards and the smaller of the two wins.
        /// </para>
        /// </remarks>
        private static void Soften(float[] counted, int side)
        {
            var eased = new float[counted.Length];

            for (int pass = 0; pass < Passes; pass++)
            {
                for (int z = 0; z < side; z++)
                {
                    for (int x = 0; x < side; x++)
                    {
                        int at = (z * side) + x;
                        eased[at] = (counted[at] * 0.5f)
                            + (Held(counted, side, x - 1, z) * 0.125f)
                            + (Held(counted, side, x + 1, z) * 0.125f)
                            + (Held(counted, side, x, z - 1) * 0.125f)
                            + (Held(counted, side, x, z + 1) * 0.125f);
                    }
                }

                Array.Copy(eased, counted, counted.Length);
            }
        }

        /// <summary>
        /// Reads one cell of a distance, or the nearest one on the grid.
        /// </summary>
        /// <param name="counted">Distances, one per cell.</param>
        /// <param name="side">Cells across the grid.</param>
        /// <param name="x">Cell across the map.</param>
        /// <param name="z">Cell up the map.</param>
        /// <returns>What is there, or what is at the edge nearest to it.</returns>
        /// <remarks>
        /// Repeating the edge rather than treating the outside as anything in particular: the
        /// alternative is a blur that pulls every layer towards some number at the border,
        /// and the border of the world is the one place nothing should be happening.
        /// </remarks>
        private static float Held(float[] counted, int side, int x, int z)
            => counted[(Mathf.Clamp(z, 0, side - 1) * side) + Mathf.Clamp(x, 0, side - 1)];

        /// <summary>
        /// Reports whether a map has any of one surface on it.
        /// </summary>
        /// <param name="kind">Surface to look for.</param>
        /// <returns><c>true</c> when at least one cell is made of it.</returns>
        /// <remarks>
        /// What lets a layer nobody occupies go undrawn. The lowest layer of a host covers
        /// the whole of it, so a map with no sand on it would otherwise be an island painted
        /// sand with an island painted grass laid exactly over the top.
        /// </remarks>
        public bool Covers(SurfaceKind kind)
        {
            foreach (SurfaceKind here in cells)
            {
                if (here == kind)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reports whether this is still a picture of a given map.
        /// </summary>
        /// <param name="level">The map to check against.</param>
        /// <returns><c>true</c> when nothing that shapes the field has changed.</returns>
        /// <remarks>
        /// What makes a cached field safe to hand out. The level editor moves a coastline
        /// and then expects the very next question about the map to be answered about the
        /// map it has now, and it does not announce that it has done so - so the field
        /// checks rather than being told. Only the land, the seed and the world's size are
        /// compared, because nothing else is rasterised: moving a tower does not move a
        /// coast, and changing the seed moves every coast on the map.
        /// </remarks>
        public bool Describes(LevelDefinition level)
        {
            LevelLand[] land = level == null || level.Land == null
                ? Array.Empty<LevelLand>()
                : level.Land;

            if (shape.Length != (land.Length * Told) + Head
                || shape[0] != HalfExtentOf(level)
                || seed != (level == null ? 0 : level.Seed))
            {
                return false;
            }

            for (int index = 0; index < land.Length; index++)
            {
                LevelLand piece = land[index];
                int at = (index * Told) + Head;

                if (piece == null)
                {
                    if (shape[at] != 0.0f || shape[at + 4] != 0.0f)
                    {
                        return false;
                    }

                    continue;
                }

                if (shape[at] != piece.MinX || shape[at + 1] != piece.MaxX
                    || shape[at + 2] != piece.MinZ || shape[at + 3] != piece.MaxZ
                    || shape[at + 4] != (int)piece.Ground || shape[at + 5] != (int)piece.Form)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Works out where the water's edge really runs, and what is on the land side of it.
        /// </summary>
        /// <param name="level">The map being rasterised.</param>
        /// <param name="table">The surface table, looked up once per kind.</param>
        /// <param name="surfaces">Cells to fill.</param>
        /// <param name="outline">Drawn-and-displaced signed distances to fill.</param>
        /// <param name="cellsAcross">Cells across the grid.</param>
        /// <param name="halfExtent">Half-width of the world, in metres.</param>
        /// <param name="cellSize">Size of one cell, in metres.</param>
        /// <remarks>
        /// <para>
        /// The natural shapes and the built ones are worked out separately, because only one
        /// of the two gets a wobble, and they are worked out in two different ways for a
        /// reason worth reading.
        /// </para>
        /// <para>
        /// <strong>Built land is measured, shape by shape.</strong> Each shape answers how
        /// far inside it a point is - see <see cref="LevelLand.Signed"/> - and the best of
        /// those answers is the map. That is exact between one cell and the next, which is
        /// what keeps a causeway 12 m wide because somebody typed 12, and the narrows at
        /// each bridgehead at the 13 m a test worked out a jeep cannot jump.
        /// </para>
        /// <para>
        /// <strong>Natural land is measured through the cells</strong>, by the same distance
        /// transform everything else on this grid uses. It has to be, and the reason is the
        /// seam: the best answer from a list of shapes is <em>not</em> the distance to the
        /// edge of what they make together, and at a place where two of them meet it is
        /// nearly zero however far inland that place is. Add a metre and a half of wobble to
        /// nearly zero and an island drawn as two rectangles side by side gets a channel cut
        /// down the join. Measured through the realised cells there is no join to cut, at
        /// the cost of the base shape being rounded to half a cell before the wobble - which
        /// is a fifth of the wobble and invisible under it.
        /// </para>
        /// <para>
        /// The wobble itself is held at zero within <see cref="SurfaceNoise.Guard"/> of
        /// anything built and eased back in over <see cref="SurfaceNoise.Blend"/> past it,
        /// on both sides, so a crossing and the water in front of it are exactly where they
        /// were drawn and the shore either side of them still wanders.
        /// </para>
        /// <para>
        /// A cell takes the surface of whichever shape covers its <em>middle</em>, last one
        /// in the file winning, which is the one rule that keeps two shapes sharing an edge
        /// from both claiming the cells along it. The wobble can also make land where no
        /// shape covers anything - never more than <see cref="SurfaceNoise.Amplitude"/>
        /// beyond one - and that takes the surface of the nearest shape, because a metre of
        /// new coast belongs to the shore it grew out of.
        /// </para>
        /// </remarks>
        private static void Cut(
            LevelDefinition level,
            SurfaceTuning[] table,
            SurfaceKind[] surfaces,
            float[] outline,
            int cellsAcross,
            float halfExtent,
            float cellSize)
        {
            LevelLand[] all = level == null || level.Land == null
                ? Array.Empty<LevelLand>()
                : level.Land;
            int seed = level == null ? 0 : level.Seed;

            // Everything about a piece of land that does not vary from cell to cell, read
            // once. Its surface and its shape are words in the file, so asking a rectangle
            // what it is costs a name lookup - which is nothing at all until it happens
            // eight times for each of fifty-seven thousand cells.
            var land = new List<LevelLand>();
            var ground = new List<SurfaceKind>();
            var outlines = new List<LandShape>();
            var wanders = new List<bool>();

            foreach (LevelLand piece in all)
            {
                if (piece == null || !piece.IsDrawn)
                {
                    continue;
                }

                SurfaceKind made = piece.Ground;
                land.Add(piece);
                ground.Add(made);
                outlines.Add(piece.Form);
                wanders.Add(table[(int)made].NaturalEdge);
            }

            // Stands for "nowhere near anything", and is finite so that adding a metre and a
            // half of wobble to it cannot turn it into something a comparison takes
            // seriously.
            float nowhere = -(halfExtent * 4.0f) - SurfaceNoise.Amplitude;

            var built = new float[surfaces.Length];
            var drawnOn = new bool[surfaces.Length];
            var elsewhere = new bool[surfaces.Length];
            var covering = new SurfaceKind[surfaces.Length];
            var nearest = new SurfaceKind[surfaces.Length];

            for (int z = 0; z < cellsAcross; z++)
            {
                float middleZ = ((z + 0.5f) * cellSize) - halfExtent;
                for (int x = 0; x < cellsAcross; x++)
                {
                    float middleX = ((x + 0.5f) * cellSize) - halfExtent;
                    var at = new Vector3(middleX, 0.0f, middleZ);
                    int index = (z * cellsAcross) + x;

                    float nearestBy = nowhere;
                    built[index] = nowhere;
                    covering[index] = SurfaceKind.None;
                    nearest[index] = SurfaceKind.None;

                    for (int piece = 0; piece < land.Count; piece++)
                    {
                        float inside = land[piece].Signed(at, outlines[piece]);

                        if (inside >= 0.0f)
                        {
                            covering[index] = ground[piece];
                        }

                        if (inside >= nearestBy)
                        {
                            nearestBy = inside;
                            nearest[index] = ground[piece];
                        }

                        if (wanders[piece])
                        {
                            drawnOn[index] |= inside >= 0.0f;
                        }
                        else
                        {
                            built[index] = Mathf.Max(built[index], inside);
                        }
                    }

                    elsewhere[index] = !drawnOn[index];
                }
            }

            float[] toEdge = Spread(elsewhere, cellsAcross);
            float[] toShore = Spread(drawnOn, cellsAcross);

            for (int z = 0; z < cellsAcross; z++)
            {
                float middleZ = ((z + 0.5f) * cellSize) - halfExtent;
                for (int x = 0; x < cellsAcross; x++)
                {
                    float middleX = ((x + 0.5f) * cellSize) - halfExtent;
                    int index = (z * cellsAcross) + x;

                    float steps = Mathf.Sqrt(drawnOn[index] ? toEdge[index] : toShore[index]);
                    float metres = (steps - 0.5f) * cellSize;
                    float natural = drawnOn[index] ? metres : -metres;

                    float wobble = SurfaceNoise.At(middleX, middleZ, seed)
                        * SurfaceNoise.Amplitude
                        * SurfaceNoise.Weight(built[index]);

                    float shore = Mathf.Max(built[index], natural + wobble);

                    outline[index] = shore;
                    surfaces[index] = shore <= 0.0f
                        ? SurfaceKind.DeepWater
                        : covering[index] == SurfaceKind.None ? nearest[index] : covering[index];
                }
            }
        }

        /// <summary>
        /// Hands every rimming surface's outermost metres to whatever it rims itself with.
        /// </summary>
        /// <param name="surfaces">Cells to repaint.</param>
        /// <param name="distances">Signed metres to the coast from each cell.</param>
        /// <param name="table">The surface table, looked up once per kind.</param>
        /// <remarks>
        /// <para>
        /// One rule and one pass, reading <see cref="SurfaceTuning.RimWidth"/> and
        /// <see cref="SurfaceTuning.RimSurface"/>: a cell within its own surface's rim of the
        /// coastline becomes that surface's rim. Grass gives up four metres to sand and the
        /// open sea gives up five to the shelf, and those are the same sentence twice rather
        /// than two rules that could drift apart.
        /// </para>
        /// <para>
        /// One pass rather than until it settles, so that a rim never rims itself: a beach
        /// around a beach would eat an island four metres at a time.
        /// </para>
        /// </remarks>
        private static void Rim(SurfaceKind[] surfaces, float[] distances, SurfaceTuning[] table)
        {
            for (int index = 0; index < surfaces.Length; index++)
            {
                SurfaceTuning surface = table[(int)surfaces[index]];
                if (surface.RimSurface == SurfaceKind.None || surface.RimWidth <= 0.0f)
                {
                    continue;
                }

                if (Mathf.Abs(distances[index]) <= surface.RimWidth)
                {
                    surfaces[index] = surface.RimSurface;
                }
            }
        }

        /// <summary>
        /// Reads the surface table once per kind, rather than once per cell.
        /// </summary>
        /// <returns>Every row, indexed by the number behind its <see cref="SurfaceKind"/>.</returns>
        /// <remarks>
        /// <see cref="SurfaceTuning.For"/> hands back a fresh copy so that callers can stamp
        /// and edit it, which is right for the handful of callers that want one row and
        /// ruinous for the two loops here, which would otherwise ask it a hundred thousand
        /// times and throw all of it away.
        /// </remarks>
        private static SurfaceTuning[] Table()
        {
            var kinds = (SurfaceKind[])Enum.GetValues(typeof(SurfaceKind));
            int most = 0;
            foreach (SurfaceKind kind in kinds)
            {
                most = Mathf.Max(most, (int)kind);
            }

            var table = new SurfaceTuning[most + 1];
            foreach (SurfaceKind kind in kinds)
            {
                table[(int)kind] = SurfaceTuning.For(kind);
            }

            return table;
        }

        /// <summary>
        /// Measures the squared distance in cells from every cell to the nearest marked one.
        /// </summary>
        /// <param name="source">Which cells are being measured from.</param>
        /// <param name="side">Cells across the grid.</param>
        /// <returns>Squared distance in cells, one per cell.</returns>
        /// <remarks>
        /// Felzenszwalb and Huttenlocher's exact Euclidean distance transform: the lower
        /// envelope of one parabola per cell, down every column and then along every row.
        /// Exact rather than one of the cheap chamfer approximations, because the numbers
        /// that come out of it are compared against real margins - a bunker needs ten metres
        /// of dry land around it, not ten metres give or take a few per cent - and linear in
        /// the number of cells, which searching for the nearest coast cell is emphatically
        /// not.
        /// </remarks>
        private static float[] Spread(bool[] source, int side)
        {
            float unreachable = Unreachable * side * side;
            var squared = new float[source.Length];
            for (int index = 0; index < squared.Length; index++)
            {
                squared[index] = source[index] ? 0.0f : unreachable;
            }

            var line = new float[side];
            var found = new float[side];
            var apex = new int[side];
            var meet = new float[side + 1];

            for (int x = 0; x < side; x++)
            {
                for (int z = 0; z < side; z++)
                {
                    line[z] = squared[(z * side) + x];
                }

                LowerEnvelope(line, found, apex, meet, side);

                for (int z = 0; z < side; z++)
                {
                    squared[(z * side) + x] = found[z];
                }
            }

            for (int z = 0; z < side; z++)
            {
                int row = z * side;
                for (int x = 0; x < side; x++)
                {
                    line[x] = squared[row + x];
                }

                LowerEnvelope(line, found, apex, meet, side);

                for (int x = 0; x < side; x++)
                {
                    squared[row + x] = found[x];
                }
            }

            return squared;
        }

        /// <summary>
        /// Runs one line of the distance transform.
        /// </summary>
        /// <param name="line">Squared distances going in.</param>
        /// <param name="found">Squared distances coming out.</param>
        /// <param name="apex">Scratch: which parabola is lowest over each span.</param>
        /// <param name="meet">Scratch: where consecutive parabolas cross.</param>
        /// <param name="length">How much of each array is in use.</param>
        private static void LowerEnvelope(float[] line, float[] found, int[] apex, float[] meet, int length)
        {
            int top = 0;
            apex[0] = 0;
            meet[0] = float.NegativeInfinity;
            meet[1] = float.PositiveInfinity;

            for (int here = 1; here < length; here++)
            {
                float crossing = Crossing(line, here, apex[top]);
                while (crossing <= meet[top])
                {
                    top--;
                    crossing = Crossing(line, here, apex[top]);
                }

                top++;
                apex[top] = here;
                meet[top] = crossing;
                meet[top + 1] = float.PositiveInfinity;
            }

            top = 0;
            for (int here = 0; here < length; here++)
            {
                while (meet[top + 1] < here)
                {
                    top++;
                }

                float away = here - apex[top];
                found[here] = (away * away) + line[apex[top]];
            }
        }

        /// <summary>
        /// Returns where two of the transform's parabolas cross.
        /// </summary>
        /// <param name="line">Squared distances going in.</param>
        /// <param name="here">The parabola being added.</param>
        /// <param name="against">The parabola on top of the stack.</param>
        /// <returns>Where along the line the two meet.</returns>
        private static float Crossing(float[] line, int here, int against)
            => ((line[here] + (here * here)) - (line[against] + (against * against)))
                / (2.0f * (here - against));

        /// <summary>
        /// Flattens the part of a level that shapes a field into plain numbers.
        /// </summary>
        /// <param name="level">The map.</param>
        /// <returns>
        /// The world's half-extent and the seed, then six numbers for every piece of land.
        /// </returns>
        private static float[] Shape(LevelDefinition level)
        {
            LevelLand[] land = level == null || level.Land == null
                ? Array.Empty<LevelLand>()
                : level.Land;

            var flattened = new float[(land.Length * Told) + Head];
            flattened[0] = HalfExtentOf(level);
            flattened[1] = level == null ? 0 : level.Seed;

            for (int index = 0; index < land.Length; index++)
            {
                LevelLand piece = land[index];
                if (piece == null)
                {
                    continue;
                }

                int at = (index * Told) + Head;
                flattened[at] = piece.MinX;
                flattened[at + 1] = piece.MaxX;
                flattened[at + 2] = piece.MinZ;
                flattened[at + 3] = piece.MaxZ;
                flattened[at + 4] = (int)piece.Ground;
                flattened[at + 5] = (int)piece.Form;
            }

            return flattened;
        }

        /// <summary>
        /// Returns how far a level's world reaches, in metres.
        /// </summary>
        /// <param name="level">The map.</param>
        /// <returns>Its half-extent, never zero: a world with no size has no cells.</returns>
        private static float HalfExtentOf(LevelDefinition level)
        {
            float halfExtent = level == null || level.Bounds == null
                ? 0.0f
                : Mathf.Abs(level.Bounds.HalfExtent);

            return halfExtent > 0.0f ? halfExtent : CellSize;
        }

        private bool Holds(int x, int z) => x >= 0 && z >= 0 && x < side && z < side;

        private int ColumnOf(float world) => Mathf.FloorToInt((world + extent) / cell);

        /// <summary>
        /// Reads one cell of the coast field, clamped to the edge of the grid.
        /// </summary>
        /// <param name="x">Cell across the map.</param>
        /// <param name="z">Cell up the map.</param>
        /// <returns>Signed metres to the coast at that cell.</returns>
        /// <remarks>
        /// Clamped rather than answered with the off-the-map sentinel that
        /// <see cref="ToTheCoast"/> uses, because this one is only ever sampled by
        /// <see cref="Shore"/> for the four cells around a point that is on the map. An
        /// abrupt drop to minus-the-world at the last cell would put a bright foam line
        /// around the outer rim of every sea slab.
        /// </remarks>
        private float Nearest(int x, int z)
            => coast[(Mathf.Clamp(z, 0, side - 1) * side) + Mathf.Clamp(x, 0, side - 1)];
    }
}
