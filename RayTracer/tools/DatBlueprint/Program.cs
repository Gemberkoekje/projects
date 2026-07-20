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
//   --filter x0 y0 x1 y1   only draw fields whose points mostly fall in this world box
//                  (use after a first pass, reading bounds from the JSON, to drop noise)
//
// Container format (from k4zmu2a/SpaceCadetPinball partman.cpp load_records):
//   183-byte header: char sig[21] ("PARTOUT(4.0)RESOURCE"), char appName[50],
//     char desc[100], int32 fileSize, uint16 groupCount, int32 bodySize, uint16 unknown.
//   If unknown != 0, skip `unknown` bytes.
//   Then groupCount groups. Each group: uint8 entryCount, then that many entries.
//   Each entry: uint8 type; size = _field_size[type] if >=0 else read uint32; then
//     `size` raw bytes. (Bitmap8/16 and Full-Tilt zmap all net to exactly `size` bytes
//     consumed, so a generic skip is byte-exact without special-casing them.)
// ─────────────────────────────────────────────────────────────────────────────

// _field_size[type]: >=0 fixed byte size, -1 read a uint32 length. Verbatim from partman.
short[] fieldSize = { 2, -1, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 0 };

var argList = new List<string>(args);
int offset = 2, minPoints = 3;
bool flipY = false;
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
    Console.Error.WriteLine("usage: dotnet run --project tools/DatBlueprint -- <pinball.dat> [outDir] [--offset N] [--min-points N] [--flip-y] [--filter x0 y0 x1 y1]");
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

// ── collect drawable geometry ────────────────────────────────────────────────
var geoms = new List<Geom>();
foreach (GroupDump g in groups)
    foreach (FieldDump f in g.Fields)
    {
        if (f.Floats is null) continue;
        int n = f.Floats.Length;
        if (n - offset < minPoints * 2) continue;
        var pts = new List<(double x, double y)>();
        for (int k = offset; k + 1 < n; k += 2)
        {
            float x = f.Floats[k], y = f.Floats[k + 1];
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)) { pts.Clear(); break; }
            if (Math.Abs(x) > 1e6 || Math.Abs(y) > 1e6) { pts.Clear(); break; }
            pts.Add((x, y));
        }
        if (pts.Count < minPoints) continue;
        if (filter is not null)
        {
            int inBox = pts.Count(p => p.x >= filter[0] && p.x <= filter[2] && p.y >= filter[1] && p.y <= filter[3]);
            if (inBox < pts.Count * 0.6) continue;
        }
        geoms.Add(new Geom(g.Index, g.Name, pts));
    }

Console.WriteLine($"Drawable float-array fields: {geoms.Count}  (offset {offset}, min-points {minPoints}{(filter != null ? ", filtered" : "")})");

// ── robust bounding box (2–98 percentile) so a few stray arrays don't blow up the scale ──
var xs = geoms.SelectMany(g => g.Points.Select(p => p.x)).OrderBy(v => v).ToList();
var ys = geoms.SelectMany(g => g.Points.Select(p => p.y)).OrderBy(v => v).ToList();
if (xs.Count == 0)
{
    Console.Error.WriteLine("No drawable geometry found. Try --offset 0, or inspect the JSON dump for the coordinate arrays.");
}
double Pct(List<double> s, double p) => s.Count == 0 ? 0 : s[Math.Clamp((int)(p * (s.Count - 1)), 0, s.Count - 1)];
double minX = Pct(xs, 0.02), maxX = Pct(xs, 0.98), minY = Pct(ys, 0.02), maxY = Pct(ys, 0.98);
double rx = Math.Max(1e-6, maxX - minX), ry = Math.Max(1e-6, maxY - minY);
minX -= rx * 0.05; maxX += rx * 0.05; minY -= ry * 0.05; maxY += ry * 0.05;
rx = maxX - minX; ry = maxY - minY;

Console.WriteLine($"World bounds (robust): x [{minX:0.##} .. {maxX:0.##}]  y [{minY:0.##} .. {maxY:0.##}]");

// ── SVG ──────────────────────────────────────────────────────────────────────
const double W = 620;
double H = Math.Clamp(W * ry / rx, 200, 1600);
double SX(double x) => (x - minX) / rx * W;
double SY(double y) => flipY ? (y - minY) / ry * H : (maxY - y) / ry * H;

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
        d.Append(CultureInfo.InvariantCulture, $"{SX(geo.Points[i].x):0.#},{SY(geo.Points[i].y):0.#}");
    }
    svg.Append(CultureInfo.InvariantCulture, $"<path d=\"{d}\" fill=\"none\" stroke=\"{col}\" stroke-width=\"1.3\" stroke-linejoin=\"round\" opacity=\"0.8\"/>\n");
}
// group-index labels at each geometry's first point (helps map a shape → its .dat group → a name)
foreach (Geom geo in geoms)
{
    var p0 = geo.Points[0];
    string lbl = geo.Name is { Length: > 0 } ? $"{geo.Group}:{geo.Name}" : geo.Group.ToString();
    svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{SX(p0.x) + 2:0.#}\" y=\"{SY(p0.y) - 2:0.#}\" fill=\"#8a93ad\" font-size=\"7\">{Esc(lbl)}</text>\n");
}
svg.Append("</g>\n");
svg.Append(CultureInfo.InvariantCulture, $"<text x=\"8\" y=\"14\" fill=\"#3fe0ff\" font-size=\"11\">DatBlueprint — {geoms.Count} arrays · offset {offset} · x[{minX:0.#},{maxX:0.#}] y[{minY:0.#},{maxY:0.#}]{(flipY ? " · flipY" : "")}</text>\n");
svg.Append("</svg>\n");

string svgPath = Path.Combine(outDir, "blueprint.svg");
File.WriteAllText(svgPath, svg.ToString());
Console.WriteLine($"Wrote   {svgPath}");

// ── JSON dump (full detail for calibration + future auto-conversion) ──────────
var dump = new
{
    appName = appName.Trim(),
    description = description.Trim(),
    groupCount = groups.Count,
    offsetUsed = offset,
    worldBounds = new { minX, maxX, minY, maxY },
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
