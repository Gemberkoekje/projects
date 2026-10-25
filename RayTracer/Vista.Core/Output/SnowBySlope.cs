using Vista.Scene;

namespace Vista.Output;

/// <summary>
/// How much of a render's ground is white by slope (vista-plan §10 M1.13 verify, "bare steep faces"): from the material-id output
/// (sample 0's first hit) and the normal output (the mean shading normal), the terrain's pixels counted by the slope of their normal
/// against the vertical, in bands, and the share of each band whose material is snow, firn or glacier ice. Snow sheds from faces
/// steeper than it lies on, so the steep bands stay dark while the ledges and gullies between them hold their snow.
/// </summary>
public static class SnowBySlope
{
    /// <summary>The bands' lower edges, degrees (the last runs to 90°).</summary>
    public static IReadOnlyList<double> BandsFromDegrees { get; } = [0.0, 30.0, 40.0, 50.0];

    /// <summary>
    /// The bands of a render's <paramref name="materialIds"/> (one float a pixel, negative for sky) and <paramref name="normals"/> (three
    /// floats a pixel); null when no pixel saw terrain.
    /// </summary>
    public static IReadOnlyList<SlopeBand>? Measure(ReadOnlySpan<float> materialIds, ReadOnlySpan<float> normals)
    {
        if (normals.Length != materialIds.Length * 3)
            throw new ArgumentException($"Expected {materialIds.Length * 3} normal floats, got {normals.Length}.", nameof(normals));
        int bands = BandsFromDegrees.Count;
        var pixels = new long[bands];
        var white = new long[bands];
        for (int p = 0; p < materialIds.Length; p++)
        {
            float id = materialIds[p];
            if (!(id >= 0f))
                continue;
            float y = normals[p * 3 + 1];
            if (!float.IsFinite(y) || normals[p * 3] == 0f && y == 0f && normals[p * 3 + 2] == 0f)
                continue;
            double slope = Math.Acos(Math.Clamp(y, -1f, 1f)) * (180.0 / Math.PI);
            int band = bands - 1;
            while (band > 0 && slope < BandsFromDegrees[band])
                band--;
            pixels[band]++;
            if ((int)id is MaterialCatalog.SnowId or MaterialCatalog.FirnId or MaterialCatalog.IceId)
                white[band]++;
        }
        if (pixels.Sum() == 0)
            return null;
        return [.. Enumerable.Range(0, bands).Select(b => new SlopeBand(BandsFromDegrees[b], b + 1 < bands ? BandsFromDegrees[b + 1] : 90.0, pixels[b],
            pixels[b] > 0 ? white[b] / (double)pixels[b] : 0.0))];
    }
}

/// <summary>One band of <see cref="SnowBySlope"/>.</summary>
/// <param name="FromDegrees">Its lower slope, degrees.</param>
/// <param name="ToDegrees">Its upper slope.</param>
/// <param name="Pixels">The terrain's pixels in it.</param>
/// <param name="WhiteShare">The share of them whose material is snow, firn or glacier ice.</param>
public sealed record SlopeBand(double FromDegrees, double ToDegrees, long Pixels, double WhiteShare);
