using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pinball.Physics;

namespace Pinball.Content;

/// <summary>
/// A data-driven table layout — the JSON schema the visual wall editor produces and the game loads, so the
/// playfield geometry lives in data rather than code. Version 1 covers the wall set (the current focus); more
/// element kinds (bumpers, flippers, lights, targets) slot in as sibling arrays later without breaking v1
/// files. Coordinates are render units — x∈[-5.5,5.5], z∈[0,24], +z up-table — matching
/// <see cref="PinballTableScene"/>. Load with <see cref="Load"/>, author round-trips through
/// <see cref="ToWalls"/> / <see cref="FromWalls"/>.
/// </summary>
public sealed class TableDefinition
{
    /// <summary>Schema version (bump when the shape changes incompatibly).</summary>
    public int Version { get; set; } = 1;
    /// <summary>Default wall height in render units (per-wall <see cref="WallDef.Height"/> overrides it).</summary>
    public double WallHeight { get; set; } = TableWalls.WallHeight;
    /// <summary>The ball's start position and size (render units). Null falls back to the engine default.</summary>
    public BallDef? Ball { get; set; }
    /// <summary>The wall set.</summary>
    public List<WallDef> Walls { get; set; } = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Reads a table definition from a JSON file.</summary>
    public static TableDefinition Load(string path) =>
        JsonSerializer.Deserialize<TableDefinition>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"empty or invalid table JSON: {path}");

    /// <summary>Serialises to pretty JSON — the editor's import/export format.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>The walls as engine <see cref="TableWalls.Wall"/> records: a "bezier" keeps its four control
    /// points; a "line" is expanded to a straight cubic (evenly-spaced control points).</summary>
    public TableWalls.Wall[] ToWalls()
    {
        var result = new List<TableWalls.Wall>(Walls.Count);
        int dropped = 0;
        foreach (WallDef w in Walls)
        {
            double[][]? pts = w.Points;
            int seg = Math.Max(1, w.Segments > 0 ? w.Segments : 12);
            bool line = string.Equals(w.Type, "line", StringComparison.OrdinalIgnoreCase);
            if (line && pts is { Length: >= 2 })
            {
                Vector3D a = Pt(pts[0]), b = Pt(pts[^1]);
                result.Add(new TableWalls.Wall(w.Id ?? "line", a,
                    Vector3D.Lerp(a, b, 1.0 / 3.0), Vector3D.Lerp(a, b, 2.0 / 3.0), b, seg));
            }
            else if (!line && pts is { Length: >= 4 })
            {
                result.Add(new TableWalls.Wall(w.Id ?? "bezier",
                    Pt(pts[0]), Pt(pts[1]), Pt(pts[2]), Pt(pts[3]), seg));
            }
            else dropped++;
        }
        if (dropped > 0)
            Console.Error.WriteLine($"[table] skipped {dropped} malformed wall(s) (a line needs ≥2 points, a bezier ≥4).");
        return result.ToArray();
    }

    /// <summary>Builds a definition from the engine wall set — for <c>--table-export</c>, to seed the editor
    /// with the current geometry.</summary>
    public static TableDefinition FromWalls(IEnumerable<TableWalls.Wall> walls, double wallHeight)
    {
        var def = new TableDefinition
        {
            WallHeight = wallHeight,
            // The render ball radius is 0.36 render units; racks at the plunger (see PinballTable.ShooterLaneStart).
            Ball = new BallDef { Start = new[] { 5.05, 1.2 }, Radius = 0.36 },
        };
        foreach (TableWalls.Wall w in walls)
            def.Walls.Add(new WallDef
            {
                Id = w.Name,
                Type = "bezier",
                Segments = w.Segments,
                Points = new[] { Xz(w.P0), Xz(w.P1), Xz(w.P2), Xz(w.P3) },
            });
        return def;
    }

    private static Vector3D Pt(double[]? p) => new(p is { Length: > 0 } ? p[0] : 0, 0, p is { Length: > 1 } ? p[1] : 0);
    private static double[] Xz(Vector3D v) => new[] { v.X, v.Z };
}

/// <summary>One wall in the table JSON: a <c>"bezier"</c> (four control points) or a <c>"line"</c> (two
/// endpoints), each a list of <c>[x, z]</c> render-unit points, tessellated into <see cref="Segments"/>
/// straight panels.</summary>
public sealed class WallDef
{
    /// <summary>A human name / stable id (shown in the editor).</summary>
    public string? Id { get; set; }
    /// <summary>"bezier" or "line".</summary>
    public string Type { get; set; } = "bezier";
    /// <summary>Panels to flatten this wall into.</summary>
    public int Segments { get; set; } = 12;
    /// <summary>Control/end points as <c>[x, z]</c> pairs (render units).</summary>
    public double[][] Points { get; set; } = Array.Empty<double[]>();
    /// <summary>Optional per-wall height override (render units).</summary>
    public double? Height { get; set; }
}

/// <summary>The ball's start position (<c>[x, z]</c>) and radius, in render units. The render ball is 0.36
/// units (co-registered with the 27&#160;mm physics ball via <c>PinballTable.RenderScale</c>).</summary>
public sealed class BallDef
{
    /// <summary>Start / rack position as <c>[x, z]</c> (render units).</summary>
    public double[] Start { get; set; } = new[] { 5.05, 1.2 };
    /// <summary>Ball radius (render units).</summary>
    public double Radius { get; set; } = 0.36;
}
