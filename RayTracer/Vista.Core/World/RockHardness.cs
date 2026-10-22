namespace Vista.World;

/// <summary>
/// How hard the rock is (vista-plan §10 M1.11d): a seeded, procedural field of strata — beds of hard and soft rock stacked one
/// on another, tilted by a regional dip and folded by a slow warp — plus a lateral mottle, so a bed is not equally hard
/// everywhere. Hardness 1 is the hardest rock, 0 the softest. The incision and the erosion read it (<see cref="ErosionSettings"/>:
/// hard rock is less erodible and stands at a steeper talus angle), so hard beds stand as cliffs and ledges and soft ones
/// weather back; the materials (M1.12) read the same field, so their bands line up with those cliffs.
/// <para>The field is a pure function of the seed and a position in the <b>rock's frame</b>: where the rock was when the
/// incision began. Uplift carries the rock up with the ground, so a bed rises with it; <see cref="StrataFrame"/> maps a
/// finished world's heights back to that frame. The beds are a table of hardness by stratigraphic height
/// (<see cref="TableSpacingMetres"/> apart, linearly interpolated), so the GPU looks them up exactly as this class does.</para>
/// </summary>
public sealed class RockHardness
{
    /// <summary>The spacing of the bed table, in metres of stratigraphic height.</summary>
    public const float TableSpacingMetres = 2f;

    /// <summary>Entries in the bed table: ±16 km of stratigraphic height around 0, beyond which the end beds continue.</summary>
    public const int TableEntries = 16384;

    /// <summary>The stratigraphic height of the table's first entry.</summary>
    public const float TableOrigin = -0.5f * TableEntries * TableSpacingMetres;

    private const double MottleWavelengthMetres = 3000.0;
    private const double FoldWavelengthMetres = 9000.0;
    private const double FoldAmplitudeMetres = 120.0;

    private readonly ulong _seed;
    private readonly double _dipX, _dipZ;
    private readonly double _mottle;

    private RockHardness(ulong seed, float[] table, float[] tints, double dipX, double dipZ, double mottle)
    {
        _seed = seed;
        Table = table;
        Tints = tints;
        _dipX = dipX;
        _dipZ = dipZ;
        _mottle = mottle;
    }

    /// <summary>The hardness of the beds by stratigraphic height: entry <c>k</c> at <see cref="TableOrigin"/> + k·<see cref="TableSpacingMetres"/>.</summary>
    public float[] Table { get; }

    /// <summary>
    /// Each bed's own tint by stratigraphic height, two values per <see cref="Table"/> entry, each from −1 to 1 (vista-plan §10
    /// M1.12): how much lighter or darker, and how much warmer or cooler, the bed's rock is than its hardness alone makes it, so
    /// beds of like hardness still read as different rocks. From the beds' own stream; it changes no hardness.
    /// </summary>
    public float[] Tints { get; }

    /// <summary>
    /// The field of <paramref name="seed"/> (the user's seed; the strata take their own stream of it) with the beds, dip and
    /// mottle of <paramref name="settings"/>.
    /// </summary>
    public static RockHardness For(ulong seed, ErosionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ulong strata = SeedDerivation.Derive(seed, SeedDerivation.TerrainStrata);

        // The beds: from stratigraphic height 0 upward and downward, each with its own thickness (a quarter to two and a half times
        // the mean, thin beds more common) and hardness (pushed toward hard or soft by a smoothstep, so beds read as distinct rocks).
        var table = new float[TableEntries];
        var tints = new float[2 * TableEntries];
        double mean = settings.StrataThickness;
        (double Thickness, float Hardness) Bed(long index)
        {
            ulong h = StableHash.Lattice(strata, index, 0x0BED);
            double u = (h >> 11) * (1.0 / (1ul << 53));
            double v = (StableHash.Mix(h) >> 11) * (1.0 / (1ul << 53));
            return (mean * (0.2 + 1.8 * u * u) / 0.8, (float)(v * v * (3.0 - 2.0 * v)));
        }
        void Tint(int k, long index)
        {
            ulong h = StableHash.Lattice(strata, index, 0x7147);
            tints[2 * k] = (float)((h >> 11) * (2.0 / (1ul << 53)) - 1.0);
            tints[2 * k + 1] = (float)((StableHash.Mix(h) >> 11) * (2.0 / (1ul << 53)) - 1.0);
        }
        double origin = TableOrigin, spacing = TableSpacingMetres;
        // Beds at or above 0, then below it.
        long bed = 0;
        double bottom = 0.0;
        (double thickness, float hardness) = Bed(bed);
        for (int k = (int)Math.Ceiling(-origin / spacing); k < TableEntries; k++)
        {
            double sigma = origin + k * spacing;
            while (sigma >= bottom + thickness)
            {
                bottom += thickness;
                (thickness, hardness) = Bed(++bed);
            }
            table[k] = hardness;
            Tint(k, bed);
        }
        bed = -1;
        (thickness, hardness) = Bed(bed);
        double top = 0.0;
        for (int k = (int)Math.Ceiling(-origin / spacing) - 1; k >= 0; k--)
        {
            double sigma = origin + k * spacing;
            while (sigma < top - thickness)
            {
                top -= thickness;
                (thickness, hardness) = Bed(--bed);
            }
            table[k] = hardness;
            Tint(k, bed);
        }

        // The dip: a direction from the seed (a rational point on the circle, so no transcendental function moves its bits) and
        // a slope up to the settings' dip.
        ulong d = StableHash.Combine(strata, 0xD1Bul);
        double t = (d >> 11) * (2.0 / (1ul << 53)) - 1.0;
        double c = (1.0 - t * t) / (1.0 + t * t), s = 2.0 * t / (1.0 + t * t);
        if ((StableHash.Mix(d) & 1ul) != 0ul)
            (c, s) = (-c, -s);
        double slope = (float)Math.Tan(settings.StrataDipDegrees * Math.PI / 180.0) * ((StableHash.Mix(d + 1ul) >> 11) * (1.0 / (1ul << 53)));
        return new RockHardness(strata, table, tints, c * slope, s * slope, settings.HardnessMottle);
    }

    /// <summary>
    /// The stratigraphic height of the rock-frame position <c>(x, y, z)</c> less <c>y</c>: the beds' dip and fold there. A bed
    /// is the surface where <c>y + Fold(x, z)</c> is constant.
    /// </summary>
    public double Fold(double x, double z)
    {
        double f = 1.0 / FoldWavelengthMetres;
        return _dipX * x + _dipZ * z + FoldAmplitudeMetres * GradientNoise.Sample(StableHash.Combine(_seed, 0xF01Dul), x * f, z * f);
    }

    /// <summary>The lateral variation of hardness at <c>(x, z)</c>, added to the bed's: two octaves of noise, ±<see cref="ErosionSettings.HardnessMottle"/>.</summary>
    public double Mottle(double x, double z)
    {
        double f = 1.0 / MottleWavelengthMetres;
        ulong m = StableHash.Combine(_seed, 0x3077ul);
        return _mottle * (0.67 * GradientNoise.Sample(m, x * f, z * f) + 0.33 * GradientNoise.Sample(StableHash.Mix(m), x * f * 4.0, z * f * 4.0));
    }

    /// <summary>
    /// The bed table at table coordinate <paramref name="t"/> (<c>(σ − <see cref="TableOrigin"/>) / <see cref="TableSpacingMetres"/></c>),
    /// interpolated, the end entries continuing past the table: the lookup the GPU makes, in the same float operations.
    /// </summary>
    public static float Lookup(float[] table, float t)
    {
        ArgumentNullException.ThrowIfNull(table);
        float clamped = Math.Clamp(t, 0f, table.Length - 1.001f);
        int k = (int)MathF.Floor(clamped);
        float f = clamped - k;
        return table[k] + (table[k + 1] - table[k]) * f;
    }

    /// <summary>
    /// Hardness from a table coordinate <paramref name="t"/> and the place's <paramref name="mottle"/>, in [0, 1] (as the GPU
    /// computes it).
    /// </summary>
    public static float Of(float[] table, float t, float mottle) => Math.Clamp(Lookup(table, t) + mottle, 0f, 1f);

    /// <summary>The hardness at rock-frame position <c>(x, y, z)</c>.</summary>
    public float At(double x, double y, double z) =>
        Of(Table, (float)((y + Fold(x, z) - TableOrigin) / TableSpacingMetres), (float)Mottle(x, z));

    /// <summary>The hardness at a stratigraphic height <paramref name="sigma"/> with <paramref name="mottle"/>, for the incision.</summary>
    public float AtSigma(double sigma, float mottle) => Of(Table, (float)((sigma - TableOrigin) / TableSpacingMetres), mottle);

    /// <summary>A 64-bit fingerprint of the bed table and the dip, for the tests.</summary>
    public ulong ContentHash()
    {
        ulong hash = StableHash.Combine(BitConverter.DoubleToUInt64Bits(_dipX), BitConverter.DoubleToUInt64Bits(_dipZ));
        Span<byte> buffer = stackalloc byte[4];
        foreach (float v in Table)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer, BitConverter.SingleToInt32Bits(v));
            hash = StableHash.Fnv1a(hash, buffer);
        }
        return hash;
    }
}

/// <summary>
/// How a finished world's heights map back to the rock's frame (<see cref="RockHardness"/>): the incision lifts the ground by
/// the uplift map for <see cref="UpliftSteps"/> steps and then maps its heights linearly back to the base terrain's range
/// (<c>y = Low + (h − Bottom)·Scale</c>), so the rock now at height <c>y</c> at <c>(x, z)</c> began at
/// <c>(y − Low) / Scale + Bottom − UpliftSteps·U(x, z)</c>. Without an incision the frames are the same.
/// </summary>
/// <param name="Low">The base terrain's lowest height on the incision grid: where the range was mapped back to.</param>
/// <param name="Bottom">The incised ground's lowest height before it was mapped back.</param>
/// <param name="Scale">The factor the incised range was stretched by.</param>
/// <param name="UpliftSteps">The incision steps, each lifting the ground by the uplift map.</param>
public sealed record StrataFrame(double Low, double Bottom, double Scale, int UpliftSteps)
{
    /// <summary>The frame of a world that was not incised.</summary>
    public static StrataFrame Identity { get; } = new(0.0, 0.0, 1.0, 0);

    /// <summary>The rock-frame height of the rock at height <paramref name="y"/> where the uplift per step is <paramref name="uplift"/>.</summary>
    public double RockHeight(double y, double uplift) => (y - Low) / Scale + Bottom - UpliftSteps * uplift;
}

/// <summary>
/// The rock's hardness laid out on a grid for the erosion (vista-plan §10 M1.11d): per cell, the offset of the bed-table
/// coordinate and the mottle, so that the hardness of the rock at height <c>y</c> in cell <c>c</c> is
/// <c>RockHardness.Of(Table, y·TableScale + Offset[c], Mottle[c])</c> — the GPU's arithmetic, done the same way on both sides.
/// </summary>
public sealed class StrataGrid
{
    /// <summary>Wraps the arrays without copying.</summary>
    public StrataGrid(float[] table, float tableScale, float[] offset, float[] mottle, StrataFrame frame)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(offset);
        ArgumentNullException.ThrowIfNull(mottle);
        ArgumentNullException.ThrowIfNull(frame);
        if (offset.Length != mottle.Length)
            throw new ArgumentException("The offsets and the mottle are not on one grid.", nameof(mottle));
        Table = table;
        TableScale = tableScale;
        Offset = offset;
        Mottle = mottle;
        Frame = frame;
    }

    /// <summary>The bed table (<see cref="RockHardness.Table"/>).</summary>
    public float[] Table { get; }

    /// <summary>Table entries per metre of world height.</summary>
    public float TableScale { get; }

    /// <summary>Per cell, the table coordinate of world height 0.</summary>
    public float[] Offset { get; }

    /// <summary>Per cell, the mottle.</summary>
    public float[] Mottle { get; }

    /// <summary>The frame the offsets were worked out in.</summary>
    public StrataFrame Frame { get; }

    /// <summary>The hardness of the rock at world height <paramref name="y"/> in cell <paramref name="c"/>.</summary>
    public float At(int c, float y) => RockHardness.Of(Table, y * TableScale + Offset[c], Mottle[c]);

    /// <summary>
    /// The field of <paramref name="hardness"/> on a grid of <paramref name="size"/>² cells <paramref name="cellSize"/> metres
    /// apart (centred on the origin, as <see cref="HeightField"/>), in <paramref name="frame"/>, with <paramref name="uplift"/>
    /// the uplift per step at a world position.
    /// </summary>
    public static StrataGrid For(RockHardness hardness, int size, double cellSize, StrataFrame frame, Func<double, double, double> uplift)
    {
        ArgumentNullException.ThrowIfNull(hardness);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(uplift);
        var offset = new float[size * size];
        var mottle = new float[size * size];
        double origin = -0.5 * (size - 1) * cellSize;
        double spacing = RockHardness.TableSpacingMetres;
        Parallel.For(0, size, j =>
        {
            double z = origin + j * cellSize;
            for (int i = 0; i < size; i++)
            {
                double x = origin + i * cellSize;
                // σ = (y − Low) / Scale + Bottom − Steps·U + Fold; the table coordinate is (σ − origin) / spacing.
                double sigma0 = -frame.Low / frame.Scale + frame.Bottom - frame.UpliftSteps * uplift(x, z) + hardness.Fold(x, z);
                offset[j * size + i] = (float)((sigma0 - RockHardness.TableOrigin) / spacing);
                mottle[j * size + i] = (float)hardness.Mottle(x, z);
            }
        });
        return new StrataGrid(hardness.Table, (float)(1.0 / (frame.Scale * spacing)), offset, mottle, frame);
    }

    /// <summary>Per cell, the hardness of the rock at <paramref name="heights"/> (row-major on this grid).</summary>
    public float[] HardnessAt(ReadOnlySpan<float> heights)
    {
        if (heights.Length != Offset.Length)
            throw new ArgumentException($"Expected {Offset.Length} heights, got {heights.Length}.", nameof(heights));
        var result = new float[heights.Length];
        float[] copy = heights.ToArray();
        Parallel.For(0, result.Length / 4096 + 1, b =>
        {
            for (int c = b * 4096; c < Math.Min(result.Length, (b + 1) * 4096); c++)
                result[c] = At(c, copy[c]);
        });
        return result;
    }
}
