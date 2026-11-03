namespace Vista.World;

/// <summary>
/// The detail below the world grid by material (vista-plan §10 M1.12, M1.12b): sharp crests on bare rock (<see cref="RockDetail"/>),
/// angular stones on scree (<see cref="ScreeDetail"/>), M1.9's smooth noise on soil and grass (<see cref="TerrainDetail"/>) and the
/// crowns of a forest's canopy (<see cref="CanopyDetail"/>), blended by the weights of the kinds the material rules give the world
/// grid's samples (<see cref="MaterialField.DetailWeightsAt"/>), so the detail follows the material the shader puts there. The kinds'
/// own detail is a pure function of position; so are the weights, bilinear between the samples.
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
///
/// <para><b>The canopy only where the forest grows.</b> The crowns stand three times as high as the other kinds' detail; bounded
/// everywhere, they would refine every view's open ground as forest. So the world's own bound leaves them out unless a scene's forest
/// grows (<see cref="MaterialField.Forest"/>), and the hierarchy's grid-level vertices take the bound without them
/// (<see cref="OpenBound"/>) wherever no forest lies in their box (<see cref="CanopyIn"/>): with <c>--forest off</c> the mesh is
/// M1.12's.</para>
///
/// <para><b>The rock's structure</b> (vista-plan §10 M1a.4, <see cref="RockStructure"/>): bare rock also takes the gullies' coarse
/// octaves and the ledges, by its weight, over the surface's own height (<see cref="HeightOver"/>), where the world grid's normals
/// give the fall line (<see cref="AttachNormals"/>). The shader shades all of it, so the mesh's normals leave it out (its height is
/// all <c>Shaded</c>), and the mesh holds it to <see cref="StructureTolerance"/> pixels. Its bound is in none of the kinds' own: it is
/// added by region (<see cref="StructureBoundIn"/>), from pyramids of the cells' shares (the gullies' by rock and its steepness, the
/// ledges' by layered rock and how fast its beds change along the ground), so meadows, forest and the far field refine as before.</para>
/// </summary>
public sealed class MaterialDetail : ISurfaceDetail
{
    /// <summary>The pixels the mesh holds the rock's structure to: its shading is exact, its outline and shadows this close.</summary>
    public const double StructureTolerance = 3.0;

    /// <summary>
    /// How fast the beds can change along the ground for the ledges' bound, m of stratigraphic height per metre: a steep face, or beds
    /// tilted steeply by the uplift; faster is held at this.
    /// </summary>
    public const double LedgeRateMax = 8.0;

    private readonly MaterialField _field;

    /// <summary>The kinds the world can hold anywhere: the canopy only where a scene's forest grows.</summary>
    private readonly int _present;

    private readonly uint _seed;
    private readonly float _depth;
    private readonly float _coarsest;
    private readonly int _meshOctaves;
    private readonly double _gullyAmplitude;
    private readonly double _gullySlope;
    private readonly double _ledgeAmplitude;
    private readonly double _ledgeFirst;
    private readonly double _ledgeSecond;
    private NormalMap? _normals;
    private byte[][] _gullyShare = [];
    private byte[][] _ledgeShare = [];
    private byte[][] _ledgeRate = [];

    /// <summary>
    /// The detail of the world whose seed is <paramref name="worldSeed"/> and whose material field is <paramref name="field"/>, with
    /// the rock's structure under <paramref name="rules"/> (the default rules when null) unless <paramref name="structure"/> is false.
    /// </summary>
    public MaterialDetail(ulong worldSeed, MaterialField field, bool structure = true, MaterialRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        _field = field;
        Smooth = new TerrainDetail(worldSeed, field.Cell);
        Rock = new RockDetail(worldSeed, field.Cell);
        Scree = new ScreeDetail(worldSeed, field.Cell);
        Canopy = new CanopyDetail(worldSeed);
        _present = field.Forest.Grows ? MaterialField.AllKinds : MaterialField.AllKinds & ~MaterialField.CanopyKind;
        rules ??= MaterialRules.Default;
        Structure = structure && field.Eroded;
        _seed = field.NoiseSeed;
        _depth = rules.StructureDepth;
        _coarsest = rules.StructureCoarsestMetres;
        _meshOctaves = int.Parse(Environment.GetEnvironmentVariable("VISTA_MESH_OCTAVES") ?? rules.StructureMeshOctaves.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        double wavelength = _coarsest;
        for (int k = 0; k < _meshOctaves; k++, wavelength *= 0.5)
            _gullyAmplitude += _depth * wavelength * RockStructure.SecMax * RockStructure.ProfileMax;
        (_ledgeAmplitude, _ledgeFirst, _ledgeSecond) = LedgeShape(field);
        // The gullies' steepest: each octave's amplitude times its angular frequency times the profile's steepest (4/π, rounded).
        wavelength = _coarsest;
        for (int k = 0; k < _meshOctaves; k++, wavelength *= 0.5)
            _gullySlope += _depth * wavelength * RockStructure.SecMax * (2.0 * Math.PI / wavelength) * (4.0 / Math.PI) * 1.1;
    }

    /// <summary>Whether bare rock takes the structure (vista-plan §10 M1a.4): asked for, on an eroded world's field.</summary>
    public bool Structure { get; }

    /// <summary>The most the structure raises or lowers rock, m: the gullies' mesh octaves at their steepest, and the ledges.</summary>
    public double StructureAmplitude => Structure ? _gullyAmplitude + _ledgeAmplitude : 0.0;

    /// <summary>Soil's and grass's detail.</summary>
    public TerrainDetail Smooth { get; }

    /// <summary>Bare rock's detail.</summary>
    public RockDetail Rock { get; }

    /// <summary>Scree's detail.</summary>
    public ScreeDetail Scree { get; }

    /// <summary>The forest's detail, its crowns.</summary>
    public CanopyDetail Canopy { get; }

    /// <inheritdoc />
    public bool IsNone => false;

    /// <inheritdoc />
    public double MaxAmplitude
    {
        get
        {
            double amplitude = Math.Max(Smooth.MaxAmplitude, Math.Max(Rock.MaxAmplitude + StructureAmplitude, Scree.MaxAmplitude));
            return Regional ? Math.Max(amplitude, Canopy.MaxAmplitude) : amplitude;
        }
    }

    /// <inheritdoc />
    public bool Regional => (_present & MaterialField.CanopyKind) != 0;

    /// <inheritdoc />
    public double Height(double x, double z)
    {
        (double rock, double scree, double forest) = _field.DetailWeightsAt(x, z);
        return Blend(x, z, rock, scree, forest);
    }

    /// <summary>The kinds' details at world <c>(x, z)</c> blended by their weights there.</summary>
    private double Blend(double x, double z, double rock, double scree, double forest)
    {
        double smooth = 1.0 - rock - scree - forest;
        double h = 0.0;
        if (smooth > 0.0)
            h += smooth * Smooth.Height(x, z);
        if (rock > 0.0)
            h += rock * Rock.Height(x, z);
        if (scree > 0.0)
            h += scree * Scree.Height(x, z);
        if (forest > 0.0)
            h += forest * Canopy.Height(x, z);
        return h;
    }

    /// <inheritdoc />
    public double MidpointBound(double length) => Bound(length, KindBounds(length), Global(_present));

    /// <inheritdoc />
    public double OpenBound(double length) =>
        Bound(length, KindBounds(length), Global(_present & ~MaterialField.CanopyKind) with { SmoothGradient = _field.OpenSmoothGradient });

    /// <inheritdoc />
    public double[] KindBounds(double length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return [Rock.MidpointBound(length), Scree.MidpointBound(length), Smooth.MidpointBound(length), Canopy.MidpointBound(length)];
    }

    /// <inheritdoc />
    public double MidpointBoundIn(double length, double[] kindBounds, double x0, double z0, double x1, double z1) =>
        Bound(length, kindBounds, _field.RegionIn(x0, z0, x1, z1)) + StructureBoundIn(length, x0, z0, x1, z1);

    /// <inheritdoc />
    public (double Height, double Shaded) HeightOver(double x, double z, double baseHeight)
    {
        (double rock, double scree, double forest) = _field.DetailWeightsAt(x, z);
        double h = Blend(x, z, rock, scree, forest);
        if (!Structure || rock <= 0.0 || _normals is null)
            return (h, 0.0);
        double structure = StructureAt(x, z, baseHeight, rock);
        return (h + structure, structure);
    }

    /// <inheritdoc />
    public void AttachNormals(NormalMap world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _normals = world;
        if (Structure)
            (_gullyShare, _ledgeShare, _ledgeRate) = Shares(world);
        if (Environment.GetEnvironmentVariable("VISTA_DEBUG_STRUCTURE") == "1")
        {
            Console.Error.WriteLine(FormattableString.Invariant($"structure: gullies {_gullyAmplitude:F2} m slope {_gullySlope:F2}, ledges {_ledgeAmplitude:F2} m first {_ledgeFirst:F3} second {_ledgeSecond:F3}, table scale {_field.TableScale:F3}"));
            foreach (double l in new[] { 0.0625, 0.25, 1.0, 4.0, 16.0, 64.0 })
                Console.Error.WriteLine(FormattableString.Invariant($"  L {l}: gullies {StructureBound(l, 1, 0, 0):F4} ledges r=0.25 {StructureBound(l, 0, 1, 0.25):F4} r=1 {StructureBound(l, 0, 1, 1):F4}"));
            var hist = new long[9];
            foreach (byte b in _ledgeRate[0]) hist[Math.Min(b / 32, 8)]++;
            Console.Error.WriteLine("  rate histogram (by 1/8): " + string.Join(" ", hist));
        }
    }

    /// <inheritdoc />
    public double StructureBound(double length) => Structure ? StructureBound(length, 1.0, 1.0, 1.0) : 0.0;

    /// <inheritdoc />
    public double StructureBoundIn(double length, double x0, double z0, double x1, double z1)
    {
        if (!Structure || _gullyShare.Length == 0)
            return 0.0;
        if (!Cells(x0, z0, x1, z1, out int i0, out int j0, out int i1, out int j1))
            return 0.0;
        double gullies = MaxIn(_gullyShare, i0, j0, i1, j1), ledges = MaxIn(_ledgeShare, i0, j0, i1, j1);
        string parts = Environment.GetEnvironmentVariable("VISTA_STRUCTURE_PARTS") ?? "both";
        if (parts == "gullies") ledges = 0.0;
        if (parts == "ledges") gullies = 0.0;
        if (gullies <= 0.0 && ledges <= 0.0)
            return 0.0;
        return StructureBound(length, gullies, ledges, MaxIn(_ledgeRate, i0, j0, i1, j1));
    }

    /// <summary>
    /// The structure's bound for a segment <paramref name="length"/> metres long where the gullies take <paramref name="gullies"/> of
    /// their steepest depth, the ledges stand on <paramref name="ledges"/> of rock (its weight where layered) and the beds change along
    /// the ground at <paramref name="rate"/> of <see cref="LedgeRateMax"/>, over <see cref="StructureTolerance"/>: the gullies' octaves'
    /// envelopes, the ledges' curvature or twice their height, and where the rock's weight changes along the segment (by δ either side
    /// of its middle) δ times half the structure's change between the ends, at most its height or its steepest slope over the segment.
    /// </summary>
    private double StructureBound(double length, double gullies, double ledges, double rate)
    {
        double sum = 0.0, wavelength = _coarsest;
        double gTol = double.Parse(Environment.GetEnvironmentVariable("VISTA_GTOL") ?? "1", System.Globalization.CultureInfo.InvariantCulture);
        double lTol = double.Parse(Environment.GetEnvironmentVariable("VISTA_LTOL") ?? "1", System.Globalization.CultureInfo.InvariantCulture);
        for (int k = 0; k < _meshOctaves; k++, wavelength *= 0.5)
            sum += gullies * _depth * wavelength * RockStructure.SecMax * RockStructure.ProfileMax * RockStructure.Envelope.At(length / wavelength) / gTol;
        double r = rate * LedgeRateMax;
        sum += ledges * Math.Min(2.0 * _ledgeAmplitude, _ledgeSecond * r * r * length * length / 8.0) / lTol;
        double amplitude = gullies * _gullyAmplitude + ledges * _ledgeAmplitude;
        double slope = gullies * _gullySlope + ledges * _ledgeFirst * r;
        sum += Math.Min(1.0, _field.RockGradient * 0.5 * length) * Math.Min(amplitude, 0.5 * slope * length);
        return sum / StructureTolerance;
    }

    /// <summary>
    /// The structure's height at world <c>(x, z)</c> over a surface <paramref name="baseHeight"/> high there, on rock of weight
    /// <paramref name="rock"/>, m: the gullies' mesh octaves, then the ledges of the beds at the surface they make, solved for the
    /// height <c>h'</c> where <c>h' = h + rock·(1 − massive)·G(t(h'))</c> (Newton's method), so the beds the surface cuts are the
    /// beds its ledges stand on. None away from the material map.
    /// </summary>
    internal double StructureAt(double x, double z, double baseHeight, double rock)
    {
        float fx = (float)x, fz = (float)z;
        if (_field.StrataAt(fx, fz) is not (float offset, float massive))
            return 0.0;
        double gully = 0.0;
        if (_meshOctaves > 0 && _normals is not null)
        {
            (float nx, float nz) = _normals.Sample(x, z);
            float inv = 1f / MathF.Max(MathF.Sqrt(MathF.Max(1f - nx * nx - nz * nz, 0f)), 0.05f);
            gully = RockStructure.Gullies(_seed, fx, fz, -nx * inv, -nz * inv, _depth, _coarsest, _meshOctaves).Height;
        }
        string parts = Environment.GetEnvironmentVariable("VISTA_STRUCTURE_PARTS") ?? "both";
        if (parts == "ledges") gully = 0.0;
        double h = baseHeight + rock * gully;
        double k = parts == "gullies" ? 0.0 : rock * (1.0 - massive);
        if (k <= 0.0)
            return rock * gully;
        double surface = h;
        for (int i = 0; i < 8; i++)
        {
            (float g, float slope) = _field.LedgeAt((float)(surface * _field.TableScale + offset));
            double step = (surface - h - k * g) / Math.Max(1.0 - k * slope * _field.TableScale, 0.2);
            surface -= step;
            if (Math.Abs(step) < 1e-4)
                break;
        }
        return surface - baseHeight;
    }

    /// <summary>
    /// The ledges' largest height, m, and their table's largest first and second derivatives by stratigraphic height (per metre of it):
    /// times how fast the stratigraphic height changes along the ground, and its square, they bound the ledges' slope and curvature.
    /// </summary>
    private static (double Amplitude, double First, double Second) LedgeShape(MaterialField field)
    {
        ReadOnlySpan<float> table = field.Ledges;
        int n = field.TableEntries;
        double amplitude = 0.0, first = 0.0, second = 0.0;
        for (int e = 1; e < n - 2; e++)
        {
            amplitude = Math.Max(amplitude, Math.Abs(table[e]));
            double p0 = table[e - 1], p1 = table[e], p2 = table[e + 1], p3 = table[e + 2];
            double b = 2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3, c = -p0 + 3.0 * p1 - 3.0 * p2 + p3;
            first = Math.Max(first, Math.Max(0.5 * Math.Abs(p2 - p0), 0.5 * Math.Abs(p3 - p1)) * 1.25);
            second = Math.Max(second, Math.Max(Math.Abs(b), Math.Abs(b + 3.0 * c)));
        }
        double scale = field.TableScale;
        return (amplitude, first * scale, second * scale * scale);
    }

    /// <summary>
    /// The structure's shares per cell of the material map, each the largest at the cell's corners, as pyramids (<see cref="Pyramid"/>):
    /// the gullies' (rock's weight times the slope's secant over <see cref="RockStructure.SecMax"/>, faded in as the gullies are), the
    /// ledges' (rock's weight where it is layered) and how fast the beds change along the ground there, over <see cref="LedgeRateMax"/>
    /// (the slope plus the beds' own tilt, where there are ledges).
    /// </summary>
    private (byte[][] Gullies, byte[][] Ledges, byte[][] Rate) Shares(NormalMap world)
    {
        int n = _field.Size;
        var gullies = new float[n * n];
        var ledges = new float[n * n];
        var rate = new float[n * n];
        float cell = (float)_field.Cell;
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
            {
                double x = _field.Origin + i * _field.Cell, z = _field.Origin + j * _field.Cell;
                double rock = _field.DetailWeightsAt(x, z).Rock;
                if (rock <= 0.0)
                    continue;
                (float nx, float nz) = world.Sample(x, z);
                double ny = Math.Max(Math.Sqrt(Math.Max(1.0 - nx * nx - nz * nz, 0.0)), 1e-3);
                double gx = -nx / ny, gz = -nz / ny, steep = Math.Sqrt(gx * gx + gz * gz);
                double fade = Math.Clamp((steep - RockStructure.SlopeFrom) / (RockStructure.SlopeTo - RockStructure.SlopeFrom), 0.0, 1.0);
                gullies[j * n + i] = (float)(rock * Math.Min(1.0 / ny / RockStructure.SecMax, 1.0) * Math.Min(1.0, 2.0 * fade));
                if (_field.StrataAt((float)x, (float)z) is not (float offset, float massive) || massive >= 1f)
                    continue;
                ledges[j * n + i] = (float)(rock * (1.0 - massive));
                // The beds' own tilt from the offsets a cell either side (the samples' own, so it is the bilinear field's steepest).
                float ox = (_field.StrataAt((float)x + cell, (float)z)?.Offset ?? offset) - (_field.StrataAt((float)x - cell, (float)z)?.Offset ?? offset);
                float oz = (_field.StrataAt((float)x, (float)z + cell)?.Offset ?? offset) - (_field.StrataAt((float)x, (float)z - cell)?.Offset ?? offset);
                double sx = gx + ox / (2.0 * cell * _field.TableScale), sz = gz + oz / (2.0 * cell * _field.TableScale);
                rate[j * n + i] = (float)Math.Min(Math.Sqrt(sx * sx + sz * sz) * 1.25 / LedgeRateMax, 1.0);
            }
        });
        return (Pyramid(gullies), Pyramid(ledges), Pyramid(rate));
    }

    /// <summary>
    /// A pyramid of the material map's cells from per-sample <paramref name="samples"/> in [0, 1]: level 0 a byte per cell, the largest
    /// at its corners rounded up; each level above half as wide, holding the largest of the four below.
    /// </summary>
    private byte[][] Pyramid(float[] samples)
    {
        int n = _field.Size, cells = n - 1;
        var levels = new List<byte[]>();
        var level0 = new byte[cells * cells];
        Parallel.For(0, cells, j =>
        {
            for (int i = 0; i < cells; i++)
            {
                float v = Math.Max(Math.Max(samples[j * n + i], samples[j * n + i + 1]), Math.Max(samples[(j + 1) * n + i], samples[(j + 1) * n + i + 1]));
                level0[j * cells + i] = (byte)Math.Min(255.0, Math.Ceiling(v * 255.0));
            }
        });
        levels.Add(level0);
        int side = cells;
        while (side > 1)
        {
            byte[] below = levels[^1];
            int next = (side + 1) / 2;
            var level = new byte[next * next];
            int width = side;
            Parallel.For(0, next, j =>
            {
                for (int i = 0; i < next; i++)
                {
                    int i0 = 2 * i, j0 = 2 * j, i1 = Math.Min(i0 + 1, width - 1), j1 = Math.Min(j0 + 1, width - 1);
                    level[j * next + i] = Math.Max(Math.Max(below[j0 * width + i0], below[j0 * width + i1]), Math.Max(below[j1 * width + i0], below[j1 * width + i1]));
                }
            });
            levels.Add(level);
            side = next;
        }
        return [.. levels];
    }

    /// <summary>The cells of the material map the box from world <c>(x0, z0)</c> to <c>(x1, z1)</c> reaches into; false off the map.</summary>
    private bool Cells(double x0, double z0, double x1, double z1, out int i0, out int j0, out int i1, out int j1)
    {
        int cells = _field.Size - 1;
        double extent = cells * _field.Cell;
        double ox = _field.Origin;
        (i0, j0, i1, j1) = (0, 0, 0, 0);
        if (x1 < ox || z1 < ox || x0 > ox + extent || z0 > ox + extent)
            return false;
        i0 = Math.Clamp((int)Math.Floor((x0 - ox) / _field.Cell), 0, cells - 1);
        i1 = Math.Clamp((int)Math.Floor((x1 - ox) / _field.Cell), 0, cells - 1);
        j0 = Math.Clamp((int)Math.Floor((z0 - ox) / _field.Cell), 0, cells - 1);
        j1 = Math.Clamp((int)Math.Floor((z1 - ox) / _field.Cell), 0, cells - 1);
        return true;
    }

    /// <summary>The largest value of <paramref name="pyramid"/> over cells <c>i0..i1</c> × <c>j0..j1</c>, from the level where they span at most 2×2.</summary>
    private static double MaxIn(byte[][] pyramid, int i0, int j0, int i1, int j1)
    {
        int level = 0, side = (int)Math.Sqrt(pyramid[0].Length);
        while ((i1 >> level) - (i0 >> level) > 1 || (j1 >> level) - (j0 >> level) > 1)
        {
            level++;
            side = (side + 1) / 2;
        }
        if (level >= pyramid.Length)
            return pyramid[^1][0] / 255.0;
        byte[] texels = pyramid[level];
        int max = 0;
        for (int j = j0 >> level; j <= j1 >> level; j++)
        {
            for (int i = i0 >> level; i <= i1 >> level; i++)
                max = Math.Max(max, texels[j * side + i]);
        }
        return max / 255.0;
    }

    /// <inheritdoc />
    public bool CanopyIn(double x0, double z0, double x1, double z1) => Regional && _field.CanopyIn(x0, z0, x1, z1);

    /// <summary>The whole world as a region holding <paramref name="kinds"/>, with the world's steepest weight changes.</summary>
    private DetailRegion Global(int kinds) =>
        new(kinds, _field.RockGradient, _field.ScreeGradient, _field.SmoothGradient) { CanopyGradient = _field.CanopyGradient };

    /// <summary>The bound in a region: the largest of its kinds' own, and, where kinds mix, their amplitudes times how much their weights change.</summary>
    private double Bound(double length, double[] kindBounds, DetailRegion region)
    {
        int kinds = region.Kinds != 0 ? region.Kinds : _present;
        double own = 0.0, changing = 0.0, amplitude = 0.0, half = 0.5 * length;
        bool mixed = (kinds & (kinds - 1)) != 0;
        foreach ((int bit, int index, double maxAmplitude, double gradient) in new (int, int, double, double)[]
        {
            (MaterialField.RockKind, 0, Rock.MaxAmplitude, region.RockGradient), (MaterialField.ScreeKind, 1, Scree.MaxAmplitude, region.ScreeGradient),
            (MaterialField.SmoothKind, 2, Smooth.MaxAmplitude, region.SmoothGradient), (MaterialField.CanopyKind, 3, Canopy.MaxAmplitude, region.CanopyGradient),
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
        (double rock, double scree, double forest) = _field.DetailWeightsAt(x, z);
        double smooth = 1.0 - rock - scree - forest;
        double best = Math.Max(Math.Max(rock, scree), Math.Max(smooth, forest));
        return rock == best ? DetailKind.Rock : scree == best ? DetailKind.Scree : smooth == best ? DetailKind.Smooth : DetailKind.Canopy;
    }
}
