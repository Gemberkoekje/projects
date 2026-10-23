namespace Vista.World;

/// <summary>The kinds of detail the ground gets below the world grid (vista-plan §10 M1.12).</summary>
public enum DetailKind
{
    /// <summary>Not set.</summary>
    None,

    /// <summary>Bare rock: sharp crests (<see cref="RockDetail"/>).</summary>
    Rock,

    /// <summary>Scree: angular stones (<see cref="ScreeDetail"/>).</summary>
    Scree,

    /// <summary>Soil and grass: M1.9's smooth noise (<see cref="TerrainDetail"/>).</summary>
    Smooth,

    /// <summary>Forest: the crowns of its canopy (<see cref="CanopyDetail"/>, vista-plan §10 M1.12b).</summary>
    Canopy,
}

/// <summary>
/// What the ground's materials read on the world grid (vista-plan §10 M1.12): per sample, the erosion's loose material, the land
/// that drains through it and its water (the deposition, flow-accumulation and wetness maps of M1.11a), and where the rock's beds
/// lie (M1.11d's strata: the bed-table coordinate of height 0 and the mottle), packed for the GPU, two words a sample; the bed table
/// with each bed's hardness and tint; and per sample the weights of the detail kinds the mesh adds below the grid. The shader and
/// <see cref="Inputs"/> read the maps alike: bilinear between the samples around a place, then the rules
/// (<see cref="MaterialRules.Evaluate"/>). A world that was not eroded has no maps (bare ground everywhere), but still its beds.
///
/// <para>The maps fade into the defaults over the grid's outer <see cref="TerrainHierarchy.BlendCells"/> cells, where the world
/// grid's surface fades into the base terrain's; beyond the grid the ground reads the defaults, with rock of middling hardness.
/// Built once per world; immutable afterwards.</para>
///
/// <para>The forest's detail weights (vista-plan §10 M1.12b) are the scene's, not the world's: where the forest stands depends on the
/// place's tree line and on the altitude of the world's height 0. A field built per world has none; <see cref="WithForest"/> makes a
/// field that shares this one's maps and adds them, which the mesh of a scene with forest carries.</para>
/// </summary>
public sealed class MaterialField
{
    /// <summary>log₁₀ of the smallest loose depth and water depth the codes tell apart from none, m (+ 0.01 m, so 0 encodes as 0).</summary>
    public const float DepthLogFloor = -2f;

    /// <summary>The decades of loose depth and water depth the codes span (0.01 m to 100 m).</summary>
    public const float DepthLogSpan = 4f;

    /// <summary>log₁₀ of the smallest upstream area the codes hold, m² (10 m²: below one 8 m cell).</summary>
    public const float AreaLogFloor = 1f;

    /// <summary>The decades of upstream area the codes span (up to 10⁹ m²).</summary>
    public const float AreaLogSpan = 8f;

    /// <summary>The largest |mottle| the codes hold.</summary>
    public const float MottleRange = 0.25f;

    /// <summary>The hardness the ground reads away from the maps.</summary>
    public const float DefaultHardness = 0.5f;

    /// <summary>The side of the box the detail kinds' weights are averaged over, in samples, so they change slowly enough to bound (see <see cref="MaterialDetail"/>).</summary>
    public const int DetailSmoothing = 7;

    /// <summary>
    /// A rock or scree detail weight below this many 255ths is dropped (the ground is smooth there): the averaging spreads a faint trace
    /// of an outcrop's crests three samples around it, which would make the mesh refine the meadow around it as rock.
    /// </summary>
    public const int DetailFloor = 13;

    /// <summary>The bit of rock's detail in a mask of kinds (<see cref="RegionIn"/>).</summary>
    public const int RockKind = 1;

    /// <summary>The bit of scree's detail.</summary>
    public const int ScreeKind = 2;

    /// <summary>The bit of the smooth ground's detail (soil and grass).</summary>
    public const int SmoothKind = 4;

    /// <summary>The bit of the forest's detail (its crowns).</summary>
    public const int CanopyKind = 8;

    /// <summary>Every kind.</summary>
    public const int AllKinds = RockKind | ScreeKind | SmoothKind | CanopyKind;

    private readonly uint[] _packed;
    private readonly float[] _beds;
    private readonly byte[] _rock;
    private readonly byte[] _scree;
    private readonly byte[] _forest;
    private readonly byte[] _cellKinds;
    private readonly byte[] _cellSteps;

    /// <summary>The summed-area table of the cells holding forest, (size)² entries from the corner (0, 0); empty without forest.</summary>
    private readonly int[] _canopyCells;

    private MaterialField(int size, double cell, uint[] packed, float[] beds, float tableScale, byte[] rock, byte[] scree, byte[] forest, bool eroded,
        uint noiseSeed, ForestPlacement forestPlacement)
    {
        Size = size;
        NoiseSeed = noiseSeed;
        Cell = cell;
        Origin = -0.5 * (size - 1) * cell;
        _packed = packed;
        _beds = beds;
        TableScale = tableScale;
        _rock = rock;
        _scree = scree;
        _forest = forest;
        (_cellKinds, _cellSteps) = CellKinds(size, rock, scree, forest);
        _canopyCells = forestPlacement.Grows ? CanopyTable(size, _cellKinds) : [];
        Eroded = eroded;
        Forest = forestPlacement;
        (RockGradient, ScreeGradient, SmoothGradient, CanopyGradient, OpenSmoothGradient) = Gradients(size, cell, rock, scree, forest);
    }

    /// <summary>Samples a side (the world grid's).</summary>
    public int Size { get; }

    /// <summary>Metres between samples.</summary>
    public double Cell { get; }

    /// <summary>World x (and z) of sample 0.</summary>
    public double Origin { get; }

    /// <summary>The seed of the material noise the detail weights were worked out with, which the shader's rules use too.</summary>
    public uint NoiseSeed { get; }

    /// <summary>Whether the maps come from an erosion (else the ground reads the defaults everywhere but for its beds).</summary>
    public bool Eroded { get; }

    /// <summary>Where the forest whose detail weights this field holds stands (<see cref="ForestPlacement.None"/>: none, a world's own field).</summary>
    public ForestPlacement Forest { get; }

    /// <summary>
    /// Two words per sample, row-major: the bed-table coordinate of world height 0 (a float's bits), then the codes, a byte each from
    /// the low end: the loose depth, the upstream area, the water depth and the mottle. What the GPU reads as <c>uint2</c>.
    /// </summary>
    public ReadOnlySpan<uint> Packed => _packed;

    /// <summary>Four floats per bed-table entry: the hardness, the bed's two tints and 0. What the GPU reads as <c>float4</c>.</summary>
    public ReadOnlySpan<float> Beds => _beds;

    /// <summary>Bed-table entries per metre of world height.</summary>
    public float TableScale { get; }

    /// <summary>The largest bed-table coordinate a lookup reads (the last entry less 1.001, as <see cref="RockHardness.Lookup"/>).</summary>
    public float TableLast => _beds.Length / 4 - 1.001f;

    /// <summary>The steepest the rock detail's weight changes, per metre (bilinear between the samples).</summary>
    public double RockGradient { get; }

    /// <summary>The steepest the scree detail's weight changes, per metre.</summary>
    public double ScreeGradient { get; }

    /// <summary>The steepest the smooth detail's weight changes, per metre.</summary>
    public double SmoothGradient { get; }

    /// <summary>The steepest the canopy detail's weight changes, per metre (0 without forest).</summary>
    public double CanopyGradient { get; }

    /// <summary>
    /// The steepest the smooth detail's weight changes where no forest lies, per metre: that of one less rock and scree, the world's
    /// own <see cref="SmoothGradient"/> before a scene's forest (vista-plan §10 M1.12b).
    /// </summary>
    public double OpenSmoothGradient { get; }

    /// <summary>Upload size, bytes.</summary>
    public long ByteSize => (long)_packed.Length * sizeof(uint) + (long)_beds.Length * sizeof(float);

    /// <summary>
    /// The material field of the world whose surface is <paramref name="surface"/> and whose erosion left <paramref name="erosion"/>
    /// (null: not eroded), with its rock laid out by <paramref name="strata"/> and its beds tinted by <paramref name="tints"/>
    /// (<see cref="RockHardness.Tints"/>); the detail weights by <paramref name="rules"/> with the material noise of
    /// <paramref name="noiseSeed"/>.
    /// </summary>
    public static MaterialField For(HeightField surface, ErosionResult? erosion, StrataGrid strata, float[] tints, MaterialRules rules, uint noiseSeed)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(strata);
        ArgumentNullException.ThrowIfNull(tints);
        ArgumentNullException.ThrowIfNull(rules);
        int n = surface.Size;
        if (strata.Offset.Length != n * n)
            throw new ArgumentException("The strata are not on the surface's grid.", nameof(strata));
        if (erosion is not null && erosion.Surface.Size != n)
            throw new ArgumentException("The erosion is not on the surface's grid.", nameof(erosion));
        if (tints.Length != 2 * strata.Table.Length)
            throw new ArgumentException("Expected two tints per bed-table entry.", nameof(tints));

        var beds = new float[4 * strata.Table.Length];
        for (int k = 0; k < strata.Table.Length; k++)
        {
            beds[4 * k] = strata.Table[k];
            beds[4 * k + 1] = tints[2 * k];
            beds[4 * k + 2] = tints[2 * k + 1];
        }

        var packed = new uint[2 * n * n];
        int band = Math.Min(TerrainHierarchy.BlendCells, (n - 1) / 8);
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
            {
                int c = j * n + i;
                float loose = 0f, wet = 0f, logArea = AreaLogFloor;
                if (erosion is not null)
                {
                    // The fade: the world grid's surface blends into the base terrain's over the outer band, and so do its maps.
                    int edge = Math.Min(Math.Min(i, n - 1 - i), Math.Min(j, n - 1 - j));
                    float x = edge >= band ? 1f : (float)edge / band;
                    float w = x * x * (3f - 2f * x);
                    loose = erosion.Loose[c] * w;
                    wet = erosion.WaterDepth[c] * w;
                    logArea = AreaLogFloor + ((float)Math.Log10(Math.Max(erosion.Drainage.Area(c), 10.0)) - AreaLogFloor) * w;
                }
                packed[2 * c] = BitConverter.SingleToUInt32Bits(strata.Offset[c]);
                packed[2 * c + 1] = DepthCode(loose) | (AreaCode(logArea) << 8) | (DepthCode(wet) << 16) | (MottleCode(strata.Mottle[c]) << 24);
            }
        });

        (byte[] rock, byte[] scree) = DetailWeights(surface, packed, beds, strata.TableScale, rules, noiseSeed);
        return new MaterialField(n, surface.CellSize, packed, beds, strata.TableScale, rock, scree, new byte[n * n], erosion is not null, noiseSeed,
            ForestPlacement.None);
    }

    /// <summary>
    /// This world's field with the detail weights of the forest that <paramref name="forest"/> places under <paramref name="rules"/>
    /// (vista-plan §10 M1.12b), on the world whose surface is <paramref name="surface"/>: the rules' forest at each sample, with its own
    /// slope, aspect, altitude and maps, averaged as rock's and scree's are and never more than they leave. Shares this field's maps;
    /// <see cref="ForestPlacement.None"/> gives the world's own field back.
    /// </summary>
    public MaterialField WithForest(HeightField surface, ForestPlacement forest, MaterialRules rules)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(forest);
        ArgumentNullException.ThrowIfNull(rules);
        if (surface.Size != Size)
            throw new ArgumentException("The surface is not on the field's grid.", nameof(surface));
        if (forest == Forest)
            return this;
        int n = Size;
        var forestBytes = new byte[n * n];
        if (forest.Grows)
        {
            MaterialConstants k = Apply(rules.Constants(0f, forest.Poleward, NoiseSeed, forest.TreeLineMetres, true));
            var weights = new float[n * n];
            Parallel.For(0, n, j =>
            {
                for (int i = 0; i < n; i++)
                {
                    (float nx, float nz) = SampleNormal(surface, i, j);
                    float x = (float)(Origin + i * Cell), z = (float)(Origin + j * Cell);
                    weights[j * n + i] = MaterialRules.Evaluate(Inputs(x, z, surface[i, j], nx, nz, forest.GroundAltitudeMetres, k), k).Forest;
                }
            });
            float[] smooth = BoxAverage(weights, n, DetailSmoothing);
            // The crowns fade out over the grid's outer band, where its surface fades into the base terrain's: beyond the grid the
            // detail reads the edge's weights, and crowns there would refine the far field as forest out to the horizon.
            int band = Math.Max(Math.Min(TerrainHierarchy.BlendCells, (n - 1) / 8), 1);
            Parallel.For(0, n, j =>
            {
                for (int i = 0; i < n; i++)
                {
                    int c = j * n + i;
                    int edge = Math.Min(Math.Min(i, n - 1 - i), Math.Min(j, n - 1 - j));
                    float e = edge >= band ? 1f : (float)edge / band;
                    int f = (int)MathF.Round(Math.Clamp(smooth[c], 0f, 1f) * (e * e * (3f - 2f * e)) * 255f);
                    forestBytes[c] = (byte)Math.Min(f < DetailFloor ? 0 : f, 255 - _rock[c] - _scree[c]);
                }
            });
        }
        return new MaterialField(n, Cell, _packed, _beds, TableScale, _rock, _scree, forestBytes, Eroded, NoiseSeed, forest);
    }

    /// <summary>
    /// The inputs of the rules at world <c>(x, z)</c> for a surface at world height <paramref name="height"/> whose world-grid normal
    /// has <paramref name="normalX"/> and <paramref name="normalZ"/> (the normal map's), with world height 0 at
    /// <paramref name="groundAltitude"/> above sea level: the shader's <c>MaterialInputsAt</c>, in the same float operations.
    /// </summary>
    public MaterialInputs Inputs(float x, float z, float height, float normalX, float normalZ, float groundAltitude, in MaterialConstants k)
    {
        float ny = MathF.Sqrt(MathF.Max(1f - normalX * normalX - normalZ * normalZ, 0f));
        float slope = MathF.Acos(MathF.Min(ny, 1f)) * (180f / MathF.PI);
        (float b1, float b2) = MaterialNoise.Breakup(k.NoiseSeed, x, z, k.Breakup1, k.Breakup2);
        float b3 = MaterialNoise.ForestBreakup(k.NoiseSeed, x, z, k.Breakup3);
        Sample s = At(x, z, height, k.TableScale, k.TableLast);
        return new MaterialInputs(slope, normalZ * k.Poleward, groundAltitude + height, s.Loose, s.Log10Area, s.Wet, s.Hardness, b1, b2, b3);
    }

    /// <summary>
    /// The bed at world <c>(x, z)</c> and height <paramref name="height"/>: its hardness (with the mottle), its two tints, and its
    /// bed-table coordinate (for its laminae); the defaults away from the map.
    /// </summary>
    public (float Hardness, float TintU, float TintV, float Table) BedAt(float x, float z, float height)
    {
        Sample s = At(x, z, height, TableScale, TableLast);
        return (s.Hardness, s.TintU, s.TintV, s.Table);
    }

    /// <summary>The detail kinds' weights at world <c>(x, z)</c>: bilinear between the samples around it, the edge's outside the grid.</summary>
    public (double Rock, double Scree, double Forest) DetailWeightsAt(double x, double z)
    {
        double u = Math.Clamp((x - Origin) / Cell, 0.0, Size - 1), v = Math.Clamp((z - Origin) / Cell, 0.0, Size - 1);
        int i = Math.Min((int)u, Size - 2), j = Math.Min((int)v, Size - 2);
        double fu = u - i, fv = v - j;
        int c = j * Size + i;
        double Bilinear(byte[] w) => ((w[c] + (w[c + 1] - w[c]) * fu) * (1.0 - fv) + (w[c + Size] + (w[c + Size + 1] - w[c + Size]) * fu) * fv) * (1.0 / 255.0);
        return (Bilinear(_rock), Bilinear(_scree), Bilinear(_forest));
    }

    /// <summary>
    /// The detail's region in the box from world <c>(x0, z0)</c> to <c>(x1, z1)</c> (<see cref="DetailRegion"/>): of every cell the box
    /// reaches into, the kinds weighted at any of its corners (the weights are bilinear in a cell, so nothing else can be there), and
    /// for each kind the steepest its weight changes there, per metre. Outside the grid the edge cells', as the weights.
    /// </summary>
    public DetailRegion RegionIn(double x0, double z0, double x1, double z1)
    {
        int last = Size - 2;
        int i0 = Math.Clamp((int)Math.Floor((x0 - Origin) / Cell), 0, last), i1 = Math.Clamp((int)Math.Floor((x1 - Origin) / Cell), 0, last);
        int j0 = Math.Clamp((int)Math.Floor((z0 - Origin) / Cell), 0, last), j1 = Math.Clamp((int)Math.Floor((z1 - Origin) / Cell), 0, last);
        int kinds = 0, rock = 0, scree = 0, smooth = 0, canopy = 0;
        for (int j = j0; j <= j1; j++)
        {
            for (int i = i0; i <= i1; i++)
            {
                int c = j * (Size - 1) + i;
                kinds |= _cellKinds[c];
                rock = Math.Max(rock, _cellSteps[4 * c]);
                scree = Math.Max(scree, _cellSteps[4 * c + 1]);
                smooth = Math.Max(smooth, _cellSteps[4 * c + 2]);
                canopy = Math.Max(canopy, _cellSteps[4 * c + 3]);
            }
        }
        // Bilinear in a cell, a weight's gradient is at most √2 times its largest step between neighbouring corners over the cell.
        double perStep = Math.Sqrt(2.0) / (255.0 * Cell);
        return new DetailRegion(kinds, rock * perStep, scree * perStep, smooth * perStep) { CanopyGradient = canopy * perStep };
    }

    /// <summary>
    /// Whether forest's detail is weighted in any cell the box from world <c>(x0, z0)</c> to <c>(x1, z1)</c> reaches into (vista-plan
    /// §10 M1.12b), the edge cells' outside the grid: in constant time, from a summed-area table of the cells.
    /// </summary>
    public bool CanopyIn(double x0, double z0, double x1, double z1)
    {
        if (_canopyCells.Length == 0)
            return false;
        int last = Size - 2;
        int i0 = Math.Clamp((int)Math.Floor((x0 - Origin) / Cell), 0, last), i1 = Math.Clamp((int)Math.Floor((x1 - Origin) / Cell), 0, last);
        int j0 = Math.Clamp((int)Math.Floor((z0 - Origin) / Cell), 0, last), j1 = Math.Clamp((int)Math.Floor((z1 - Origin) / Cell), 0, last);
        int n = Size;
        return _canopyCells[(j1 + 1) * n + i1 + 1] - _canopyCells[j0 * n + i1 + 1] - _canopyCells[(j1 + 1) * n + i0] + _canopyCells[j0 * n + i0] > 0;
    }

    /// <summary>These constants with the material map's place and the bed table.</summary>
    public MaterialConstants Apply(MaterialConstants k) => k with
    {
        TableScale = TableScale,
        TableLast = TableLast,
        MapOriginX = (float)Origin,
        MapOriginZ = (float)Origin,
        MapCell = (float)Cell,
        MapSize = (uint)Size,
    };

    /// <summary>
    /// The material weights of every sample under <paramref name="k"/> (the scene's grass line and pole), with world height 0 at
    /// <paramref name="groundAltitude"/>: what the dry run's material map and the sidecar's shares show.
    /// </summary>
    public MaterialWeights[] SampleWeights(HeightField surface, in MaterialConstants k, float groundAltitude)
    {
        ArgumentNullException.ThrowIfNull(surface);
        int n = Size;
        var weights = new MaterialWeights[n * n];
        MaterialConstants constants = k;
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
            {
                (float nx, float nz) = SampleNormal(surface, i, j);
                float x = (float)(Origin + i * Cell), z = (float)(Origin + j * Cell);
                weights[j * n + i] = MaterialRules.Evaluate(Inputs(x, z, surface[i, j], nx, nz, groundAltitude, constants), constants);
            }
        });
        return weights;
    }

    // ── The GPU's reads, in its float operations ─────────────────────────────────────────────────────

    private readonly record struct Sample(float Loose, float Log10Area, float Wet, float Hardness, float TintU, float TintV, float Table);

    /// <summary>The decoded maps and the bed at a place: bilinear on the codes and the offsets, then decoded (the shader's <c>MaterialSampleAt</c>).</summary>
    private Sample At(float x, float z, float height, float tableScale, float tableLast)
    {
        float extent = (float)Cell * (Size - 1);
        float ox = (float)Origin;
        if (!(x >= ox && x <= ox + extent && z >= ox && z <= ox + extent))
            return new Sample(0f, AreaLogFloor, 0f, DefaultHardness, 0f, 0f, 0f);
        float u = Math.Clamp((x - ox) / (float)Cell, 0f, Size - 1), v = Math.Clamp((z - ox) / (float)Cell, 0f, Size - 1);
        int i = Math.Min((int)u, Size - 2), j = Math.Min((int)v, Size - 2);
        float fu = u - i, fv = v - j;
        int c = j * Size + i;
        (float o00, float l00, float a00, float w00, float m00) = Unpack(c);
        (float o10, float l10, float a10, float w10, float m10) = Unpack(c + 1);
        (float o01, float l01, float a01, float w01, float m01) = Unpack(c + Size);
        (float o11, float l11, float a11, float w11, float m11) = Unpack(c + Size + 1);
        float Mix(float v00, float v10, float v01, float v11)
        {
            float bottom = v00 + (v10 - v00) * fu, top = v01 + (v11 - v01) * fu;
            return bottom + (top - bottom) * fv;
        }
        float offset = Mix(o00, o10, o01, o11);
        float loose = DepthOf(Mix(l00, l10, l01, l11));
        float logArea = AreaOf(Mix(a00, a10, a01, a11));
        float wet = DepthOf(Mix(w00, w10, w01, w11));
        float mottle = MottleOf(Mix(m00, m10, m01, m11));
        float t = Math.Clamp(height * tableScale + offset, 0f, tableLast);
        int k = (int)MathF.Floor(t);
        float f = t - k;
        float hard = _beds[4 * k] + (_beds[4 * k + 4] - _beds[4 * k]) * f;
        float tintU = _beds[4 * k + 1] + (_beds[4 * k + 5] - _beds[4 * k + 1]) * f;
        float tintV = _beds[4 * k + 2] + (_beds[4 * k + 6] - _beds[4 * k + 2]) * f;
        return new Sample(loose, logArea, wet, Math.Clamp(hard + mottle, 0f, 1f), tintU, tintV, t);
    }

    private (float Offset, float Loose, float Area, float Wet, float Mottle) Unpack(int c)
    {
        uint codes = _packed[2 * c + 1];
        return (BitConverter.UInt32BitsToSingle(_packed[2 * c]), codes & 0xFF, (codes >> 8) & 0xFF, (codes >> 16) & 0xFF, codes >> 24);
    }

    /// <summary>The code of a loose depth or water depth, m: log₁₀(d + 0.01) over four decades in a byte.</summary>
    public static uint DepthCode(float metres) =>
        (uint)MathF.Round(Math.Clamp((MathF.Log10(Math.Max(metres, 0f) + 0.01f) - DepthLogFloor) / DepthLogSpan, 0f, 1f) * 255f);

    /// <summary>The depth of a (bilinear) code, m.</summary>
    public static float DepthOf(float code) => MathF.Pow(10f, code * (DepthLogSpan / 255f) + DepthLogFloor) - 0.01f;

    /// <summary>The code of log₁₀ of an upstream area.</summary>
    public static uint AreaCode(float log10Area) => (uint)MathF.Round(Math.Clamp((log10Area - AreaLogFloor) / AreaLogSpan, 0f, 1f) * 255f);

    /// <summary>log₁₀ of the upstream area of a (bilinear) code.</summary>
    public static float AreaOf(float code) => code * (AreaLogSpan / 255f) + AreaLogFloor;

    /// <summary>The code of a mottle, signed about 128.</summary>
    public static uint MottleCode(float mottle) => (uint)(MathF.Round(Math.Clamp(mottle / MottleRange, -1f, 1f) * 127f) + 128f);

    /// <summary>The mottle of a (bilinear) code.</summary>
    public static float MottleOf(float code) => (code - 128f) * (MottleRange / 127f);

    // ── The detail kinds' weights ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Per sample the rules' rock and scree (soil and grass make the rest, the smooth kind), at the sample's own slope, bed and maps,
    /// averaged over <see cref="DetailSmoothing"/>² samples and stored as bytes that never sum past 255.
    /// </summary>
    private static (byte[] Rock, byte[] Scree) DetailWeights(HeightField surface, uint[] packed, float[] beds, float tableScale, MaterialRules rules,
        uint noiseSeed)
    {
        int n = surface.Size;
        double cell = surface.CellSize;
        float origin = (float)(-0.5 * (n - 1) * cell);
        // The grass line does not move the detail (soil and grass are both smooth), so any will do; nor does the forest move rock and
        // scree (it takes only the ground they leave), whose weights it adds per scene (WithForest).
        MaterialConstants k = rules.Constants(0f, 1f, noiseSeed, 0f, false) with { TableScale = tableScale, TableLast = beds.Length / 4 - 1.001f };
        var rock = new float[n * n];
        var scree = new float[n * n];
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
            {
                int c = j * n + i;
                (float nx, float nz) = SampleNormal(surface, i, j);
                float ny = MathF.Sqrt(MathF.Max(1f - nx * nx - nz * nz, 0f));
                float x = origin + (float)(i * cell), z = origin + (float)(j * cell);
                (float b1, float b2) = MaterialNoise.Breakup(k.NoiseSeed, x, z, k.Breakup1, k.Breakup2);
                uint codes = packed[2 * c + 1];
                float t = Math.Clamp(surface[i, j] * tableScale + BitConverter.UInt32BitsToSingle(packed[2 * c]), 0f, k.TableLast);
                int e = (int)MathF.Floor(t);
                float hard = Math.Clamp(beds[4 * e] + (beds[4 * e + 4] - beds[4 * e]) * (t - e) + MottleOf(codes >> 24), 0f, 1f);
                var inputs = new MaterialInputs(MathF.Acos(MathF.Min(ny, 1f)) * (180f / MathF.PI), 0f, 0f, DepthOf(codes & 0xFF),
                    AreaOf((codes >> 8) & 0xFF), DepthOf((codes >> 16) & 0xFF), hard, b1, b2, 0f);
                MaterialWeights w = MaterialRules.Evaluate(inputs, k);
                rock[c] = w.Rock;
                scree[c] = w.Scree;
            }
        });
        float[] smoothRock = BoxAverage(rock, n, DetailSmoothing);
        float[] smoothScree = BoxAverage(scree, n, DetailSmoothing);
        var rockBytes = new byte[n * n];
        var screeBytes = new byte[n * n];
        Parallel.For(0, n, j =>
        {
            for (int c = j * n; c < (j + 1) * n; c++)
            {
                int r = (int)MathF.Round(Math.Clamp(smoothRock[c], 0f, 1f) * 255f);
                int s = (int)MathF.Round(Math.Clamp(smoothScree[c], 0f, 1f) * 255f);
                r = r < DetailFloor ? 0 : r;
                s = s < DetailFloor ? 0 : s;
                rockBytes[c] = (byte)r;
                screeBytes[c] = (byte)Math.Min(s, 255 - r);
            }
        });
        return (rockBytes, screeBytes);
    }

    /// <summary>Per cell, the kinds weighted at any of its four corners, and per kind the largest step of its weight along the cell's four edges.</summary>
    private static (byte[] Kinds, byte[] Steps) CellKinds(int n, byte[] rock, byte[] scree, byte[] forest)
    {
        var kinds = new byte[(n - 1) * (n - 1)];
        var steps = new byte[4 * (n - 1) * (n - 1)];
        Parallel.For(0, n - 1, j =>
        {
            Span<int> r = stackalloc int[4];
            Span<int> s = stackalloc int[4];
            Span<int> f = stackalloc int[4];
            for (int i = 0; i < n - 1; i++)
            {
                int k = 0;
                int[] corners = [j * n + i, j * n + i + 1, (j + 1) * n + i + 1, (j + 1) * n + i];
                for (int q = 0; q < 4; q++)
                {
                    r[q] = rock[corners[q]];
                    s[q] = scree[corners[q]];
                    f[q] = forest[corners[q]];
                    if (r[q] > 0)
                        k |= RockKind;
                    if (s[q] > 0)
                        k |= ScreeKind;
                    if (f[q] > 0)
                        k |= CanopyKind;
                    if (r[q] + s[q] + f[q] < 255)
                        k |= SmoothKind;
                }
                int c = j * (n - 1) + i;
                kinds[c] = (byte)k;
                int rockStep = 0, screeStep = 0, smoothStep = 0, canopyStep = 0;
                for (int q = 0; q < 4; q++)
                {
                    int p = (q + 1) % 4;
                    rockStep = Math.Max(rockStep, Math.Abs(r[p] - r[q]));
                    screeStep = Math.Max(screeStep, Math.Abs(s[p] - s[q]));
                    canopyStep = Math.Max(canopyStep, Math.Abs(f[p] - f[q]));
                    smoothStep = Math.Max(smoothStep, Math.Abs(r[p] + s[p] + f[p] - r[q] - s[q] - f[q]));
                }
                steps[4 * c] = (byte)rockStep;
                steps[4 * c + 1] = (byte)screeStep;
                steps[4 * c + 2] = (byte)smoothStep;
                steps[4 * c + 3] = (byte)canopyStep;
            }
        });
        return (kinds, steps);
    }

    /// <summary>
    /// The summed-area table of the cells whose kinds include the canopy: entry <c>(j, i)</c> of the <c>n × n</c> table counts those in
    /// the cells before column <c>i</c> and row <c>j</c>.
    /// </summary>
    private static int[] CanopyTable(int n, byte[] cellKinds)
    {
        var table = new int[n * n];
        for (int j = 0; j < n - 1; j++)
        {
            int row = 0;
            for (int i = 0; i < n - 1; i++)
            {
                row += (cellKinds[j * (n - 1) + i] & CanopyKind) != 0 ? 1 : 0;
                table[(j + 1) * n + i + 1] = table[j * n + i + 1] + row;
            }
        }
        return table;
    }

    /// <summary>The world grid's normal at sample (i, j): central differences, one-sided at the edges (as <see cref="NormalMap"/>'s).</summary>
    private static (float X, float Z) SampleNormal(HeightField surface, int i, int j)
    {
        int n = surface.Size;
        int i0 = Math.Max(i - 1, 0), i1 = Math.Min(i + 1, n - 1), j0 = Math.Max(j - 1, 0), j1 = Math.Min(j + 1, n - 1);
        double dhdx = (surface[i1, j] - surface[i0, j]) / ((i1 - i0) * surface.CellSize);
        double dhdz = (surface[i, j1] - surface[i, j0]) / ((j1 - j0) * surface.CellSize);
        double inv = 1.0 / Math.Sqrt(dhdx * dhdx + 1.0 + dhdz * dhdz);
        return ((float)(-dhdx * inv), (float)(-dhdz * inv));
    }

    /// <summary>The mean over the <paramref name="width"/>² samples around each sample (fewer at the edges): two separable passes.</summary>
    private static float[] BoxAverage(float[] values, int n, int width)
    {
        int r = width / 2;
        var rows = new float[n * n];
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                int from = Math.Max(i - r, 0), to = Math.Min(i + r, n - 1);
                for (int a = from; a <= to; a++)
                    sum += values[j * n + a];
                rows[j * n + i] = sum / (to - from + 1);
            }
        });
        var result = new float[n * n];
        Parallel.For(0, n, j =>
        {
            int from = Math.Max(j - r, 0), to = Math.Min(j + r, n - 1);
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                for (int b = from; b <= to; b++)
                    sum += rows[b * n + i];
                result[j * n + i] = sum / (to - from + 1);
            }
        });
        return result;
    }

    /// <summary>
    /// The steepest each kind's weight changes, per metre: bilinear between samples, its gradient is at most the largest step between
    /// neighbours along x and along z, combined, over the cell.
    /// </summary>
    private static (double Rock, double Scree, double Smooth, double Canopy, double OpenSmooth) Gradients(int n, double cell, byte[] rock, byte[] scree,
        byte[] forest)
    {
        var steps = new int[10]; // rock x, rock z, scree x, scree z, smooth x, smooth z, canopy x, canopy z, open smooth x, open smooth z
        object gate = new();
        Parallel.For(0, n, () => new int[10], (j, _, local) =>
        {
            for (int i = 0; i < n; i++)
            {
                int c = j * n + i;
                int open = 255 - rock[c] - scree[c];
                int smooth = open - forest[c];
                if (i + 1 < n)
                {
                    local[0] = Math.Max(local[0], Math.Abs(rock[c + 1] - rock[c]));
                    local[2] = Math.Max(local[2], Math.Abs(scree[c + 1] - scree[c]));
                    local[4] = Math.Max(local[4], Math.Abs(255 - rock[c + 1] - scree[c + 1] - forest[c + 1] - smooth));
                    local[6] = Math.Max(local[6], Math.Abs(forest[c + 1] - forest[c]));
                    local[8] = Math.Max(local[8], Math.Abs(255 - rock[c + 1] - scree[c + 1] - open));
                }
                if (j + 1 < n)
                {
                    local[1] = Math.Max(local[1], Math.Abs(rock[c + n] - rock[c]));
                    local[3] = Math.Max(local[3], Math.Abs(scree[c + n] - scree[c]));
                    local[5] = Math.Max(local[5], Math.Abs(255 - rock[c + n] - scree[c + n] - forest[c + n] - smooth));
                    local[7] = Math.Max(local[7], Math.Abs(forest[c + n] - forest[c]));
                    local[9] = Math.Max(local[9], Math.Abs(255 - rock[c + n] - scree[c + n] - open));
                }
            }
            return local;
        }, local =>
        {
            lock (gate)
            {
                for (int k = 0; k < steps.Length; k++)
                    steps[k] = Math.Max(steps[k], local[k]);
            }
        });
        double G(int x, int z) => Math.Sqrt((double)steps[x] * steps[x] + (double)steps[z] * steps[z]) / (255.0 * cell);
        return (G(0, 1), G(2, 3), G(4, 5), G(6, 7), G(8, 9));
    }
}

/// <summary>
/// The detail below the grid in a box of the world (<see cref="MaterialField.RegionIn"/>): which kinds are weighted anywhere in it, as
/// a mask, and how fast each one's weight changes there at most, per metre.
/// </summary>
/// <param name="Kinds">The kinds, as <see cref="MaterialField.RockKind"/>, <see cref="MaterialField.ScreeKind"/>, <see cref="MaterialField.SmoothKind"/> and <see cref="MaterialField.CanopyKind"/> bits.</param>
/// <param name="RockGradient">The steepest the rock detail's weight changes in the box, per metre.</param>
/// <param name="ScreeGradient">The scree detail's.</param>
/// <param name="SmoothGradient">The smooth detail's.</param>
public readonly record struct DetailRegion(int Kinds, double RockGradient, double ScreeGradient, double SmoothGradient)
{
    /// <summary>The canopy detail's (vista-plan §10 M1.12b).</summary>
    public double CanopyGradient { get; init; }
}

/// <summary>
/// Where a scene's forest stands, as the mesh's detail reads it (vista-plan §10 M1.12b): the forest is the scene's, not the world's,
/// because its tree line comes from the place's climate and is compared with the altitude the scene gives the world's height 0.
/// </summary>
/// <param name="Grows">Whether there is forest at all (<see cref="None"/>: none).</param>
/// <param name="TreeLineMetres">The tree line above sea level, m.</param>
/// <param name="Poleward">1 when the pole lies toward +z, −1 when toward −z.</param>
/// <param name="GroundAltitudeMetres">The altitude of world height 0 above sea level, m.</param>
public sealed record ForestPlacement(bool Grows, float TreeLineMetres, int Poleward, float GroundAltitudeMetres)
{
    /// <summary>No forest: a world's own field, and the mesh of a scene with <c>--forest off</c>.</summary>
    public static ForestPlacement None { get; } = new(false, 0f, 1, 0f);
}
