namespace Vista.World;

/// <summary>
/// Where a world stands (vista-plan §10 M1.12c): the height of its lowest valleys above its own height 0, taken as a low percentile
/// of its ground (<see cref="DefaultPercentile"/>, so a single pit or the base level at the grid's edge cannot move the world), and
/// its lowest point. The altitude slider puts this height, not height 0, at the place's valley floor, so the tree line, the grass
/// line, the snowline and the freezing level read against true altitudes.
/// </summary>
/// <param name="Percentile">The percentile of the ground's heights taken, 0 to 100.</param>
/// <param name="HeightMetres">That percentile's height above the world's height 0, m: its lowest valleys.</param>
/// <param name="LowestMetres">The lowest point of the ground, m.</param>
public sealed record WorldFooting(double Percentile, double HeightMetres, double LowestMetres)
{
    /// <summary>The percentile of the ground the world's lowest valleys are taken at.</summary>
    public const double DefaultPercentile = 5.0;

    /// <summary>The percentile of the ground the world's crest is taken at (<see cref="CrestMetres"/>).</summary>
    public const double CrestPercentile = 95.0;

    /// <summary>
    /// The height of the world's crest above its height 0, m: the <see cref="CrestPercentile"/>th percentile of its ground, where its
    /// high ridges stand (vista-plan §10 M1.13a: a valley's cold pool fills its valleys, not the peaks above them). <see cref="Of"/>
    /// measures it; a footing made without it leaves it 0, and the valley haze then knows no relief.
    /// </summary>
    public double CrestMetres { get; init; }

    /// <summary>How far the world's crest stands above its lowest valleys, m.</summary>
    public double ReliefMetres => Math.Max(CrestMetres - HeightMetres, 0.0);

    /// <summary>
    /// The footing of the ground <paramref name="surface"/> at <paramref name="percentile"/>: the height of the sample of rank
    /// round(p/100·(n − 1)) from the lowest, exactly (a two-pass histogram select, no sort of the whole grid), so a pure function of
    /// the heights.
    /// </summary>
    public static WorldFooting Of(HeightField surface, double percentile = DefaultPercentile)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (!(percentile >= 0.0 && percentile <= 100.0))
            throw new ArgumentOutOfRangeException(nameof(percentile), percentile, "A percentile is from 0 to 100.");
        ReadOnlySpan<float> heights = surface.Heights;
        float low = surface.MinHeight, high = surface.MaxHeight;
        if (!(high > low))
            return new WorldFooting(percentile, low, low) { CrestMetres = low };
        return new WorldFooting(percentile, Select(heights, low, high, percentile), low) { CrestMetres = Select(heights, low, high, CrestPercentile) };
    }

    /// <summary>
    /// The height of rank round(p/100·(n − 1)) from the lowest of <paramref name="heights"/>, which lie in [<paramref name="low"/>,
    /// <paramref name="high"/>]: how many heights fall in each of 65536 bins, then the bin that holds the rank, and the heights in it, sorted.
    /// </summary>
    private static float Select(ReadOnlySpan<float> heights, float low, float high, double percentile)
    {
        long rank = (long)Math.Round(percentile / 100.0 * (heights.Length - 1));
        const int Bins = 65536;
        double scale = (Bins - 1) / ((double)high - low);
        int Bin(float h) => (int)Math.Clamp((h - (double)low) * scale, 0.0, Bins - 1);
        var counts = new long[Bins];
        foreach (float h in heights)
            counts[Bin(h)]++;
        long below = 0;
        int bin = 0;
        while (below + counts[bin] <= rank)
            below += counts[bin++];
        var inBin = new List<float>((int)counts[bin]);
        foreach (float h in heights)
        {
            if (Bin(h) == bin)
                inBin.Add(h);
        }
        inBin.Sort();
        return inBin[(int)(rank - below)];
    }
}
