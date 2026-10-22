using Vista.Scene;
using Vista.World;

namespace Vista.Output;

/// <summary>
/// Turns the renderer's auxiliary buffers into 8-bit RGB images a person or an LLM can read at a glance
/// (vista-plan §7.4 "debug outputs at preview size"). Every function returns tightly packed RGB rows,
/// top row first, ready for <see cref="PngEncoder"/>. Sky pixels get a fixed, distinct colour.
/// </summary>
public static class DebugImages
{
    /// <summary>Sky colour in the depth image (dark blue — never produced by a terrain depth).</summary>
    public static readonly (byte R, byte G, byte B) DepthSky = (0, 0, 96);

    private static readonly (byte R, byte G, byte B) Black = (0, 0, 0);

    /// <summary>Albedo (linear RGB) through the plain sRGB transfer — no exposure, no tone curve.</summary>
    public static byte[] Albedo(ReadOnlySpan<float> rgb)
    {
        var bytes = new byte[rgb.Length];
        for (int i = 0; i < rgb.Length; i++)
            bytes[i] = DisplayTransform.EncodeSrgb(rgb[i]);
        return bytes;
    }

    /// <summary>World normals mapped <c>[−1, 1] → [0, 255]</c> per axis (up = +Y shows green).</summary>
    public static byte[] Normal(ReadOnlySpan<float> normals)
    {
        var bytes = new byte[normals.Length];
        for (int i = 0; i < normals.Length; i++)
            bytes[i] = normals[i] is var n && float.IsFinite(n) ? (byte)Math.Clamp((n * 0.5f + 0.5f) * 255f + 0.5f, 0f, 255f) : (byte)0;
        return bytes;
    }

    /// <summary>
    /// Depth on a log scale between the nearest and farthest terrain hit in the frame: near is white, far
    /// is dark grey (never black), sky is <see cref="DepthSky"/>.
    /// </summary>
    public static byte[] Depth(ReadOnlySpan<float> depth)
    {
        double min = double.MaxValue, max = 0.0;
        foreach (float d in depth)
        {
            if (IsHit(d))
            {
                min = Math.Min(min, d);
                max = Math.Max(max, d);
            }
        }
        double logMin = Math.Log(Math.Max(min, 1e-3));
        double range = Math.Max(Math.Log(Math.Max(max, 1e-3)) - logMin, 1e-9);

        var bytes = new byte[depth.Length * 3];
        for (int p = 0; p < depth.Length; p++)
        {
            float d = depth[p];
            if (!IsHit(d))
            {
                Put(bytes, p, DepthSky);
                continue;
            }
            double t = (Math.Log(Math.Max(d, 1e-3)) - logMin) / range; // 0 near … 1 far
            byte g = (byte)Math.Round(255.0 - t * (255.0 - 32.0));
            Put(bytes, p, (g, g, g));
        }
        return bytes;
    }

    /// <summary>One stable colour per material id; sky (negative id) is black.</summary>
    public static byte[] Material(ReadOnlySpan<float> materialIds)
    {
        var bytes = new byte[materialIds.Length * 3];
        for (int p = 0; p < materialIds.Length; p++)
        {
            float id = materialIds[p];
            Put(bytes, p, id >= 0f && float.IsFinite(id) ? PaletteColor((int)id) : Black);
        }
        return bytes;
    }

    /// <summary>
    /// The data tier of each pixel's material (vista-plan §6.2): measured tiers in greens, the model tier
    /// yellow, built-in orange, sky black.
    /// </summary>
    public static byte[] Tier(ReadOnlySpan<float> materialIds, IReadOnlyList<VistaMaterial> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        var bytes = new byte[materialIds.Length * 3];
        for (int p = 0; p < materialIds.Length; p++)
        {
            float id = materialIds[p];
            int index = id >= 0f && float.IsFinite(id) ? (int)id : -1;
            Put(bytes, p, index >= 0 && index < materials.Count ? TierColor(materials[index].Tier) : Black);
        }
        return bytes;
    }

    /// <summary>Legend colour of a data tier.</summary>
    public static (byte R, byte G, byte B) TierColor(DataTier tier) => tier switch
    {
        DataTier.LocalCache => (0, 160, 60),
        DataTier.Download => (60, 200, 90),
        DataTier.ManualImport => (140, 230, 140),
        DataTier.Model => (240, 220, 40),
        _ => (240, 130, 20),
    };

    /// <summary>
    /// Variance as a log-scale heat map spanning four decades below the frame's maximum: black (converged)
    /// through red and yellow to white (noisiest).
    /// </summary>
    public static byte[] Variance(ReadOnlySpan<float> variance)
    {
        double max = 0.0;
        foreach (float v in variance)
        {
            if (float.IsFinite(v))
                max = Math.Max(max, v);
        }
        var bytes = new byte[variance.Length * 3];
        if (max <= 0.0)
            return bytes;
        double logMax = Math.Log10(max);
        for (int p = 0; p < variance.Length; p++)
        {
            float v = variance[p];
            double t = v > 0f && float.IsFinite(v) ? Math.Clamp((Math.Log10(v) - logMax + 4.0) / 4.0, 0.0, 1.0) : 0.0;
            Put(bytes, p, Heat(t));
        }
        return bytes;
    }

    /// <summary>
    /// A top-down shaded map of the height field, north up, at most <paramref name="maxSize"/> pixels per
    /// side, with the camera marked in red and its heading drawn — the dry run's only image, so world
    /// generation can be inspected without a GPU.
    /// </summary>
    public static (byte[] Rgb, int Size) HeightMap(HeightField field, CameraSpec? camera, int maxSize = 512)
    {
        ArgumentNullException.ThrowIfNull(field);
        int size = Math.Min(field.Size, maxSize);
        double step = (field.Size - 1) / (double)Math.Max(size - 1, 1);
        float min = field.MinHeight;
        float range = Math.Max(field.MaxHeight - min, 1e-3f);
        var bytes = new byte[size * size * 3];
        for (int row = 0; row < size; row++)
        {
            int j = (int)Math.Round((size - 1 - row) * step); // north (high j) at the top
            int j0 = Math.Max(j - 1, 0), j1 = Math.Min(j + 1, field.Size - 1);
            for (int col = 0; col < size; col++)
            {
                int i = (int)Math.Round(col * step);
                int i0 = Math.Max(i - 1, 0), i1 = Math.Min(i + 1, field.Size - 1);
                // Lambert hillshade under a light from the north-west, tinted by elevation.
                double dhdx = (field[i1, j] - field[i0, j]) / ((i1 - i0) * field.CellSize);
                double dhdz = (field[i, j1] - field[i, j0]) / ((j1 - j0) * field.CellSize);
                double shade = Math.Clamp((dhdx * 0.577 - dhdz * 0.577 + 0.577) / Math.Sqrt(dhdx * dhdx + 1.0 + dhdz * dhdz), 0.0, 1.0);
                double e = (field[i, j] - min) / range;
                double light = 0.35 + 0.65 * shade;
                Put(bytes, row * size + col, (
                    (byte)Math.Round(light * (90 + 140 * e)),
                    (byte)Math.Round(light * (110 + 120 * e)),
                    (byte)Math.Round(light * (80 + 150 * e))));
            }
        }

        if (camera is not null)
            MarkCamera(bytes, size, field, camera);
        return (bytes, size);
    }

    /// <summary>
    /// The erosion maps over a grey hillshade of the eroded surface, top-down with north up like <see cref="HeightMap"/>: loose
    /// material in tan (full at 30 m and more, on a log scale), the mean discharge in blue where it is in the grid's top tenth (log
    /// scale from the 90th percentile, none, to the 99.9th, full: the channel network), water still standing at the end deeper than 0.5 m in dark blue. At most <paramref name="maxSize"/> pixels a
    /// side (nearest samples).
    /// </summary>
    public static (byte[] Rgb, int Size) ErosionMap(ErosionResult result, CameraSpec? camera, int maxSize = 1024)
    {
        ArgumentNullException.ThrowIfNull(result);
        HeightField field = result.Surface;
        int size = Math.Min(field.Size, maxSize);
        double step = (field.Size - 1) / (double)Math.Max(size - 1, 1);
        float[] sorted = (float[])result.Discharge.Clone();
        Array.Sort(sorted);
        double noDischarge = Math.Log(Math.Max(sorted[(int)((sorted.Length - 1) * 0.9)], 1e-6f));
        double fullDischarge = Math.Log(Math.Max(sorted[(int)((sorted.Length - 1) * 0.999)], 2e-6f)) - noDischarge;
        double fullLoose = Math.Log(1.0 + 30.0);
        var bytes = new byte[size * size * 3];
        Parallel.For(0, size, row =>
        {
            int j = (int)Math.Round((size - 1 - row) * step);
            int j0 = Math.Max(j - 1, 0), j1 = Math.Min(j + 1, field.Size - 1);
            for (int col = 0; col < size; col++)
            {
                int i = (int)Math.Round(col * step);
                int i0 = Math.Max(i - 1, 0), i1 = Math.Min(i + 1, field.Size - 1);
                double dhdx = (field[i1, j] - field[i0, j]) / ((i1 - i0) * field.CellSize);
                double dhdz = (field[i, j1] - field[i, j0]) / ((j1 - j0) * field.CellSize);
                double shade = Math.Clamp((dhdx * 0.577 - dhdz * 0.577 + 0.577) / Math.Sqrt(dhdx * dhdx + 1.0 + dhdz * dhdz), 0.0, 1.0);
                double grey = 60 + 170 * shade;
                (double r, double g, double b) = (grey, grey, grey);
                int c = j * field.Size + i;
                double loose = Math.Clamp(Math.Log(1.0 + result.Loose[c]) / fullLoose, 0.0, 1.0);
                (r, g, b) = (r + (215 * shade + 40 - r) * loose, g + (175 * shade + 30 - g) * loose, b + (110 * shade + 20 - b) * loose);
                double flow = Math.Clamp((Math.Log(Math.Max(result.Discharge[c], 1e-6f)) - noDischarge) / fullDischarge, 0.0, 1.0);
                (r, g, b) = (r + (30 - r) * flow, g + (110 - g) * flow, b + (255 - b) * flow);
                if (result.StandingWater[c] > 0.5f)
                    (r, g, b) = (20, 40, 150);
                Put(bytes, row * size + col, ((byte)Math.Round(r), (byte)Math.Round(g), (byte)Math.Round(b)));
            }
        });
        if (camera is not null)
            MarkCamera(bytes, size, field, camera);
        return (bytes, size);
    }

    /// <summary>
    /// The drainage (M1.11a) over a grey hillshade of <paramref name="surface"/>, top-down with north up like
    /// <see cref="ErosionMap"/>: the upstream area in blue from 0.05 km² (faint) to the largest river on the grid (full), on a log
    /// scale, and lakes (filled deeper than 1 m) in dark blue. Each pixel shows the largest upstream area among the cells it
    /// covers, so a channel one cell wide survives the reduction to at most <paramref name="maxSize"/> pixels a side.
    /// </summary>
    public static (byte[] Rgb, int Size) DrainageImage(HeightField surface, DrainageMap drainage, CameraSpec? camera, int maxSize = 1024)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(drainage);
        if (drainage.Size != surface.Size)
            throw new ArgumentException("The drainage is not on the surface's grid.", nameof(drainage));
        int n = surface.Size;
        int size = Math.Min(n, maxSize);
        double step = (n - 1) / (double)Math.Max(size - 1, 1);
        double faint = Math.Log(0.05e6 / drainage.CellArea);
        double full = Math.Log(drainage.Upstream.Max()) - faint;
        var bytes = new byte[size * size * 3];
        Parallel.For(0, size, row =>
        {
            int j = (int)Math.Round((size - 1 - row) * step);
            int j0 = Math.Max(j - 1, 0), j1 = Math.Min(j + 1, n - 1);
            int bj0 = (int)Math.Round((size - 1 - row - 0.5) * step), bj1 = (int)Math.Round((size - 1 - row + 0.5) * step);
            for (int col = 0; col < size; col++)
            {
                int i = (int)Math.Round(col * step);
                int i0 = Math.Max(i - 1, 0), i1 = Math.Min(i + 1, n - 1);
                double dhdx = (surface[i1, j] - surface[i0, j]) / ((i1 - i0) * surface.CellSize);
                double dhdz = (surface[i, j1] - surface[i, j0]) / ((j1 - j0) * surface.CellSize);
                double shade = Math.Clamp((dhdx * 0.577 - dhdz * 0.577 + 0.577) / Math.Sqrt(dhdx * dhdx + 1.0 + dhdz * dhdz), 0.0, 1.0);
                double grey = 60 + 170 * shade;
                int most = 0;
                bool lake = false;
                int bi0 = (int)Math.Round((col - 0.5) * step), bi1 = (int)Math.Round((col + 0.5) * step);
                for (int bj = Math.Max(bj0, 0); bj <= Math.Min(bj1, n - 1); bj++)
                {
                    for (int bi = Math.Max(bi0, 0); bi <= Math.Min(bi1, n - 1); bi++)
                    {
                        int c = bj * n + bi;
                        most = Math.Max(most, drainage.Upstream[c]);
                        lake |= drainage.FillDepth[c] > 1f;
                    }
                }
                double flow = Math.Clamp((Math.Log(most) - faint) / full, 0.0, 1.0);
                (double r, double g, double b) = (grey + (30 - grey) * flow, grey + (110 - grey) * flow, grey + (255 - grey) * flow);
                if (lake)
                    (r, g, b) = (20, 40, 150);
                Put(bytes, row * size + col, ((byte)Math.Round(r), (byte)Math.Round(g), (byte)Math.Round(b)));
            }
        });
        if (camera is not null)
            MarkCamera(bytes, size, surface, camera);
        return (bytes, size);
    }

    /// <summary>
    /// The rock's hardness at the bedrock's surface (M1.11d, <see cref="ErosionResult.Hardness"/>) over a hillshade of the eroded
    /// surface, top-down with north up like <see cref="ErosionMap"/>: soft rock in rust, hard rock in pale blue-grey, so the beds
    /// show as bands where slopes cut through them. At most <paramref name="maxSize"/> pixels a side (nearest samples).
    /// </summary>
    public static (byte[] Rgb, int Size) HardnessImage(ErosionResult result, CameraSpec? camera, int maxSize = 1024)
    {
        ArgumentNullException.ThrowIfNull(result);
        HeightField field = result.Surface;
        int n = field.Size;
        int size = Math.Min(n, maxSize);
        double step = (n - 1) / (double)Math.Max(size - 1, 1);
        var bytes = new byte[size * size * 3];
        Parallel.For(0, size, row =>
        {
            int j = (int)Math.Round((size - 1 - row) * step);
            int j0 = Math.Max(j - 1, 0), j1 = Math.Min(j + 1, n - 1);
            for (int col = 0; col < size; col++)
            {
                int i = (int)Math.Round(col * step);
                int i0 = Math.Max(i - 1, 0), i1 = Math.Min(i + 1, n - 1);
                double dhdx = (field[i1, j] - field[i0, j]) / ((i1 - i0) * field.CellSize);
                double dhdz = (field[i, j1] - field[i, j0]) / ((j1 - j0) * field.CellSize);
                double shade = Math.Clamp((dhdx * 0.577 - dhdz * 0.577 + 0.577) / Math.Sqrt(dhdx * dhdx + 1.0 + dhdz * dhdz), 0.0, 1.0);
                double light = 0.3 + 0.7 * shade;
                double h = result.Hardness[j * n + i];
                Put(bytes, row * size + col, (
                    (byte)Math.Round(light * (200 - 20 * h)),
                    (byte)Math.Round(light * (110 + 95 * h)),
                    (byte)Math.Round(light * (60 + 170 * h))));
            }
        });
        if (camera is not null)
            MarkCamera(bytes, size, field, camera);
        return (bytes, size);
    }

    private static void MarkCamera(byte[] bytes, int size, HeightField field, CameraSpec camera)
    {
        double ToCol(double x) => (x - field.OriginX) / field.Extent * (size - 1);
        double ToRow(double z) => (size - 1) - (z - field.OriginZ) / field.Extent * (size - 1);
        void Dot(double col, double row)
        {
            int c = (int)Math.Round(col), r = (int)Math.Round(row);
            if (c >= 0 && c < size && r >= 0 && r < size)
                Put(bytes, r * size + c, (255, 0, 0));
        }

        double c0 = ToCol(camera.Position.X), r0 = ToRow(camera.Position.Z);
        Vector3D f = camera.Forward();
        double flat = Math.Sqrt(f.X * f.X + f.Z * f.Z);
        int length = Math.Max(size / 12, 4);
        if (flat > 1e-9)
        {
            for (int s = 0; s <= length; s++)
                Dot(c0 + f.X / flat * s, r0 - f.Z / flat * s);
        }
        for (int dr = -1; dr <= 1; dr++)
        {
            for (int dc = -1; dc <= 1; dc++)
                Dot(c0 + dc, r0 + dr);
        }
    }

    private static (byte R, byte G, byte B) PaletteColor(int id)
    {
        ulong h = StableHash.Mix((ulong)id);
        return ((byte)(64 + (h & 0xBF)), (byte)(64 + ((h >> 8) & 0xBF)), (byte)(64 + ((h >> 16) & 0xBF)));
    }

    private static (byte R, byte G, byte B) Heat(double t)
    {
        // black → red → yellow → white
        double r = Math.Clamp(t * 3.0, 0.0, 1.0);
        double g = Math.Clamp(t * 3.0 - 1.0, 0.0, 1.0);
        double b = Math.Clamp(t * 3.0 - 2.0, 0.0, 1.0);
        return ((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    private static bool IsHit(float depth) => depth > 0f && float.IsFinite(depth);

    private static void Put(byte[] bytes, int pixel, (byte R, byte G, byte B) c)
    {
        bytes[pixel * 3] = c.R;
        bytes[pixel * 3 + 1] = c.G;
        bytes[pixel * 3 + 2] = c.B;
    }
}
