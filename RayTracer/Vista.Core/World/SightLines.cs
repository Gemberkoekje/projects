using Vista.Scene;

namespace Vista.World;

/// <summary>What a camera's rays see on a height field: how many end on the terrain and how far away, how many escape to
/// the sky, and how many leave the grid below its highest point — the pixels that would show what lies beyond it.</summary>
/// <param name="Pixels">Rays traced.</param>
/// <param name="Terrain">Rays that hit the terrain inside the grid.</param>
/// <param name="Sky">Rays that rose above the grid's highest point without hitting.</param>
/// <param name="Beyond">Rays that left the grid's footprint below its highest point.</param>
/// <param name="Distances">The hit distances in metres, sorted.</param>
public sealed record SightLineStats(int Pixels, int Terrain, int Sky, int Beyond, double[] Distances)
{
    /// <summary>The <paramref name="q"/> quantile of the hit distances (0 when nothing was hit).</summary>
    public double Quantile(double q) => Distances.Length == 0 ? 0.0 : Distances[(int)Math.Round(q * (Distances.Length - 1))];

    /// <summary>The fraction of the hits farther than <paramref name="metres"/>.</summary>
    public double FartherThan(double metres) => Distances.Length == 0 ? 0.0 : Distances.Count(d => d > metres) / (double)Distances.Length;
}

/// <summary>
/// Sight lines over a height field (vista-plan §10 M1.7: the grid's extent is chosen by measurement, including what the views
/// see beyond it). Each pixel's ray from a pinhole <see cref="CameraSpec"/> is marched over the bilinear surface with a step
/// that grows with the ray's height above it, so a ray grazing a ridge may miss it by a little: good for statistics, not for
/// images.
/// </summary>
public static class SightLines
{
    /// <summary>Traces <paramref name="width"/>×<paramref name="height"/> rays of <paramref name="camera"/> over <paramref name="field"/>.</summary>
    public static SightLineStats Measure(HeightField field, CameraSpec camera, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(camera);
        double tanHalf = Math.Tan(camera.VerticalFovRadians() * 0.5);
        double aspect = (double)width / height;
        Vector3D forward = camera.Forward(), right = camera.Right(), up = camera.Up();
        double top = field.MaxHeight;
        double min = field.OriginX, max = field.OriginX + field.Extent;
        var results = new double[width * height];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                double sx = ((x + 0.5) / width * 2.0 - 1.0) * tanHalf * aspect;
                double sy = (1.0 - (y + 0.5) / height * 2.0) * tanHalf;
                Vector3D d = (forward + right * sx + up * sy).Normalized();
                results[y * width + x] = March(field, camera.Position, d, top, min, max);
            }
        });
        double[] hits = results.Where(double.IsFinite).Order().ToArray();
        return new SightLineStats(results.Length, hits.Length, results.Count(double.IsPositiveInfinity), results.Count(double.IsNaN), hits);
    }

    // The hit distance; +∞ for the sky, NaN for a ray that leaves the grid below its highest point.
    private static double March(HeightField field, WorldPoint origin, Vector3D d, double top, double min, double max)
    {
        double t = 0.0;
        double floor = field.CellSize * 0.5;
        for (int step = 0; step < 100_000; step++)
        {
            double x = origin.X + d.X * t, y = origin.Y + d.Y * t, z = origin.Z + d.Z * t;
            if (y > top && d.Y >= 0.0)
                return double.PositiveInfinity;
            if (x < min || x > max || z < min || z > max)
                return y > top && d.Y >= 0.0 ? double.PositiveInfinity : double.NaN;
            double clearance = y - field.HeightAt(x, z);
            if (clearance <= 0.0)
                return t;
            t += Math.Max(floor, 0.15 * clearance);
        }
        return double.NaN;
    }
}
