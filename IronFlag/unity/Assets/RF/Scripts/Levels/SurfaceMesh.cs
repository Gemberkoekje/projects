using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace IronFlag.Levels
{
    /// <summary>
    /// Cuts the sheets a map is drawn as out of a <see cref="SurfaceField"/>: one flat sheet
    /// per surface, and the bank that hangs below the coastline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Marching squares over the field's cells, and the whole reason for it is that a cell
    /// is a metre and a coastline is not. The boundary of a sheet is drawn as a line
    /// <em>through</em> the cells - straight in, at whatever angle the numbers say - rather
    /// than around them, so a coast that wanders comes out as a coast that wanders instead
    /// of as a staircase, and an edge the level file put half a metre off the grid comes out
    /// half a metre off the grid.
    /// </para>
    /// <para>
    /// <strong>The sheets are stacked, not stitched.</strong> Each one covers its own
    /// surface and every surface drawn above it - see <see cref="SurfaceTuning.Layer"/> -
    /// so a sheet is always a superset of the one on top of it and there is no shared edge
    /// anywhere that has to be got exactly right. Where two of them disagree by a
    /// hair, what shows through is the sheet below, which is the colour that belongs just
    /// outside; that is a great deal better than the alternative, where what shows through
    /// is whatever happens to be under the whole map.
    /// </para>
    /// <para>
    /// Everything here is geometry over a field that has already decided everything. Nothing
    /// in this file knows what sand is, which surface rims which, or where a level put a
    /// rectangle.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// Mesh beach = SurfaceMesh.Build(level.Field, SurfaceKind.Sand, "Sand");
    /// Mesh cliff = SurfaceMesh.Bank(level.Field, SurfaceKind.Sand, 1.2f, "Sand bank");
    /// </code>
    /// </example>
    public static class SurfaceMesh
    {
        /// <summary>Smaller than which a piece of geometry is not worth drawing.</summary>
        /// <remarks>
        /// A tenth of a millimetre. Squares whose boundary passes exactly through a corner
        /// produce a triangle with two corners in the same place or a stretch of bank with
        /// no length, and both are nothing on the screen and something in a normal.
        /// </remarks>
        private const float Flat = 0.0001f;

        /// <summary>Corners of a square, clockwise seen from above.</summary>
        /// <remarks>
        /// Clockwise in the x-z plane is the way up in a left-handed world. Wound the other
        /// way, a sheet is not there when you look down at it and is there from underneath -
        /// which is not a subtle bug but is a very quiet one, because the map still has a
        /// sea in it.
        /// </remarks>
        private static readonly int[] AlongX = { 0, 0, 1, 1 };

        /// <summary>The other half of <see cref="AlongX"/>.</summary>
        private static readonly int[] AlongZ = { 0, 1, 1, 0 };

        /// <summary>
        /// Builds the flat sheet covering one surface's layer.
        /// </summary>
        /// <param name="field">The rasterised map.</param>
        /// <param name="kind">Surface whose layer to cut.</param>
        /// <param name="name">What to call the mesh.</param>
        /// <returns>
        /// The mesh, lying at <c>y = 0</c> and facing up, or <c>null</c> when the map has
        /// none of that layer on it - so a caller can leave the object out rather than hang
        /// an empty renderer off the map.
        /// </returns>
        public static Mesh Build(SurfaceField field, SurfaceKind kind, string name)
        {
            if (field == null || kind == SurfaceKind.None)
            {
                return null;
            }

            int side = field.Side;
            float[] reach = field.Layer(kind);
            var vertices = new List<Vector3>();
            var texture = new List<Vector2>();
            var triangles = new List<int>();

            bool[] whole = Whole(reach, side);
            Inside(field, whole, side, vertices, texture, triangles);

            var corner = new Vector2[4];
            var value = new float[4];
            var outline = new Vector2[8];
            var cut = new bool[8];

            for (int z = -1; z < side; z++)
            {
                for (int x = -1; x < side; x++)
                {
                    if (x >= 0 && z >= 0 && x + 1 < side && z + 1 < side && whole[(z * side) + x])
                    {
                        continue;
                    }

                    int drawn = Trace(field, reach, x, z, corner, value, outline, cut);
                    for (int step = 1; step + 1 < drawn; step++)
                    {
                        Ground(vertices, texture, triangles, outline[0], outline[step], outline[step + 1]);
                    }
                }
            }

            return Finish(vertices, texture, triangles, name);
        }

        /// <summary>
        /// Builds the bank hanging below the stretches of coastline one surface reaches.
        /// </summary>
        /// <param name="field">The rasterised map.</param>
        /// <param name="kind">Surface whose coast to hang a bank from.</param>
        /// <param name="thickness">How far down it goes, in metres.</param>
        /// <param name="name">What to call the mesh.</param>
        /// <returns>
        /// The mesh, its top edge on the coastline at <c>y = 0</c> and its faces looking out
        /// to sea, or <c>null</c> when that surface never reaches the water.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The one place the map has any relief at all: land is at <c>y = 0</c> everywhere
        /// because <see cref="IronFlag.Combat.CombatPlane"/> resolves every round on that
        /// plane, so the only height on the whole island is the drop to the water, and this
        /// is it. It is also what makes the coastline read from directly overhead, which M7
        /// established the hard way and which a flat colour boundary would give away again.
        /// </para>
        /// <para>
        /// Cut from the coastline itself rather than from the layer whose bank it is, so
        /// that the top of the bank and the edge of the sheet above it are the same line by
        /// construction. Which stretch belongs to which surface is decided per segment, by
        /// the land cell nearest it - so a road that runs down to the water gets a road's
        /// bank and the beach either side of it gets a beach's.
        /// </para>
        /// </remarks>
        public static Mesh Bank(SurfaceField field, SurfaceKind kind, float thickness, string name)
        {
            if (field == null || kind == SurfaceKind.None || thickness <= 0.0f)
            {
                return null;
            }

            float[] shore = field.Outline();
            var vertices = new List<Vector3>();
            var texture = new List<Vector2>();
            var triangles = new List<int>();

            var corner = new Vector2[4];
            var value = new float[4];
            var outline = new Vector2[8];
            var cut = new bool[8];

            for (int z = -1; z < field.Side; z++)
            {
                for (int x = -1; x < field.Side; x++)
                {
                    int drawn = Trace(field, shore, x, z, corner, value, outline, cut);

                    for (int step = 0; step < drawn; step++)
                    {
                        int next = (step + 1) % drawn;
                        if (!cut[step] || !cut[next])
                        {
                            continue;
                        }

                        Vector2 from = outline[step];
                        Vector2 to = outline[next];
                        if (Ashore(field, x, z, value, (from + to) * 0.5f) == kind)
                        {
                            Wall(vertices, texture, triangles, from, to, thickness);
                        }
                    }
                }
            }

            return Finish(vertices, texture, triangles, name);
        }

        /// <summary>
        /// Marks the squares of the dual grid that are wholly inside a layer.
        /// </summary>
        /// <param name="reach">How far inside each cell is, one per cell.</param>
        /// <param name="side">Cells across the grid.</param>
        /// <returns>One flag per square, indexed by its south-west cell.</returns>
        /// <remarks>
        /// Squares with a boundary through them have to be cut one at a time; squares with
        /// nothing but inside in them are all the same square, and there are tens of
        /// thousands of them on a map this size. Telling the two apart is what lets the
        /// interior be merged into a handful of rectangles instead of two triangles per
        /// square metre - which is the difference between a scene file of a few hundred
        /// kilobytes and one of twenty-six megabytes.
        /// </remarks>
        private static bool[] Whole(float[] reach, int side)
        {
            var whole = new bool[side * side];

            for (int z = 0; z + 1 < side; z++)
            {
                for (int x = 0; x + 1 < side; x++)
                {
                    whole[(z * side) + x] =
                        reach[(z * side) + x] > 0.0f
                        && reach[(z * side) + x + 1] > 0.0f
                        && reach[((z + 1) * side) + x] > 0.0f
                        && reach[((z + 1) * side) + x + 1] > 0.0f;
                }
            }

            return whole;
        }

        /// <summary>
        /// Covers everything wholly inside a layer with as few rectangles as it will take.
        /// </summary>
        /// <param name="field">The rasterised map.</param>
        /// <param name="whole">Which squares are wholly inside.</param>
        /// <param name="side">Cells across the grid.</param>
        /// <param name="vertices">Vertices being built.</param>
        /// <param name="texture">Texture coordinates being built.</param>
        /// <param name="triangles">Triangles being built.</param>
        /// <remarks>
        /// Grown east as far as it will go and then north as far as every row of it will go,
        /// which is the cheapest merge that is exact: no square is covered twice and none is
        /// missed, so the result is the same shape as the squares it replaces and the only
        /// difference is how many triangles it took. The interior of an island comes out as
        /// a few dozen rectangles rather than as thirteen thousand.
        /// </remarks>
        private static void Inside(
            SurfaceField field,
            bool[] whole,
            int side,
            List<Vector3> vertices,
            List<Vector2> texture,
            List<int> triangles)
        {
            var used = new bool[whole.Length];

            for (int z = 0; z + 1 < side; z++)
            {
                for (int x = 0; x + 1 < side; x++)
                {
                    int at = (z * side) + x;
                    if (!whole[at] || used[at])
                    {
                        continue;
                    }

                    int east = x;
                    while (east + 2 < side && whole[(z * side) + east + 1] && !used[(z * side) + east + 1])
                    {
                        east++;
                    }

                    int north = z;
                    while (north + 2 < side && Row(whole, used, side, x, east, north + 1))
                    {
                        north++;
                    }

                    for (int row = z; row <= north; row++)
                    {
                        for (int column = x; column <= east; column++)
                        {
                            used[(row * side) + column] = true;
                        }
                    }

                    Quad(
                        vertices,
                        texture,
                        triangles,
                        field.Middle(x),
                        field.Middle(east + 1),
                        field.Middle(z),
                        field.Middle(north + 1));
                }
            }
        }

        /// <summary>
        /// Reports whether a whole row of squares can join the rectangle being grown.
        /// </summary>
        /// <param name="whole">Which squares are wholly inside.</param>
        /// <param name="used">Which squares have been covered already.</param>
        /// <param name="side">Cells across the grid.</param>
        /// <param name="west">First square of the row.</param>
        /// <param name="east">Last square of the row.</param>
        /// <param name="z">Which row.</param>
        /// <returns><c>true</c> when every square in it is inside and free.</returns>
        private static bool Row(bool[] whole, bool[] used, int side, int west, int east, int z)
        {
            for (int x = west; x <= east; x++)
            {
                if (!whole[(z * side) + x] || used[(z * side) + x])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Adds one rectangle of ground, lying flat and facing up.
        /// </summary>
        /// <param name="vertices">Vertices being built.</param>
        /// <param name="texture">Texture coordinates being built.</param>
        /// <param name="triangles">Triangles being built.</param>
        /// <param name="west">West edge, in metres.</param>
        /// <param name="east">East edge, in metres.</param>
        /// <param name="south">South edge, in metres.</param>
        /// <param name="north">North edge, in metres.</param>
        private static void Quad(
            List<Vector3> vertices,
            List<Vector2> texture,
            List<int> triangles,
            float west,
            float east,
            float south,
            float north)
        {
            var southWest = new Vector2(west, south);
            var northWest = new Vector2(west, north);
            var northEast = new Vector2(east, north);
            var southEast = new Vector2(east, south);

            Ground(vertices, texture, triangles, southWest, northWest, northEast);
            Ground(vertices, texture, triangles, southWest, northEast, southEast);
        }

        /// <summary>
        /// Works out what part of one square of the dual grid is inside.
        /// </summary>
        /// <param name="field">The rasterised map.</param>
        /// <param name="reach">How far inside each cell is, one per cell.</param>
        /// <param name="x">Square across the map: its west corners are cell <paramref name="x"/>.</param>
        /// <param name="z">Square up the map: its south corners are cell <paramref name="z"/>.</param>
        /// <param name="corner">Scratch: where the four corners are.</param>
        /// <param name="value">Scratch: how far inside each corner is.</param>
        /// <param name="outline">Filled with the inside polygon, clockwise from above.</param>
        /// <param name="cut">Filled with which of those points are on the boundary.</param>
        /// <returns>How many points the polygon has: zero, or three to six.</returns>
        /// <remarks>
        /// <para>
        /// The squares are the ones <em>between</em> cell centres rather than the cells
        /// themselves, because a value belongs at the middle of a cell and a boundary
        /// belongs between two values. Walking the four sides in order and writing down each
        /// corner that is inside and each side that is crossed produces the polygon directly,
        /// including for the two squares where opposite corners are inside and the diagonal
        /// is ambiguous - those come out joined, which is the choice that keeps a coastline
        /// from pinching itself into two touching corners.
        /// </para>
        /// <para>
        /// Corners past the edge of the grid count as outside, one cell's worth, sitting on
        /// the world's boundary. A map whose land reaches the edge of the world is a map
        /// <see cref="LevelValidation"/> already complains about; this only decides how it
        /// looks while somebody fixes it.
        /// </para>
        /// </remarks>
        private static int Trace(
            SurfaceField field,
            float[] reach,
            int x,
            int z,
            Vector2[] corner,
            float[] value,
            Vector2[] outline,
            bool[] cut)
        {
            int inside = 0;
            for (int at = 0; at < 4; at++)
            {
                int column = x + AlongX[at];
                int row = z + AlongZ[at];
                bool held = column >= 0 && row >= 0 && column < field.Side && row < field.Side;

                corner[at] = new Vector2(
                    Mathf.Clamp(field.Middle(column), -field.Extent, field.Extent),
                    Mathf.Clamp(field.Middle(row), -field.Extent, field.Extent));
                value[at] = held ? reach[(row * field.Side) + column] : -field.Cell;

                if (value[at] > 0.0f)
                {
                    inside++;
                }
            }

            if (inside == 0)
            {
                return 0;
            }

            int drawn = 0;
            for (int at = 0; at < 4; at++)
            {
                int next = (at + 1) & 3;
                bool here = value[at] > 0.0f;
                bool there = value[next] > 0.0f;

                if (here)
                {
                    cut[drawn] = false;
                    outline[drawn++] = corner[at];
                }

                if (here != there)
                {
                    float along = value[at] / (value[at] - value[next]);
                    cut[drawn] = true;
                    outline[drawn++] = Vector2.Lerp(corner[at], corner[next], along);
                }
            }

            return drawn;
        }

        /// <summary>
        /// Returns which surface owns one stretch of coastline.
        /// </summary>
        /// <param name="field">The rasterised map.</param>
        /// <param name="x">Square across the map.</param>
        /// <param name="z">Square up the map.</param>
        /// <param name="value">How far inside each corner of the square is.</param>
        /// <param name="middle">Middle of the stretch, in metres.</param>
        /// <returns>The surface of the nearest cell on the land side of it.</returns>
        private static SurfaceKind Ashore(
            SurfaceField field, int x, int z, float[] value, Vector2 middle)
        {
            SurfaceKind found = SurfaceKind.None;
            float nearest = float.MaxValue;

            for (int at = 0; at < 4; at++)
            {
                if (value[at] <= 0.0f)
                {
                    continue;
                }

                int column = x + AlongX[at];
                int row = z + AlongZ[at];
                float away = new Vector2(field.Middle(column) - middle.x, field.Middle(row) - middle.y)
                    .sqrMagnitude;

                if (away < nearest)
                {
                    nearest = away;
                    found = field.At(column, row);
                }
            }

            return found;
        }

        /// <summary>
        /// Adds one triangle of ground, lying flat and facing up.
        /// </summary>
        /// <param name="vertices">Vertices being built.</param>
        /// <param name="texture">Texture coordinates being built.</param>
        /// <param name="triangles">Triangles being built.</param>
        /// <param name="first">First corner, in metres.</param>
        /// <param name="second">Second corner, in metres.</param>
        /// <param name="third">Third corner, in metres.</param>
        /// <remarks>
        /// <para>
        /// Texture coordinates are world metres, so anything that ever wants to tile a
        /// surface tiles it at the same scale wherever on the map it appears.
        /// </para>
        /// <para>
        /// Triangles with no area in them are dropped rather than added, and they do turn
        /// up: where a boundary passes exactly through a cell's middle - which is precisely
        /// what a built edge written on a half metre does - the polygon for that square
        /// comes out with two of its corners in the same place. They would draw nothing, but
        /// a corner belonging to nothing but flat triangles has no direction to face, and a
        /// mesh with normals like that lights as a black speck.
        /// </para>
        /// </remarks>
        private static void Ground(
            List<Vector3> vertices,
            List<Vector2> texture,
            List<int> triangles,
            Vector2 first,
            Vector2 second,
            Vector2 third)
        {
            float twice = ((second.x - first.x) * (third.y - first.y))
                - ((third.x - first.x) * (second.y - first.y));
            if (Mathf.Abs(twice) < Flat)
            {
                return;
            }

            int at = vertices.Count;

            vertices.Add(new Vector3(first.x, 0.0f, first.y));
            vertices.Add(new Vector3(second.x, 0.0f, second.y));
            vertices.Add(new Vector3(third.x, 0.0f, third.y));

            texture.Add(first);
            texture.Add(second);
            texture.Add(third);

            triangles.Add(at);
            triangles.Add(at + 1);
            triangles.Add(at + 2);
        }

        /// <summary>
        /// Adds one stretch of bank, hanging below the coastline and facing the water.
        /// </summary>
        /// <param name="vertices">Vertices being built.</param>
        /// <param name="texture">Texture coordinates being built.</param>
        /// <param name="triangles">Triangles being built.</param>
        /// <param name="from">Where the stretch starts, in metres.</param>
        /// <param name="to">Where it ends, in metres.</param>
        /// <param name="thickness">How far down it goes, in metres.</param>
        /// <remarks>
        /// The land is on the right walking from <paramref name="from"/> to
        /// <paramref name="to"/>, because that is how <see cref="Trace"/> winds a polygon,
        /// so this winding is the one that looks out to sea. Wound the other way the island
        /// would be a hole you could see the inside of.
        /// </remarks>
        private static void Wall(
            List<Vector3> vertices,
            List<Vector2> texture,
            List<int> triangles,
            Vector2 from,
            Vector2 to,
            float thickness)
        {
            float across = (to - from).magnitude;
            if (across < Flat)
            {
                return;
            }

            int at = vertices.Count;

            vertices.Add(new Vector3(from.x, 0.0f, from.y));
            vertices.Add(new Vector3(from.x, -thickness, from.y));
            vertices.Add(new Vector3(to.x, -thickness, to.y));
            vertices.Add(new Vector3(to.x, 0.0f, to.y));

            texture.Add(new Vector2(0.0f, 0.0f));
            texture.Add(new Vector2(0.0f, -thickness));
            texture.Add(new Vector2(across, -thickness));
            texture.Add(new Vector2(across, 0.0f));

            triangles.Add(at);
            triangles.Add(at + 1);
            triangles.Add(at + 2);
            triangles.Add(at);
            triangles.Add(at + 2);
            triangles.Add(at + 3);
        }

        /// <summary>
        /// Turns what has been gathered into a mesh, or into nothing.
        /// </summary>
        /// <param name="vertices">Vertices gathered.</param>
        /// <param name="texture">Texture coordinates gathered.</param>
        /// <param name="triangles">Triangles gathered.</param>
        /// <param name="name">What to call it.</param>
        /// <returns>The mesh, or <c>null</c> when nothing was gathered.</returns>
        /// <remarks>
        /// Sixteen-bit indices run out at 65535 vertices. A coastline that wanders spends
        /// three vertices per triangle and one triangle per cell it crosses, so a big map
        /// with a long coast gets there - and it is cheaper to ask for the wider index than
        /// to find out which map was the one that did.
        /// </remarks>
        private static Mesh Finish(
            List<Vector3> vertices, List<Vector2> texture, List<int> triangles, string name)
        {
            if (vertices.Count == 0)
            {
                return null;
            }

            var mesh = new Mesh { name = name };
            if (vertices.Count > ushort.MaxValue)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, texture);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
