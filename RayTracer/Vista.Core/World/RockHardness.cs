namespace Vista.World;

/// <summary>
/// How hard the rock is (vista-plan §10 M1.11d, M1.12c): a seeded, procedural field of strata — beds of hard and soft rock stacked one
/// on another, tilted and folded — plus a lateral mottle, so a bed is not equally hard everywhere. Hardness 1 is the hardest rock, 0
/// the softest. The incision and the erosion read it (<see cref="ErosionSettings"/>: hard rock is less erodible and stands at a
/// steeper talus angle), so hard beds stand as cliffs and ledges and soft ones weather back; the materials (M1.12) read the same
/// field, so their bands line up with those cliffs.
/// <para>The field is a pure function of the seed and a position in the <b>rock's frame</b>: where the rock was when the
/// incision began. Uplift carries the rock up with the ground, so a bed rises with it; <see cref="StrataFrame"/> maps a
/// finished world's heights back to that frame. The beds are a table of hardness by stratigraphic height
/// (<see cref="TableSpacingMetres"/> apart, linearly interpolated), so the GPU looks them up exactly as this class does.</para>
/// <para>Since M1.12c the field is three-dimensional in earnest: a bed is the surface where <c>y + Fold(x, z)</c> is constant, and
/// <see cref="Fold"/> tilts it by a dip and a strike of its own in each region (<see cref="ErosionSettings.StrataRegionalDipDegrees"/>)
/// and folds it into long anticlines and synclines whose axes wander (<see cref="ErosionSettings.StrataFoldMetres"/>); the beds come
/// in formations of their own bed thickness and hardness (<see cref="ErosionSettings.StrataFormationMetres"/>); and over a seeded
/// share of the ground (<see cref="Massive"/>) the beds fade into massive rock, whose hardness (<see cref="MassiveTable"/>) changes
/// only slowly with height. With all four off the field is M1.11d's, bit for bit.</para>
/// </summary>
public sealed class RockHardness
{
    /// <summary>The spacing of the bed table, in metres of stratigraphic height.</summary>
    public const float TableSpacingMetres = 2f;

    /// <summary>Entries in the bed table: ±16 km of stratigraphic height around 0, beyond which the end beds continue.</summary>
    public const int TableEntries = 16384;

    /// <summary>The stratigraphic height of the table's first entry.</summary>
    public const float TableOrigin = -0.5f * TableEntries * TableSpacingMetres;

    /// <summary>
    /// Half the side of the square the massive mask's threshold is set over, in metres: the default world's extent, so the share of
    /// a world that is massive is the setting's, whatever the seed (vista-plan §10 M1.12c).
    /// </summary>
    public const double MaskExtentMetres = 16384.0;

    /// <summary>The mean hardness of massive rock (vista-plan §10 M1.12c).</summary>
    public const double MassiveMeanHardness = 0.6;

    /// <summary>How far massive rock's hardness strays from its mean with height (± this).</summary>
    public const double MassiveSpread = 0.3;

    /// <summary>How far massive rock's hardness strays from place to place, on top of the mottle (± this where the rock is fully massive).</summary>
    public const double MassiveMottle = 0.15;

    /// <summary>The coarser of massive rock's two octaves of lateral variation, in metres (the finer is 3.1 times finer).</summary>
    public const double MassiveMottleWavelengthMetres = 900.0;

    private const double MottleWavelengthMetres = 3000.0;
    private const double FoldWavelengthMetres = 9000.0;
    private const double FoldAmplitudeMetres = 120.0;

    // M1.12c: the fold's axes wander by this share of its wavelength, over twice its wavelength; its height swells and fades over
    // three wavelengths, between these shares of the setting.
    private const double FoldWander = 0.35;
    private const double FoldLowShare = 0.3;

    // The massive mask: two octaves at this wavelength, faded over ± this many units of its noise around the threshold.
    private const double MaskWavelengthMetres = 9000.0;
    private const double MaskEdge = 0.08;

    // Massive rock's hardness changes with stratigraphic height at this wavelength.
    private const double MassiveWavelengthMetres = 350.0;

    private readonly ulong _seed;
    private readonly double _dipX, _dipZ;
    private readonly double _mottle;
    private readonly double _regionalScale, _regionMetres;
    private readonly double _foldMetres, _foldWavelength, _foldX, _foldZ;
    private readonly double _maskThreshold;
    private readonly bool _massive;

    private RockHardness(ulong seed, float[] table, float[] tints, float[] massiveTable, double dipX, double dipZ, double mottle, ErosionSettings settings,
        double foldX, double foldZ, double maskThreshold)
    {
        _seed = seed;
        Table = table;
        Tints = tints;
        MassiveTable = massiveTable;
        _dipX = dipX;
        _dipZ = dipZ;
        _mottle = mottle;
        // The regional dip: gradient noise has the gradient √2 at its lattice points, so this scale gives each region's centre the
        // setting's dip (rounded to float like every other setting, so the platform's tangent cannot move a bit of the field).
        _regionMetres = settings.StrataRegionMetres;
        _regionalScale = (float)Math.Tan(settings.StrataRegionalDipDegrees * Math.PI / 180.0) * _regionMetres / 1.41421356237309504880;
        _foldMetres = settings.StrataFoldMetres;
        _foldWavelength = settings.StrataFoldWavelengthMetres;
        (_foldX, _foldZ) = (foldX, foldZ);
        _maskThreshold = maskThreshold;
        _massive = settings.StrataMassiveShare > 0f;
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
    /// The hardness of massive rock by stratigraphic height (vista-plan §10 M1.12c), entry for entry as <see cref="Table"/>: about
    /// <see cref="MassiveMeanHardness"/>, changing smoothly over hundreds of metres, with no beds. Zeros when no rock is massive.
    /// </summary>
    public float[] MassiveTable { get; }

    /// <summary>
    /// The field of <paramref name="seed"/> (the user's seed; the strata take their own stream of it) with the beds, dip and
    /// mottle of <paramref name="settings"/>.
    /// </summary>
    public static RockHardness For(ulong seed, ErosionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ulong strata = SeedDerivation.Derive(seed, SeedDerivation.TerrainStrata);

        var table = new float[TableEntries];
        var tints = new float[2 * TableEntries];
        if (settings.StrataFormationMetres > 0f)
            Formations(strata, settings, table, tints);
        else
            EvenBeds(strata, settings, table, tints);

        // The dip: a direction from the seed (a rational point on the circle, so no transcendental function moves its bits) and
        // a slope up to the settings' dip.
        ulong d = StableHash.Combine(strata, 0xD1Bul);
        (double c, double s) = Direction(d);
        double slope = (float)Math.Tan(settings.StrataDipDegrees * Math.PI / 180.0) * ((StableHash.Mix(d + 1ul) >> 11) * (1.0 / (1ul << 53)));

        // The fold's axes (M1.12c): across a direction of the seed's own.
        (double foldX, double foldZ) = Direction(StableHash.Combine(strata, 0xF07Dul));

        float[] massive = new float[TableEntries];
        double threshold = 0.0;
        if (settings.StrataMassiveShare > 0f)
        {
            ulong profile = StableHash.Combine(strata, 0x3A55ul);
            for (int k = 0; k < TableEntries; k++)
            {
                double sigma = TableOrigin + k * (double)TableSpacingMetres;
                massive[k] = (float)Math.Clamp(MassiveMeanHardness + MassiveSpread * GradientNoise.Sample(profile, sigma / MassiveWavelengthMetres, 0.37), 0.0, 1.0);
            }
            threshold = MaskThreshold(strata, settings.StrataMassiveShare);
        }
        return new RockHardness(strata, table, tints, massive, c * slope, s * slope, settings.HardnessMottle, settings, foldX, foldZ, threshold);
    }

    /// <summary>A unit vector from a hash: a rational point on the circle, either way round.</summary>
    private static (double C, double S) Direction(ulong h)
    {
        double t = (h >> 11) * (2.0 / (1ul << 53)) - 1.0;
        double c = (1.0 - t * t) / (1.0 + t * t), s = 2.0 * t / (1.0 + t * t);
        return (StableHash.Mix(h) & 1ul) != 0ul ? (-c, -s) : (c, s);
    }

    private static double Unit(ulong h) => (h >> 11) * (1.0 / (1ul << 53));

    /// <summary>
    /// M1.11d's beds: from stratigraphic height 0 upward and downward, each with its own thickness (a quarter to two and a half times
    /// the mean, thin beds more common) and hardness (pushed toward hard or soft by a smoothstep, so beds read as distinct rocks).
    /// </summary>
    private static void EvenBeds(ulong strata, ErosionSettings settings, float[] table, float[] tints)
    {
        double mean = settings.StrataThickness;
        (double Thickness, float Hardness) Bed(long index)
        {
            ulong h = StableHash.Lattice(strata, index, 0x0BED);
            double u = Unit(h);
            double v = Unit(StableHash.Mix(h));
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
    }

    /// <summary>
    /// M1.12c's beds: formations stacked from stratigraphic height 0 upward and downward, each 0.3 to 1.7 times the mean formation
    /// thickness, with its own mean bed thickness (0.15 to 2 times the settings' bed, thin-bedded formations more common; thicker
    /// beds stood as single talus planes), its own lean toward hard or soft rock (±0.35), its own contrast between its beds (from
    /// a third of M1.11d's to all of it) and its own tint; within a formation the beds vary about its mean by its contrast, and take
    /// most of their tint from it, so a formation reads as one kind of rock: a hard limestone, a soft shale, a banded flysch.
    /// </summary>
    private static void Formations(ulong strata, ErosionSettings settings, float[] table, float[] tints)
    {
        double formationMean = settings.StrataFormationMetres, bedMean = settings.StrataThickness;
        (double Thickness, double Bed, double Lean, double Contrast, double TintU, double TintV) Formation(long f)
        {
            ulong h = StableHash.Lattice(strata, f, 0xF0A7);
            double u = Unit(h), v = Unit(StableHash.Mix(h)), w = Unit(StableHash.Mix(h + 1ul)), c = Unit(StableHash.Mix(h + 4ul));
            double tu = Unit(StableHash.Mix(h + 2ul)) * 2.0 - 1.0, tv = Unit(StableHash.Mix(h + 3ul)) * 2.0 - 1.0;
            return (formationMean * (0.3 + 1.4 * u), bedMean * (0.15 + 1.85 * v * v * v), 0.7 * (w - 0.5), 0.6 + 0.4 * c, tu, tv);
        }
        (double Thickness, float Hardness, float TintU, float TintV) Bed(long f, long b,
            (double Thickness, double Bed, double Lean, double Contrast, double TintU, double TintV) formation)
        {
            ulong h = StableHash.Lattice(StableHash.Combine(strata, (ulong)f), b, 0x0BED);
            double u = Unit(h), v = Unit(StableHash.Mix(h));
            double tu = Unit(StableHash.Mix(h + 1ul)) * 2.0 - 1.0, tv = Unit(StableHash.Mix(h + 2ul)) * 2.0 - 1.0;
            double hardness = Math.Clamp(0.5 + formation.Lean + formation.Contrast * (v * v * (3.0 - 2.0 * v) - 0.5), 0.0, 1.0);
            return (formation.Bed * (0.2 + 1.8 * u * u) / 0.8, (float)hardness,
                (float)(0.65 * formation.TintU + 0.35 * tu), (float)(0.65 * formation.TintV + 0.35 * tv));
        }
        double origin = TableOrigin, spacing = TableSpacingMetres;
        int zero = (int)Math.Ceiling(-origin / spacing);

        // Upward from 0: formation 0 and its bed 0 start there.
        long f = 0, b = 0;
        var formation = Formation(f);
        double formationBottom = 0.0, bedBottom = 0.0;
        var bed = Bed(f, b, formation);
        for (int k = zero; k < TableEntries; k++)
        {
            double sigma = origin + k * spacing;
            while (sigma >= formationBottom + formation.Thickness)
            {
                formationBottom += formation.Thickness;
                formation = Formation(++f);
                bedBottom = formationBottom;
                b = 0;
                bed = Bed(f, b, formation);
            }
            while (sigma >= bedBottom + bed.Thickness)
            {
                bedBottom += bed.Thickness;
                bed = Bed(f, ++b, formation);
            }
            (table[k], tints[2 * k], tints[2 * k + 1]) = (bed.Hardness, bed.TintU, bed.TintV);
        }

        // Downward from 0: formation −1 and its bed −1 end there.
        f = -1;
        b = -1;
        formation = Formation(f);
        double formationTop = 0.0, bedTop = 0.0;
        bed = Bed(f, b, formation);
        for (int k = zero - 1; k >= 0; k--)
        {
            double sigma = origin + k * spacing;
            while (sigma < formationTop - formation.Thickness)
            {
                formationTop -= formation.Thickness;
                formation = Formation(--f);
                bedTop = formationTop;
                b = -1;
                bed = Bed(f, b, formation);
            }
            while (sigma < bedTop - bed.Thickness)
            {
                bedTop -= bed.Thickness;
                bed = Bed(f, --b, formation);
            }
            (table[k], tints[2 * k], tints[2 * k + 1]) = (bed.Hardness, bed.TintU, bed.TintV);
        }
    }

    /// <summary>
    /// The mask noise's value below which a share <paramref name="share"/> of the default world's square lies: its quantile over
    /// 129² points spread over ±<see cref="MaskExtentMetres"/>, so the massive share of every seed's world is close to the setting's.
    /// </summary>
    private static double MaskThreshold(ulong strata, double share)
    {
        const int Side = 129;
        var values = new double[Side * Side];
        for (int j = 0; j < Side; j++)
        {
            for (int i = 0; i < Side; i++)
            {
                double x = -MaskExtentMetres + i * (2.0 * MaskExtentMetres / (Side - 1));
                double z = -MaskExtentMetres + j * (2.0 * MaskExtentMetres / (Side - 1));
                values[j * Side + i] = MaskNoise(strata, x, z);
            }
        }
        Array.Sort(values);
        return values[(int)Math.Round(share * (values.Length - 1))];
    }

    private static double MaskNoise(ulong strata, double x, double z)
    {
        double f = 1.0 / MaskWavelengthMetres;
        ulong m = StableHash.Combine(strata, 0x3A5Cul);
        return 0.7 * GradientNoise.Sample(m, x * f, z * f) + 0.3 * GradientNoise.Sample(StableHash.Mix(m), x * f * 2.3, z * f * 2.3);
    }

    /// <summary>
    /// The stratigraphic height of the rock-frame position <c>(x, y, z)</c> less <c>y</c>: the beds' dip and fold there. A bed
    /// is the surface where <c>y + Fold(x, z)</c> is constant.
    /// </summary>
    public double Fold(double x, double z)
    {
        double f = 1.0 / FoldWavelengthMetres;
        double fold = _dipX * x + _dipZ * z + FoldAmplitudeMetres * GradientNoise.Sample(StableHash.Combine(_seed, 0xF01Dul), x * f, z * f);
        if (_regionalScale > 0.0)
        {
            // A dip and a strike per region (M1.12c): gradient noise is a plane of its lattice point's gradient around each point,
            // blended into the next, so the beds keep one dip over a region and turn between regions.
            double r = 1.0 / _regionMetres;
            fold += _regionalScale * GradientNoise.Sample(StableHash.Combine(_seed, 0x2E61ul), x * r, z * r);
        }
        if (_foldMetres > 0.0)
            fold += FoldAt(x, z);
        return fold;
    }

    /// <summary>
    /// The long folds (M1.12c): a periodic wave across the seed's fold direction, smooth at its crests and troughs (a quintic, no
    /// transcendental function), its axes wandering by <see cref="FoldWander"/> of a wavelength and its height swelling and fading
    /// between <see cref="FoldLowShare"/> and the whole of the setting's.
    /// </summary>
    private double FoldAt(double x, double z)
    {
        double w = 1.0 / _foldWavelength;
        double wander = FoldWander * GradientNoise.Sample(StableHash.Combine(_seed, 0xA7E5ul), x * w * 0.5, z * w * 0.5);
        double phase = (_foldX * x + _foldZ * z) * w + wander;
        double v = Math.Abs(2.0 * (phase - Math.Floor(phase)) - 1.0);
        double wave = 1.0 - 2.0 * (v * v * v * (v * (v * 6.0 - 15.0) + 10.0));
        double swell = 0.5 + 0.5 * GradientNoise.Sample(StableHash.Combine(_seed, 0x5E11ul), x * w / 3.0, z * w / 3.0);
        return _foldMetres * (FoldLowShare + (1.0 - FoldLowShare) * Math.Clamp(swell, 0.0, 1.0)) * wave;
    }

    /// <summary>
    /// The lateral variation of hardness at <c>(x, z)</c>, added to the bed's: two octaves of noise, ±<see cref="ErosionSettings.HardnessMottle"/>;
    /// where the rock is massive (M1.12c) two finer octaves more, ±<see cref="MassiveMottle"/> at full strength, so massive rock
    /// varies from place to place as intrusions and joints do and its slopes do not settle into one talus plane.
    /// </summary>
    public double Mottle(double x, double z)
    {
        double f = 1.0 / MottleWavelengthMetres;
        ulong m = StableHash.Combine(_seed, 0x3077ul);
        double mottle = _mottle * (0.67 * GradientNoise.Sample(m, x * f, z * f) + 0.33 * GradientNoise.Sample(StableHash.Mix(m), x * f * 4.0, z * f * 4.0));
        double massive = Massive(x, z);
        if (massive > 0.0)
        {
            double g = 1.0 / MassiveMottleWavelengthMetres;
            ulong q = StableHash.Combine(_seed, 0x3A57ul);
            mottle += massive * MassiveMottle * (0.6 * GradientNoise.Sample(q, x * g, z * g) + 0.4 * GradientNoise.Sample(StableHash.Mix(q), x * g * 3.1, z * g * 3.1));
        }
        return mottle;
    }

    /// <summary>
    /// How massive the rock is at <c>(x, z)</c> (vista-plan §10 M1.12c): 0 where it is layered, 1 where its beds have faded into
    /// massive rock (<see cref="MassiveTable"/>), a smooth step between; 0 everywhere without massive regions.
    /// </summary>
    public double Massive(double x, double z)
    {
        if (!_massive)
            return 0.0;
        double t = Math.Clamp((_maskThreshold + MaskEdge - MaskNoise(_seed, x, z)) / (2.0 * MaskEdge), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
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
    /// Hardness from a table coordinate <paramref name="t"/>, the place's <paramref name="mottle"/> and how
    /// <paramref name="massive"/> its rock is, in [0, 1] (as the GPU computes it): the bed, moved toward massive rock's by
    /// <paramref name="massive"/>, plus the mottle. With <paramref name="massive"/> 0 it is the bed's to the bit.
    /// </summary>
    public static float Of(float[] table, float[] massiveTable, float t, float mottle, float massive)
    {
        float bed = Lookup(table, t);
        if (massive != 0f)
            bed += (Lookup(massiveTable, t) - bed) * massive;
        return Math.Clamp(bed + mottle, 0f, 1f);
    }

    /// <summary>The hardness at rock-frame position <c>(x, y, z)</c>.</summary>
    public float At(double x, double y, double z) =>
        Of(Table, MassiveTable, (float)((y + Fold(x, z) - TableOrigin) / TableSpacingMetres), (float)Mottle(x, z), (float)Massive(x, z));

    /// <summary>The hardness at a stratigraphic height <paramref name="sigma"/> with <paramref name="mottle"/> and <paramref name="massive"/>, for the incision.</summary>
    public float AtSigma(double sigma, float mottle, float massive) => Of(Table, MassiveTable, (float)((sigma - TableOrigin) / TableSpacingMetres), mottle, massive);

    /// <summary>
    /// A 64-bit fingerprint of the bed table and the dip, for the tests; with M1.12c's strata, of their folds, mask and massive
    /// rock too (M1.11d's field keeps its fingerprint).
    /// </summary>
    public ulong ContentHash()
    {
        ulong hash = StableHash.Combine(BitConverter.DoubleToUInt64Bits(_dipX), BitConverter.DoubleToUInt64Bits(_dipZ));
        Span<byte> buffer = stackalloc byte[4];
        foreach (float v in Table)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer, BitConverter.SingleToInt32Bits(v));
            hash = StableHash.Fnv1a(hash, buffer);
        }
        if (_regionalScale > 0.0 || _foldMetres > 0.0 || _massive)
        {
            foreach (double v in new[] { _regionalScale, _regionMetres, _foldMetres, _foldWavelength, _foldX, _foldZ, _maskThreshold })
                hash = StableHash.Combine(hash, BitConverter.DoubleToUInt64Bits(v));
            foreach (float v in MassiveTable)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer, BitConverter.SingleToInt32Bits(v));
                hash = StableHash.Fnv1a(hash, buffer);
            }
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
/// coordinate, the mottle and how massive the rock is (M1.12c), so that the hardness of the rock at height <c>y</c> in cell
/// <c>c</c> is <c>RockHardness.Of(Table, MassiveTable, y·TableScale + Offset[c], Mottle[c], Massive[c])</c> — the GPU's
/// arithmetic, done the same way on both sides.
/// </summary>
public sealed class StrataGrid
{
    private float[]? _bands;

    /// <summary>Wraps the arrays without copying.</summary>
    public StrataGrid(float[] table, float[] massiveTable, float tableScale, float[] offset, float[] mottle, float[] massive, StrataFrame frame)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(massiveTable);
        ArgumentNullException.ThrowIfNull(offset);
        ArgumentNullException.ThrowIfNull(mottle);
        ArgumentNullException.ThrowIfNull(massive);
        ArgumentNullException.ThrowIfNull(frame);
        if (offset.Length != mottle.Length || offset.Length != massive.Length)
            throw new ArgumentException("The offsets, the mottle and the massive weights are not on one grid.", nameof(mottle));
        if (massiveTable.Length != table.Length)
            throw new ArgumentException("The massive rock's table is not the beds'.", nameof(massiveTable));
        Table = table;
        MassiveTable = massiveTable;
        TableScale = tableScale;
        Offset = offset;
        Mottle = mottle;
        Massive = massive;
        Frame = frame;
    }

    /// <summary>The bed table (<see cref="RockHardness.Table"/>).</summary>
    public float[] Table { get; }

    /// <summary>Massive rock's table (<see cref="RockHardness.MassiveTable"/>).</summary>
    public float[] MassiveTable { get; }

    /// <summary>
    /// The two tables as the GPU's erosion reads them, interleaved: per entry the bed's hardness, then massive rock's.
    /// </summary>
    public float[] Bands => _bands ??= Interleave(Table, MassiveTable);

    /// <summary>Table entries per metre of world height.</summary>
    public float TableScale { get; }

    /// <summary>Per cell, the table coordinate of world height 0.</summary>
    public float[] Offset { get; }

    /// <summary>Per cell, the mottle.</summary>
    public float[] Mottle { get; }

    /// <summary>Per cell, how massive the rock is (<see cref="RockHardness.Massive"/>).</summary>
    public float[] Massive { get; }

    /// <summary>The frame the offsets were worked out in.</summary>
    public StrataFrame Frame { get; }

    /// <summary>The hardness of the rock at world height <paramref name="y"/> in cell <paramref name="c"/>.</summary>
    public float At(int c, float y) => RockHardness.Of(Table, MassiveTable, y * TableScale + Offset[c], Mottle[c], Massive[c]);

    /// <summary>Two tables of one length interleaved, entry by entry.</summary>
    public static float[] Interleave(float[] table, float[] massiveTable)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(massiveTable);
        var bands = new float[2 * table.Length];
        for (int k = 0; k < table.Length; k++)
        {
            bands[2 * k] = table[k];
            bands[2 * k + 1] = massiveTable[k];
        }
        return bands;
    }

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
        var massive = new float[size * size];
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
                massive[j * size + i] = (float)hardness.Massive(x, z);
            }
        });
        return new StrataGrid(hardness.Table, hardness.MassiveTable, (float)(1.0 / (frame.Scale * spacing)), offset, mottle, massive, frame);
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
