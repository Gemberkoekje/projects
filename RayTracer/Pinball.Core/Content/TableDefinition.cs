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
    /// <summary>Circular pop bumpers (round, active kickers).</summary>
    public List<BumperDef> Bumpers { get; set; } = new();
    /// <summary>Passive posts (rubber pegs the ball bounces off).</summary>
    public List<PostDef> Posts { get; set; } = new();
    /// <summary>Flippers (kinematic bats).</summary>
    public List<FlipperDef> Flippers { get; set; } = new();
    /// <summary>Slingshots / bumper surfaces (flat active kicker faces).</summary>
    public List<SlingshotDef> Slingshots { get; set; } = new();
    /// <summary>Standing targets (drop / spot).</summary>
    public List<TargetDef> Targets { get; set; } = new();
    /// <summary>Point lights (position + optional colour).</summary>
    public List<LightDef> Lights { get; set; } = new();

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

    // Coil strengths (kg·m/s impulse, m/s trigger). Ball mass 0.0806 kg ⇒ 0.15 ≈ a 1.9 m/s pop.
    private const double BumperImpulse = 0.15, BumperTrigger = 0.30;
    private const double SlingImpulse = 0.10, SlingTrigger = 0.30;

    /// <summary>Builds the co-registered <b>physics</b> table from this definition (the data-driven game): walls
    /// → <see cref="BezierWallCollider"/>, circular bumpers &amp; slingshots → <see cref="ActiveZone"/> kickers,
    /// posts → cylinders, targets → passive quads, flippers → <see cref="Flipper"/> actuators. Render units ×
    /// <c>PinballTable.RenderScale</c> into metres — the render twin is <c>PinballTableScene.BuildWallsOnly(…,
    /// gameReady:true)</c>. An invisible outer shell backstops any gap in the authored boundary.</summary>
    public PinballTable BuildPhysicsTable(ulong seed = 0x5D_EE_CE_5Eul)
    {
        double rs = PinballTable.RenderScale;
        double alpha = PhysicsConstants.DefaultInclineRadians, g = PhysicsConstants.Gravity, r = PhysicsConstants.BallRadius;
        PhysicsSettings settings = PhysicsSettings.Default with
        {
            Seed = seed,
            GravityOverride = new Vector3D(0, -g * Math.Cos(alpha), -g * Math.Sin(alpha)),
        };
        PhysicsMaterial steel = PhysicsMaterial.Steel, rubber = PhysicsMaterial.Rubber;

        var cols = new List<ICollider>
        {
            new PlaneCollider(Vector3D.Zero, Vector3D.UnitY, PhysicsMaterial.Playfield), // playfield floor
            new PlaneCollider(new Vector3D(-6.5 * rs, 0, 0), Vector3D.UnitX, steel),      // shell backstop (invisible)
            new PlaneCollider(new Vector3D(6.5 * rs, 0, 0), -Vector3D.UnitX, steel),
            new PlaneCollider(new Vector3D(0, 0, 26 * rs), -Vector3D.UnitZ, steel),
            // Plexiglass ceiling at the wall tops — caps the ball so a flip can't pop it up and over the walls.
            new PlaneCollider(new Vector3D(0, WallHeight * rs, 0), -Vector3D.UnitY, PhysicsMaterial.Playfield),
        };

        foreach (TableWalls.Wall w in ToWalls())
            cols.Add(new BezierWallCollider(TableWalls.CurveMetres(w, rs), 0, WallHeight * rs, w.Segments, 0, steel));

        foreach (PostDef p in Posts)
            if (p.Pos is { Length: >= 2 })
                cols.Add(CylinderCollider.VerticalPost(new Vector3D(p.Pos[0] * rs, 0, p.Pos[1] * rs),
                    Math.Max(0.01, p.Height * rs), Math.Max(0.004, p.Radius * rs), rubber));

        foreach (BumperDef b in Bumpers)
            if (b.Pos is { Length: >= 2 })
                cols.Add(new ActiveZone(CylinderCollider.VerticalPost(new Vector3D(b.Pos[0] * rs, 0, b.Pos[1] * rs),
                    Math.Max(0.01, b.Height * rs), Math.Max(0.01, b.Radius * rs), rubber), BumperImpulse, BumperTrigger));

        foreach (SlingshotDef sl in Slingshots)
            if (sl.From is { Length: >= 2 } && sl.To is { Length: >= 2 })
                cols.Add(new ActiveZone(new QuadCollider(
                    new Vector3D(sl.From[0] * rs, 0, sl.From[1] * rs),
                    new Vector3D((sl.To[0] - sl.From[0]) * rs, 0, (sl.To[1] - sl.From[1]) * rs),
                    new Vector3D(0, Math.Max(0.02, sl.Height * rs), 0), rubber), SlingImpulse, SlingTrigger));

        foreach (TargetDef t in Targets)
            if (t.Pos is { Length: >= 2 })
            {
                double hw = Math.Max(0.02, t.Width * 0.5);
                var dir = new Vector3D(Math.Cos(t.Angle), 0, -Math.Sin(t.Angle)); // RotY(+X, angle) — matches AddTarget
                cols.Add(new QuadCollider(new Vector3D(t.Pos[0] * rs, 0, t.Pos[1] * rs) - dir * (hw * rs),
                    dir * (t.Width * rs), new Vector3D(0, Math.Max(0.02, t.Height * rs), 0), steel));
            }

        Flipper left = FlipperFor(false, rs) ?? MakeFlipper(-1.5, 3.4, -2.7, 2.5, rs);
        Flipper right = FlipperFor(true, rs) ?? MakeFlipper(1.5, 3.4, 2.7, 2.5, rs);
        cols.Add(left);
        cols.Add(right);

        Vector3D ballStart = Ball is { Start.Length: >= 2 }
            ? new Vector3D(Ball.Start[0] * rs, r, Ball.Start[1] * rs)
            : PinballTable.PlayfieldPoint(0, 6);

        return new PinballTable(settings, cols, left, right, ballStart, ballStart,
            launchSpeed: 2.5, drainZ: 1.5 * rs, drainX: double.MaxValue);
    }

    private Flipper? FlipperFor(bool right, double rs)
    {
        foreach (FlipperDef f in Flippers)
            if (string.Equals(f.Side, "right", StringComparison.OrdinalIgnoreCase) == right
                && f.Pivot is { Length: >= 2 } && f.Tip is { Length: >= 2 })
                return MakeFlipper(f.Pivot[0], f.Pivot[1], f.Tip[0], f.Tip[1], rs);
        return null;
    }

    private static Flipper MakeFlipper(double px, double pz, double tx, double tz, double rs)
    {
        double r = PhysicsConstants.BallRadius;
        Vector3D pivot = new(px * rs, r, pz * rs), tip = new(tx * rs, r, tz * rs);
        // Flip UP-table (raise the tip's z) whichever way the bat points: d(tipZ)/dθ at rest = −(tip.x−pivot.x),
        // so the throw is +1.4 when the tip is left of the pivot, −1.4 when it is to the right.
        double endAngle = tx > px ? -1.4 : 1.4;
        return new Flipper(pivot, Vector3D.UnitY, (tip - pivot).Normalized(), (tip - pivot).Length(),
            0.6 * r, restAngle: 0, endAngle: endAngle, PhysicsMaterial.Rubber);
    }
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

/// <summary>A circular pop bumper (round, active kicker) — render <c>AddBumper</c>, physics
/// <c>ActiveZone(CylinderCollider…)</c>. Position <c>[x, z]</c>, radius and height in render units.</summary>
public sealed class BumperDef
{
    public string? Id { get; set; }
    public double[] Pos { get; set; } = new[] { 0.0, 12.0 };
    public double Radius { get; set; } = 0.45;
    public double Height { get; set; } = 0.45;
}

/// <summary>A passive post / rubber peg — render <c>AddPost</c>, physics <c>CylinderCollider.VerticalPost</c>.</summary>
public sealed class PostDef
{
    public string? Id { get; set; }
    public double[] Pos { get; set; } = new[] { 0.0, 12.0 };
    public double Radius { get; set; } = 0.12;
    public double Height { get; set; } = 0.7;
}

/// <summary>A flipper — render <c>AddFlipper</c>, physics <c>Flipper</c> about +Y. <see cref="Side"/> ("left"
/// or "right") sets the flip direction (end angle ±1.4). Pivot/tip are <c>[x, z]</c> (render units).</summary>
public sealed class FlipperDef
{
    public string? Id { get; set; }
    public double[] Pivot { get; set; } = new[] { -1.5, 3.4 };
    public double[] Tip { get; set; } = new[] { -2.7, 2.5 };
    public double Width { get; set; } = 0.28;
    public string Side { get; set; } = "left";
}

/// <summary>A slingshot / bumper surface (flat active kicker) — render <c>AddRotBox</c>, physics
/// <c>ActiveZone(QuadCollider…)</c>. Authored as the face segment <see cref="From"/>→<see cref="To"/> (render
/// units), with a panel <see cref="Depth"/> (thickness) and <see cref="Height"/>.</summary>
public sealed class SlingshotDef
{
    public string? Id { get; set; }
    public double[] From { get; set; } = new[] { -3.6, 3.8 };
    public double[] To { get; set; } = new[] { -2.4, 4.6 };
    public double Depth { get; set; } = 0.08;
    public double Height { get; set; } = 0.6;
}

/// <summary>A standing target — render <c>AddTarget</c>. Position <c>[x, z]</c>, face width + height and a
/// rotation <see cref="Angle"/> (radians about +Y).</summary>
public sealed class TargetDef
{
    public string? Id { get; set; }
    public double[] Pos { get; set; } = new[] { 0.0, 7.0 };
    public double Width { get; set; } = 0.5;
    public double Height { get; set; } = 0.5;
    public double Angle { get; set; }
}

/// <summary>A point light — <c>[x, y, z]</c> (render units, y up) and an optional <c>[r, g, b]</c> colour.</summary>
public sealed class LightDef
{
    public string? Id { get; set; }
    public double[] Pos { get; set; } = new[] { 0.0, 9.0, 12.0 };
    public double[]? Color { get; set; }
}
