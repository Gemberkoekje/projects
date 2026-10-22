namespace Vista.World;

/// <summary>
/// The detail below the world grid by material (vista-plan §10 M1.12): sharp crests on bare rock (<see cref="RockDetail"/>), angular
/// stones on scree (<see cref="ScreeDetail"/>), M1.9's smooth noise on soil and grass (<see cref="TerrainDetail"/>), blended by the
/// weights of the kinds the material rules give the world grid's samples (<see cref="MaterialField.DetailWeightsAt"/>), so the
/// detail follows the material the shader puts there. The kinds' own detail is a pure function of position; so are the weights,
/// bilinear between the samples.
///
/// <para><b>The bound.</b> With weights <c>w_k</c> summing to 1 and the kinds' details <c>D_k</c>, the blend at a segment's midpoint
/// strays from the mean of its ends by at most <c>max_k B_k(L) + Σ_k A_k·min(1, G_k·L/2)</c>: each kind's own bound
/// <c>B_k</c> where the weights hold still, and for each kind its amplitude <c>A_k</c> times how much its weight can change over
/// half the segment (<c>G_k</c>, the steepest change of its weight, <see cref="MaterialField.RockGradient"/>); never more than
/// twice the largest amplitude. The weights are averaged over 7×7 samples so <c>G_k</c> stays near a seventh of a weight per cell.</para>
///
/// <para><b>By region.</b> In a box (<see cref="MaterialField.RegionIn"/>: the kinds weighted anywhere in it, and how fast each one's
/// weight changes there) the sum and the maximum run over those kinds alone, with the box's own gradients; with one kind alone its
/// weight is 1 throughout and the bound is that kind's own: a meadow near the camera is refined as M1.9's smooth detail was, a rock
/// face as rock's. The mesh asks about a box around each vertex below the grid that holds its edge and every descendant's
/// (<see cref="TerrainHierarchy"/>), so a descendant's box lies in its own and its bound is never larger.</para>
/// </summary>
public sealed class MaterialDetail : ISurfaceDetail
{
    private readonly MaterialField _field;

    /// <summary>The detail of the world whose seed is <paramref name="worldSeed"/> and whose material field is <paramref name="field"/>.</summary>
    public MaterialDetail(ulong worldSeed, MaterialField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        _field = field;
        Smooth = new TerrainDetail(worldSeed, field.Cell);
        Rock = new RockDetail(worldSeed, field.Cell);
        Scree = new ScreeDetail(worldSeed, field.Cell);
    }

    /// <summary>Soil's and grass's detail.</summary>
    public TerrainDetail Smooth { get; }

    /// <summary>Bare rock's detail.</summary>
    public RockDetail Rock { get; }

    /// <summary>Scree's detail.</summary>
    public ScreeDetail Scree { get; }

    /// <inheritdoc />
    public bool IsNone => false;

    /// <inheritdoc />
    public double MaxAmplitude => Math.Max(Smooth.MaxAmplitude, Math.Max(Rock.MaxAmplitude, Scree.MaxAmplitude));

    /// <inheritdoc />
    public double Height(double x, double z)
    {
        (double rock, double scree) = _field.DetailWeightsAt(x, z);
        double smooth = 1.0 - rock - scree;
        double h = 0.0;
        if (smooth > 0.0)
            h += smooth * Smooth.Height(x, z);
        if (rock > 0.0)
            h += rock * Rock.Height(x, z);
        if (scree > 0.0)
            h += scree * Scree.Height(x, z);
        return h;
    }

    /// <inheritdoc />
    public double MidpointBound(double length) =>
        Bound(length, KindBounds(length), new DetailRegion(MaterialField.AllKinds, _field.RockGradient, _field.ScreeGradient, _field.SmoothGradient));

    /// <inheritdoc />
    public double[] KindBounds(double length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return [Rock.MidpointBound(length), Scree.MidpointBound(length), Smooth.MidpointBound(length)];
    }

    /// <inheritdoc />
    public double MidpointBoundIn(double length, double[] kindBounds, double x0, double z0, double x1, double z1) =>
        Bound(length, kindBounds, _field.RegionIn(x0, z0, x1, z1));

    /// <summary>The bound in a region: the largest of its kinds' own, and, where kinds mix, their amplitudes times how much their weights change.</summary>
    private double Bound(double length, double[] kindBounds, DetailRegion region)
    {
        int kinds = region.Kinds != 0 ? region.Kinds : MaterialField.AllKinds;
        double own = 0.0, changing = 0.0, amplitude = 0.0, half = 0.5 * length;
        bool mixed = (kinds & (kinds - 1)) != 0;
        foreach ((int bit, int index, double maxAmplitude, double gradient) in new (int, int, double, double)[]
        {
            (MaterialField.RockKind, 0, Rock.MaxAmplitude, region.RockGradient), (MaterialField.ScreeKind, 1, Scree.MaxAmplitude, region.ScreeGradient),
            (MaterialField.SmoothKind, 2, Smooth.MaxAmplitude, region.SmoothGradient),
        })
        {
            if ((kinds & bit) == 0)
                continue;
            own = Math.Max(own, kindBounds[index]);
            amplitude = Math.Max(amplitude, maxAmplitude);
            if (mixed)
                changing += maxAmplitude * Math.Min(1.0, gradient * half);
        }
        return Math.Min(own + changing, 2.0 * amplitude);
    }

    /// <summary>The kind whose weight is largest at world <c>(x, z)</c>.</summary>
    public DetailKind KindAt(double x, double z)
    {
        (double rock, double scree) = _field.DetailWeightsAt(x, z);
        double smooth = 1.0 - rock - scree;
        return rock >= scree && rock >= smooth ? DetailKind.Rock : scree >= smooth ? DetailKind.Scree : DetailKind.Smooth;
    }
}
