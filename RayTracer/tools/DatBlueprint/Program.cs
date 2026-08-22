using System.Globalization;
using System.Text;
using System.Text.Json;

// ─────────────────────────────────────────────────────────────────────────────
// DatBlueprint — turn a 3D Pinball / Full Tilt "PARTOUT(4.0)RESOURCE" data file
// into a TOP-DOWN blueprint you can trace over in tools/table-editor.html.
//
// Why this exists: the reference screenshot is a baked 2.5-D perspective render, so
// you can't read accurate top-down (x,z) off it. But the ORIGINAL SIMULATION is 2-D
// top-down, and its collision geometry lives in the .dat as plain float arrays. This
// tool walks the .dat container (byte-exact for both 3DPB and Full Tilt — see below),
// pulls every float array, and plots the ones that look like table geometry as an SVG
// you drop into the editor as the reference overlay. It also dumps everything to JSON
// so we can calibrate the exact coordinate mapping into table.json space.
//
// Clean-room note: this reads ONLY the file you pass on the command line. It does not
// contain, embed, or redistribute any game data. Point it at YOUR OWN pinball.dat.
//
// Usage:
//   dotnet run --project tools/DatBlueprint -- <path-to-pinball.dat> [outDir] [options]
// Options:
//   --offset N     floats to skip at the head of each array before pairing (default 2;
//                  the original's query_visual exposes collision floats at FloatArr+2)
//   --min-points N minimum coordinate pairs for a field to be drawn (default 3)
//   --flip-y       mirror vertically (try this if the plot is upside-down vs the game)
//   --rotate DEG   rotate the view 0/90/180/270° (use 180 if the table is upside-down AND
//                  mirrored vs the game; display-only, JSON/--filter stay in .dat coords)
//   --filter x0 y0 x1 y1   only draw fields whose points mostly fall in this world box
//                  (use after a first pass, reading bounds from the JSON, to drop noise)
//   --no-filter    keep every field; disables the automatic outlier-field drop
//
// By default (no --filter, no --no-filter) it auto-drops fields whose floats aren't
// coordinates — camera params, sprite rects, state tables — via a Tukey 3×IQR fence,
// so the real table fills the canvas instead of being crushed by far-off junk points.
//
// Container format (from k4zmu2a/SpaceCadetPinball partman.cpp load_records):
//   183-byte header: char sig[21] ("PARTOUT(4.0)RESOURCE"), char appName[50],
//     char desc[100], int32 fileSize, uint16 groupCount, int32 bodySize, uint16 unknown.
//   If unknown != 0, skip `unknown` bytes.
//   Then groupCount groups. Each group: uint8 entryCount, then that many entries.
//   Each entry: uint8 type; size = _field_size[type] if >=0 else read uint32; then
//     `size` raw bytes. (Bitmap8/16 and Full-Tilt zmap all net to exactly `size` bytes
//     consumed, so a generic skip is byte-exact without special-casing them.)
//
// Float fields are led by a record code. Most geometry is code 600 = a flat (x,y) collision
// list read at +2 (loader::query_visual). Ramps differ: code 1300 = ramp_plane_type triangles,
// 1301/1302/1303 = wall segments — decoded in DecodePolylines() from TRamp.cpp / maths.h so the
// ramp footprint traces cleanly instead of scrambling. See README "Record decoding".
// ─────────────────────────────────────────────────────────────────────────────

// _field_size[type]: >=0 fixed byte size, -1 read a uint32 length. Verbatim from partman.
short[] fieldSize = { 2, -1, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 0 };

var argList = new List<string>(args);
int offset = 2, minPoints = 3;
bool flipY = false;
bool autoFilter = true;
double[]? filter = null;

int TakeIntOpt(string flag, int dflt)
{
    int i = argList.IndexOf(flag);
    if (i < 0 || i + 1 >= argList.Count) return dflt;
    int v = int.Parse(argList[i + 1], CultureInfo.InvariantCulture);
    argList.RemoveRange(i, 2);
    return v;
}

offset = TakeIntOpt("--offset", offset);
minPoints = TakeIntOpt("--min-points", minPoints);
if (argList.Remove("--flip-y")) flipY = true;
if (argList.Remove("--no-filter")) autoFilter = false;
int rotate = ((TakeIntOpt("--rotate", 0) % 360) + 360) % 360;
if (rotate is not (0 or 90 or 180 or 270))
{
    Console.Error.WriteLine($"--rotate must be 0/90/180/270 (got {rotate}); using 0.");
    rotate = 0;
}
{
    int i = argList.IndexOf("--filter");
    if (i >= 0 && i + 4 < argList.Count)
    {
        filter = new[]
        {
            double.Parse(argList[i + 1], CultureInfo.InvariantCulture),
            double.Parse(argList[i + 2], CultureInfo.InvariantCulture),
            double.Parse(argList[i + 3], CultureInfo.InvariantCulture),
            double.Parse(argList[i + 4], CultureInfo.InvariantCulture),
        };
        argList.RemoveRange(i, 5);
    }
}

var positional = argList.Where(a => !a.StartsWith("--")).ToList();
if (positional.Count < 1)
{
    Console.Error.WriteLine("usage: dotnet run --project tools/DatBlueprint -- <pinball.dat> [outDir] [--offset N] [--min-points N] [--flip-y] [--rotate 0|90|180|270] [--filter x0 y0 x1 y1] [--no-filter]");
    return 1;
}

string datPath = positional[0];
string outDir = positional.Count > 1 ? positional[1] : Path.GetDirectoryName(Path.GetFullPath(datPath)) ?? ".";
Directory.CreateDirectory(outDir);

if (!File.Exists(datPath))
{
    Console.Error.WriteLine($"not found: {datPath}");
    return 1;
}

byte[] bytes = File.ReadAllBytes(datPath);
var groups = ParseDat(bytes, fieldSize, out string appName, out string description, out int groupCountHeader);

Console.WriteLine($"Loaded  {datPath}");
Console.WriteLine($"AppName {appName.Trim()}");
Console.WriteLine($"Desc    {description.Trim()}");
Console.WriteLine($"Groups  {groups.Count} (header said {groupCountHeader})");

// Decode one .dat float field into one or more polylines. Most fields are flat (x,y)
// collision lists (led by code 600 — read at +2, exactly what loader::query_visual does).
// Ramp components store 3-D data instead: code 1300 is a list of ramp_plane_type triangles,
// 1301/1302/1303 are wall segments. Reading those as flat pairs is what produced the
// scrambled knot; the layouts here are copied verbatim from k4zmu2a/SpaceCadetPinball
// (TRamp.cpp + the ramp_plane_type struct in maths.h).
static IEnumerable<List<(double x, double y)>> DecodePolylines(float[] fl, int offset, int minPoints)
{
    int code = fl.Length > 0 && MathF.Abs(fl[0] - MathF.Round(fl[0])) < 1e-3f ? (int)MathF.Round(fl[0]) : -1;
    switch (code)
    {
        case 1300:  // [1300, planeCount, planeCount × ramp_plane_type (13 floats each)]
        {           //   plane = [0..2] BallCollisionOffset xyz, [3,4] V1, [5,6] V2, [7,8] V3, [9,10] gravity, [11,12] fieldForce
            int count = fl.Length > 1 ? (int)fl[1] : 0;  // the game builds collision edges V1→V2→V3, so draw that triangle
            for (int i = 0; i < count; i++)
            {
                int b = 2 + i * 13;
                if (b + 8 >= fl.Length) break;
                yield return new List<(double x, double y)>
                {
                    (fl[b + 3], fl[b + 4]),  // V1
                    (fl[b + 5], fl[b + 6]),  // V2
                    (fl[b + 7], fl[b + 8]),  // V3
                    (fl[b + 3], fl[b + 4]),  // close the triangle
                };
            }
            break;
        }
        case 1301:  // ramp wall segment: query_float_attribute returns code+1, wall_point_type at +3 → field[4..7]
        case 1302:
            if (fl.Length >= 8)
                yield return new List<(double x, double y)> { (fl[4], fl[5]), (fl[6], fl[7]) };
            break;
        case 1303:  // ramp wall0 segment: wall_point_type at +2 → field[3..6]
            if (fl.Length >= 7)
                yield return new List<(double x, double y)> { (fl[3], fl[4]), (fl[5], fl[6]) };
            break;
        default:    // 600-style collision list & everything else: blind (x,y) pairs from `offset`
            if (fl.Length - offset >= minPoints * 2)
            {
                var pts = new List<(double x, double y)>();
                for (int k = offset; k + 1 < fl.Length; k += 2)
                    pts.Add((fl[k], fl[k + 1]));
                if (pts.Count >= minPoints)
                    yield return pts;
            }
            break;
    }
}

// ── collect drawable geometry ────────────────────────────────────────────────
var geoms = new List<Geom>();
foreach (GroupDump g in groups)
    foreach (FieldDump f in g.Fields)
    {
        if (f.Floats is null) continue;
        foreach (List<(double x, double y)> poly in DecodePolylines(f.Floats, offset, minPoints))
        {
            if (poly.Count < 2) continue;
            if (poly.Any(p => double.IsNaN(p.x) || double.IsNaN(p.y) || double.IsInfinity(p.x) || double.IsInfinity(p.y) || Math.Abs(p.x) > 1e6 || Math.Abs(p.y) > 1e6))
                continue;
            if (filter is not null)
            {
                int inBox = poly.Count(p => p.x >= filter[0] && p.x <= filter[2] && p.y >= filter[1] && p.y <= filter[3]);
                if (inBox < poly.Count * 0.6) continue;
            }
            geoms.Add(new Geom(g.Index, g.Name, poly));
        }
    }

// ── auto-drop non-geometry fields ─────────────────────────────────────────────
// Every 4-byte-aligned field got read as (x,y) pairs, but some groups are camera
// params / sprite rects / state tables whose floats aren't table coordinates. Read
// as points they land hundreds of units away and crush the real table into a sliver
// (see README "Status / next"). Unless the user gave an explicit --filter box or
// passed --no-filter, drop any field with a point outside a Tukey 3×IQR fence — the
// standard far-outlier fence, robust because a few junk arrays can't move the quartiles.
int autoDropped = 0;
if (filter is null && autoFilter && geoms.Count > 0)
{
    var fx = geoms.SelectMany(g => g.Points.Select(p => p.x)).OrderBy(v => v).ToList();
    var fy = geoms.SelectMany(g => g.Points.Select(p => p.y)).OrderBy(v => v).ToList();
    double qx1 = Pct(fx, 0.25), qx3 = Pct(fx, 0.75), iqrX = qx3 - qx1;
    double qy1 = Pct(fy, 0.25), qy3 = Pct(fy, 0.75), iqrY = qy3 - qy1;
    double fxLo = qx1 - 3 * iqrX, fxHi = qx3 + 3 * iqrX;
    double fyLo = qy1 - 3 * iqrY, fyHi = qy3 + 3 * iqrY;
    bool InFence(Geom g) => g.Points.All(p => p.x >= fxLo && p.x <= fxHi && p.y >= fyLo && p.y <= fyHi);
    var kept = new List<Geom>(geoms.Count);
    var dropped = new List<Geom>();
    foreach (Geom g in geoms) (InFence(g) ? kept : dropped).Add(g);
    autoDropped = dropped.Count;
    if (autoDropped > 0)
    {
        geoms = kept;
        string names = string.Join(", ", dropped.Select(g => g.Name is { Length: > 0 } ? $"{g.Group}:{g.Name}" : g.Group.ToString()));
        Console.WriteLine($"Auto-filter dropped {autoDropped} non-geometry field(s) outside 3xIQR fence x[{fxLo:0.#},{fxHi:0.#}] y[{fyLo:0.#},{fyHi:0.#}]: {names}");
        Console.WriteLine("  (pass --no-filter to keep them, or --filter x0 y0 x1 y1 to set the box by hand)");
    }
}

string filterNote = filter != null ? "explicit --filter"
    : !autoFilter ? "no filter"
    : autoDropped > 0 ? $"auto-filtered, -{autoDropped}"
    : "auto-filter, none dropped";
Console.WriteLine($"Drawable float-array fields: {geoms.Count}  (offset {offset}, min-points {minPoints}, {filterNote})");

// ── ball radius (query_float_attribute code 500, see TBall.cpp) + table outer extent (the "table"
//    group's code-600 collision list). The plot is framed on this table so the whole outline shows
//    and the editor's overlay maps 1:1 onto the playfield — not on the drawn-geometry percentile,
//    which clips the outer walls and lands off-centre.
double? ballRadius = null;
foreach (GroupDump bg in groups)
    foreach (FieldDump bf in bg.Fields)
        if (bf.Floats is { Length: >= 2 } fa && Math.Abs(fa[0] - 500) < 1e-3) { ballRadius = fa[1]; break; }

(double x0, double x1, double y0, double y1)? TableExtent(Func<GroupDump, bool> pick)
{
    double a = double.PositiveInfinity, b = double.NegativeInfinity, c = double.PositiveInfinity, d = double.NegativeInfinity;
    bool any = false;
    foreach (GroupDump g in groups)
    {
        if (!pick(g)) continue;
        foreach (FieldDump f in g.Fields)
        {
            float[]? fl = f.Floats;
            if (fl is null || fl.Length < 6 || Math.Abs(fl[0] - 600) > 1e-3) continue;   // code-600 collision list
            int np = fl.Length / 2 - 2;                                                   // loader::query_visual point count
            for (int k = 2; k < 2 + np * 2 && k + 1 < fl.Length; k += 2)
            {
                double x = fl[k], y = fl[k + 1];
                if (Math.Abs(x) > 1e6 || Math.Abs(y) > 1e6) continue;
                a = Math.Min(a, x); b = Math.Max(b, x); c = Math.Min(c, y); d = Math.Max(d, y); any = true;
            }
        }
    }
    return any ? (a, b, c, d) : null;
}
var tableRaw = TableExtent(g => string.Equals(g.Name, "table", StringComparison.OrdinalIgnoreCase)) ?? TableExtent(_ => true);

// ── view rotation (0/90/180/270), applied about the origin as a display-only
//    transform — blueprint.json and --filter stay in the .dat's own coordinates.
//    Fitting the box to the ROTATED points re-centers it and swaps the aspect for 90/270.
(double x, double y) Rot(double x, double y) => rotate switch
{
    90 => (-y, x),
    180 => (-x, -y),
    270 => (y, -x),
    _ => (x, y),
};

// table extent rotated into the SVG frame (frames the plot + feeds calibration)
(double x, double y)[]? tblCorners = tableRaw is { } te
    ? new[] { Rot(te.x0, te.y0), Rot(te.x1, te.y0), Rot(te.x1, te.y1), Rot(te.x0, te.y1) }
    : null;
double? tblMinX = tblCorners?.Min(p => p.x), tblMaxX = tblCorners?.Max(p => p.x),
        tblMinY = tblCorners?.Min(p => p.y), tblMaxY = tblCorners?.Max(p => p.y);

// ── bounding box: frame on the real table when we have it (full outline, maps 1:1 to the
//    playfield); else a robust 2–98 percentile of the drawn points so strays don't blow up scale ──
var rpts = geoms.SelectMany(g => g.Points.Select(p => Rot(p.x, p.y))).ToList();
var xs = rpts.Select(p => p.x).OrderBy(v => v).ToList();
var ys = rpts.Select(p => p.y).OrderBy(v => v).ToList();
if (xs.Count == 0)
{
    Console.Error.WriteLine("No drawable geometry found. Try --offset 0, or inspect the JSON dump for the coordinate arrays.");
}
double Pct(List<double> s, double p) => s.Count == 0 ? 0 : s[Math.Clamp((int)(p * (s.Count - 1)), 0, s.Count - 1)];
double minX, maxX, minY, maxY, pad;
if (tblMinX is double)
{
    minX = tblMinX.Value; maxX = tblMaxX!.Value; minY = tblMinY!.Value; maxY = tblMaxY!.Value; pad = 0.02;
}
else
{
    minX = Pct(xs, 0.02); maxX = Pct(xs, 0.98); minY = Pct(ys, 0.02); maxY = Pct(ys, 0.98); pad = 0.05;
}
double rx = Math.Max(1e-6, maxX - minX), ry = Math.Max(1e-6, maxY - minY);
minX -= rx * pad; maxX += rx * pad; minY -= ry * pad; maxY += ry * pad;
rx = maxX - minX; ry = maxY - minY;

Console.WriteLine($"World bounds (robust): x [{minX:0.##} .. {maxX:0.##}]  y [{minY:0.##} .. {maxY:0.##}]{(rotate != 0 ? $"  (rotated {rotate} deg)" : "")}");

// ── SVG ──────────────────────────────────────────────────────────────────────
const double W = 620;
double H = Math.Clamp(W * ry / rx, 200, 1600);
double SX(double x, double y) { (double u, _) = Rot(x, y); return (u - minX) / rx * W; }
double SY(double x, double y) { (_, double v) = Rot(x, y); return flipY ? (v - minY) / ry * H : (maxY - v) / ry * H; }

var svg = new StringBuilder();
svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{W:0}\" height=\"{H:0}\" viewBox=\"0 0 {W:0} {H:0}\" font-family=\"monospace\">\n");
svg.Append(CultureInfo.InvariantCulture, $"<rect width=\"{W:0}\" height=\"{H:0}\" fill=\"#0c0f17\"/>\n");
svg.Append("<defs><clipPath id=\"cp\"><rect x=\"0\" y=\"0\" width=\"" + W.ToString("0", CultureInfo.InvariantCulture) + "\" height=\"" + H.ToString("0", CultureInfo.InvariantCulture) + "\"/></clipPath></defs>\n");
svg.Append("<g clip-path=\"url(#cp)\">\n");
foreach (Geom geo in geoms)
{
    string col = Hsl(geo.Group * 47 % 360);
    var d = new StringBuilder("M ");
    for (int i = 0; i < geo.Points.Count; i++)
    {
        if (i > 0) d.Append(" L ");
        d.Append(CultureInfo.InvariantCulture, $"{SX(geo.Points[i].x, geo.Points[i].y):0.#},{SY(geo.Points[i].x, geo.Points[i].y):0.#}");
    }
    svg.Append(CultureInfo.InvariantCulture, $"<path d=\"{d}\" fill=\"none\" stroke=\"{col}\" stroke-width=\"1.3\" stroke-linejoin=\"round\" opacity=\"0.8\"/>\n");
}
// group-index labels at each geometry's first point (helps map a shape → its .dat group → a name).
// One label per distinct group/name: a ramp now yields ~18 triangle polylines, so labelling every
// one would bury the plot.
var labelled = new HashSet<string>();
foreach (Geom geo in geoms)
{
    var p0 = geo.Points[0];
    string lbl = geo.Name is { Length: > 0 } ? $"{geo.Group}:{geo.Name}" : geo.Group.ToString();
    if (!labelled.Add(lbl)) continue;
    svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{SX(p0.x, p0.y) + 2:0.#}\" y=\"{SY(p0.x, p0.y) - 2:0.#}\" fill=\"#8a93ad\" font-size=\"7\">{Esc(lbl)}</text>\n");
}
svg.Append("</g>\n");
svg.Append(CultureInfo.InvariantCulture, $"<text x=\"8\" y=\"14\" fill=\"#3fe0ff\" font-size=\"11\">DatBlueprint — {geoms.Count} arrays · offset {offset} · x[{minX:0.#},{maxX:0.#}] y[{minY:0.#},{maxY:0.#}]{(autoDropped > 0 ? $" · −{autoDropped} outliers" : "")}{(rotate != 0 ? $" · rot {rotate}°" : "")}{(flipY ? " · flipY" : "")}</text>\n");
svg.Append("</svg>\n");

string svgPath = Path.Combine(outDir, "blueprint.svg");
File.WriteAllText(svgPath, svg.ToString());
Console.WriteLine($"Wrote   {svgPath}");

// ── calibration for the editor (ball radius + table extent were computed above) ──────────────
// `table` is the real outer extent; `svg` is the (slightly padded) rect the canvas spans — with
// table framing they nearly coincide, so the editor maps the overlay 1:1 onto the playfield.
object? calibration = tblMinX is double
    ? new
    {
        unit = "dat",
        rotate,
        ballRadius,
        table = new { minX = tblMinX, maxX = tblMaxX, minY = tblMinY, maxY = tblMaxY, width = tblMaxX - tblMinX, height = tblMaxY - tblMinY },
        svg = new { minX, maxX, minY, maxY, width = W, height = H },
    }
    : null;
if (calibration != null)
    Console.WriteLine($"Calibration: ball r={ballRadius:0.###}  table {tblMaxX - tblMinX:0.#}×{tblMaxY - tblMinY:0.#} (dat units)");

// ── JSON dump (full detail for calibration + future auto-conversion) ──────────
var dump = new
{
    appName = appName.Trim(),
    description = description.Trim(),
    groupCount = groups.Count,
    offsetUsed = offset,
    worldBounds = new { minX, maxX, minY, maxY },
    calibration,
    groups = groups.Select(g => new
    {
        index = g.Index,
        name = g.Name,
        fields = g.Fields.Select(f => new
        {
            type = f.Type,
            size = f.Size,
            asShort = f.AsShort,
            asString = f.AsString,
            floatCount = f.Floats?.Length,
            floatMin = f.Floats is { Length: > 0 } ? f.Floats.Where(v => !float.IsNaN(v) && !float.IsInfinity(v)).DefaultIfEmpty(0).Min() : (float?)null,
            floatMax = f.Floats is { Length: > 0 } ? f.Floats.Where(v => !float.IsNaN(v) && !float.IsInfinity(v)).DefaultIfEmpty(0).Max() : (float?)null,
            floats = f.Floats is { Length: <= 64 } ? f.Floats : f.Floats?.Take(64).ToArray(),
        }),
    }),
};
string jsonPath = Path.Combine(outDir, "blueprint.json");
File.WriteAllText(jsonPath, JsonSerializer.Serialize(dump, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Wrote   {jsonPath}");
Console.WriteLine();
Console.WriteLine("Next: open tools/table-editor.html, click \"Load reference image\", pick blueprint.svg,");
Console.WriteLine("      set opacity ~50%, and trace. If it looks upside-down, re-run with --flip-y.");
Console.WriteLine("      If it's a hairball, read blueprint.json bounds and re-run with --filter x0 y0 x1 y1.");
return 0;

// ─────────────────────────────────────────────────────────────────────────────
static List<GroupDump> ParseDat(byte[] b, short[] fieldSize, out string appName, out string description, out int groupCount)
{
    int pos = 0;
    string sig = ReadFixedString(b, ref pos, 21);
    if (!sig.StartsWith("PARTOUT(4.0)RESOURCE", StringComparison.Ordinal))
        throw new InvalidDataException($"not a PARTOUT(4.0)RESOURCE file (signature was \"{sig}\")");
    appName = ReadFixedString(b, ref pos, 50);
    description = ReadFixedString(b, ref pos, 100);
    _ = ReadI32(b, ref pos);                 // FileSize
    groupCount = ReadU16(b, ref pos);        // NumberOfGroups
    _ = ReadI32(b, ref pos);                 // SizeOfBody
    int unknown = ReadU16(b, ref pos);       // trailing skip count
    if (unknown != 0) pos += unknown;

    var groups = new List<GroupDump>(groupCount);
    for (int gi = 0; gi < groupCount && pos < b.Length; gi++)
    {
        int entryCount = b[pos++];
        var g = new GroupDump { Index = gi, Fields = new List<FieldDump>(entryCount) };
        for (int e = 0; e < entryCount; e++)
        {
            int type = b[pos++];
            int size = type < fieldSize.Length && fieldSize[type] >= 0 ? fieldSize[type] : ReadI32(b, ref pos);
            if (size < 0 || pos + size > b.Length)
                throw new InvalidDataException($"group {gi} entry {e}: bad field size {size} at offset {pos}");
            var buf = new byte[size];
            Array.Copy(b, pos, buf, 0, size);
            pos += size;

            var fd = new FieldDump { Type = type, Size = size };
            if (size == 2) fd.AsShort = BitConverter.ToInt16(buf, 0);
            fd.AsString = TryString(buf);
            if (size >= 4 && size % 4 == 0)
            {
                var fl = new float[size / 4];
                for (int j = 0; j < fl.Length; j++) fl[j] = BitConverter.ToSingle(buf, j * 4);
                fd.Floats = fl;
            }
            g.Fields.Add(fd);
        }
        // A group's human name is its first clean string field (component id like "flipper1").
        g.Name = g.Fields.FirstOrDefault(f => f.AsString is { Length: >= 2 })?.AsString;
        groups.Add(g);
    }
    return groups;
}

static string ReadFixedString(byte[] b, ref int pos, int len)
{
    int end = Math.Min(pos + len, b.Length);
    int z = pos;
    while (z < end && b[z] != 0) z++;
    string s = Encoding.ASCII.GetString(b, pos, z - pos);
    pos += len;
    return s;
}
static int ReadU16(byte[] b, ref int pos) { int v = BitConverter.ToUInt16(b, pos); pos += 2; return v; }
static int ReadI32(byte[] b, ref int pos) { int v = BitConverter.ToInt32(b, pos); pos += 4; return v; }

static string? TryString(byte[] buf)
{
    if (buf.Length is 0 or > 200) return null;
    int end = buf.Length;
    while (end > 0 && buf[end - 1] == 0) end--;          // trim trailing nulls
    if (end < 2) return null;
    for (int i = 0; i < end; i++)
    {
        byte c = buf[i];
        bool printable = c is >= 32 and <= 126 || c is 9 or 10 or 13;
        if (!printable) return null;
    }
    return Encoding.ASCII.GetString(buf, 0, end);
}

static string Hsl(int hue) => $"hsl({hue},70%,62%)";
static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

sealed class GroupDump
{
    public int Index;
    public string? Name;
    public List<FieldDump> Fields = new();
}
sealed class FieldDump
{
    public int Type;
    public int Size;
    public short? AsShort;
    public string? AsString;
    public float[]? Floats;
}
readonly record struct Geom(int Group, string? Name, List<(double x, double y)> Points);
