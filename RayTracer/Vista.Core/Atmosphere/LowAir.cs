using Vista.Scene;
using Vista.Weather;
using Vista.World;

namespace Vista.Atmosphere;

/// <summary>
/// How the renderer takes the valleys' pools (vista-plan §10 M1.13b): the noise that breaks them up, the mist's optics, the sunlight
/// volume's layout and the march's steps. The defaults are the slice's choices (the noise's strength and scale from a sheet).
/// </summary>
public sealed record LowAirSettings
{
    /// <summary>The defaults.</summary>
    public static LowAirSettings Default { get; } = new();

    /// <summary>How far the 3D noise moves the pools' and the mist's tops up or down, m: the wisps.</summary>
    public double WispAmplitudeMetres { get; init; } = 25.0;

    /// <summary>The 3D noise's horizontal wavelength, m.</summary>
    public double WispScaleMetres { get; init; } = 220.0;

    /// <summary>The 3D noise's vertical wavelength, m.</summary>
    public double WispVerticalMetres { get; init; } = 60.0;

    /// <summary>How much the 2D noise moves the mist's top, as a share of its depth: above 1 it opens clear patches.</summary>
    public double PatchStrength { get; init; } = 0.8;

    /// <summary>The 2D noise's wavelength, m: the patches.</summary>
    public double PatchScaleMetres { get; init; } = 1800.0;

    /// <summary>
    /// Where a mist frays: within this many metres of its top the ground rises through it (a deck's edge, lapping the slopes), and a
    /// weak mist everywhere. Under a strong inversion a fog's top is flat and sharp and its body opaque, a white surface from above,
    /// its edges frayed and wispy (the Sunnegga webcam, vista-plan §10): the mist's wisps and patches take the larger of its edge's
    /// share (1 at the ground, 0 this far under the top) and its weakness (1 less its water over the cap), so a strong deck's top over
    /// a valley's middle lies flat.
    /// </summary>
    public double FrayMetres { get; init; } = 80.0;

    /// <summary>The softness of a pool's haze top (a logistic's scale, m): M1.13a's lid.</summary>
    public double HazeSoftnessMetres { get; init; } = 30.0;

    /// <summary>
    /// The softness of the mist's top (a logistic's scale, m): a fog layer deeper than a few tens of metres is mixed by its own turbulence,
    /// its water about uniform under a fairly sharp top (Roach et al. 1976), the surface seen from above.
    /// </summary>
    public double MistSoftnessMetres { get; init; } = 8.0;

    /// <summary>The width over which the world's level of the pool map blends into the far field's at the world's edge, m (the terrain's own blend).</summary>
    public double BlendMetres { get; init; } = TerrainHierarchy.BlendCells * WorldGrid.Default.CellSize;

    /// <summary>The Henyey–Greenstein asymmetry of the mist's droplets (water droplets of about 10 µm: strongly forward).</summary>
    public double MistAsymmetry { get; init; } = 0.85;

    /// <summary>The mist's single-scattering albedo: water hardly absorbs in the visible.</summary>
    public double MistAlbedo { get; init; } = 0.9999;

    /// <summary>The sunlight volume's cell in pool map cells (128 m over the world, 512 m over the far field).</summary>
    public int VolumeRatio { get; init; } = 2;

    /// <summary>
    /// The sunlight volume's layers over each column: <see cref="VolumePoolLayers"/> from a pool's floor to its top plus
    /// <see cref="VolumeMarginMetres"/>, the rest from there to the pools' highest reach (the low air a ridge may shade, M1.4's lit-air
    /// limit lifted below it).
    /// </summary>
    public int VolumeLayers { get; init; } = 32;

    /// <summary>The sunlight volume's layers over a column's pool (<see cref="VolumeLayers"/>).</summary>
    public int VolumePoolLayers { get; init; } = 24;

    /// <summary>How far above a pool's top the sunlight volume reaches, m: the noise's reach and the soft top's.</summary>
    public double VolumeMarginMetres { get; init; } = 150.0;

    /// <summary>The steps of the march toward the sun from a voxel.</summary>
    public int SunSteps { get; init; } = 48;

    /// <summary>How far toward the sun the volume's march goes at most, m.</summary>
    public double SunReachMetres { get; init; } = 30_000.0;

    /// <summary>The steps of the march straight up from a voxel (the mist's depth above it).</summary>
    public int UpSteps { get; init; } = 16;

    /// <summary>The length of a step of the march through the pools' haze, m (near a mist the steps shrink: <see cref="MistStepDepth"/>).</summary>
    public double MarchStepMetres { get; init; } = 150.0;

    /// <summary>
    /// The ceiling map's block, in pool map cells (1 km over the world, 4 km over the far field): above a block's ceiling no pool
    /// reaches, and the march strides over it.
    /// </summary>
    public int CeilingBlock { get; init; } = 16;

    /// <summary>The most steps a segment's march through the pools takes.</summary>
    public int MarchMaxSteps { get; init; } = 64;

    /// <summary>How the march's steps grow with distance along a camera ray: a step is at least this share of its distance (far pools need fewer).</summary>
    public double MarchGrowth { get; init; } = 0.015;

    /// <summary>The most steps a bounce's march takes: indirect light, whose noise the samples average, needs less of the pools' detail.</summary>
    public int BounceMaxSteps { get; init; } = 8;

    /// <summary>The length of a step of a bounce's march, m.</summary>
    public double BounceStepMetres { get; init; } = 300.0;

    /// <summary>
    /// The optical depth of a step inside a mist, of the mist the last step found: the march's steps shrink to its density there, so
    /// its top and its patches are resolved where the haze is not (<see cref="MistBandStepMetres"/>).
    /// </summary>
    public double MistStepDepth { get; init; } = 0.5;

    /// <summary>
    /// The longest step in a mist's band (from its floor to the highest its top reaches): a ray that enters the band takes steps no longer
    /// than this, nor than <see cref="MistStepDepth"/> of the mist it last found; above the band a step reaches at most to it.
    /// </summary>
    public double MistBandStepMetres { get; init; } = 50.0;

    /// <summary>The most steps a camera ray's march to the sky takes (the far valleys' pools along the horizon, seen as soft haze).</summary>
    public int SkyMaxSteps { get; init; } = 24;

    /// <summary>How a camera ray's march to the sky grows its steps with distance (<see cref="MarchGrowth"/>'s twin).</summary>
    public double SkyGrowth { get; init; } = 0.03;

    /// <summary>The octaves of the mist's multiple scattering (Wrenninge et al. 2013), the first being single scattering.</summary>
    public int Octaves { get; init; } = 6;

    /// <summary>Each octave's share of the one before's light.</summary>
    public double OctaveA { get; init; } = 0.5;

    /// <summary>Each octave's share of the one before's optical depth toward the sun.</summary>
    public double OctaveB { get; init; } = 0.5;

    /// <summary>Each octave's share of the one before's asymmetry.</summary>
    public double OctaveC { get; init; } = 0.5;

    /// <summary>
    /// The weight of the second octave (the first beyond single scattering, still a forward lobe), times the sine of the sun's elevation
    /// to <see cref="ForwardLobeSunPower"/>. The octaves were made for clouds lit along the sun's path; a mist deck is a slab wider than it
    /// is deep, which catches the sun per unit of its top, so its light scattered many times falls with the sun's elevation where the
    /// octaves keep it. Fitted to a Monte Carlo of a slab (vista-log/M1.13b.md: g 0.85, optical depth 1–6, sun 3–45°, views 3–12° down
    /// at every azimuth): plain octaves ran 0.5–5.5× the truth, the brightest with a low sun behind the camera; this and
    /// <see cref="DiffuseWeight"/> within 0.6–1.9×.
    /// </summary>
    public double ForwardLobeWeight { get; init; } = 2.0;

    /// <summary>The power of the sine of the sun's elevation on the second octave (<see cref="ForwardLobeWeight"/>).</summary>
    public double ForwardLobeSunPower { get; init; } = 0.25;

    /// <summary>
    /// The weight of the octaves past the second (light scattered so many times it is nearly diffuse), times the sine of the sun's
    /// elevation to <see cref="DiffuseSunPower"/>: the sunlight a deck catches per unit of its top (<see cref="ForwardLobeWeight"/>).
    /// </summary>
    public double DiffuseWeight { get; init; } = 1.6;

    /// <summary>The power of the sine of the sun's elevation on the octaves past the second (<see cref="DiffuseWeight"/>).</summary>
    public double DiffuseSunPower { get; init; } = 1.0;

    /// <summary>The scale of the sky's light (the tables' multiple scattering) on the mist.</summary>
    public double Ambient { get; init; } = 1.0;

    /// <summary>
    /// How much of the pools' haze the atmosphere's tables carry as one flat layer (<see cref="ValleyPools.AverageLayer"/>): the sky view
    /// and the scattering orders see it; the march takes the pools themselves and leaves it out. The pools' average, chosen against the
    /// reference (vista-log/M1.13b.md): on the misty ridges the march ran +2.4% with it and +5.6% with twice it, the haze alone +3.7%
    /// and +6.8%. (Twice it looked better while the reference still found pools under the floors.)
    /// </summary>
    public double TablesLayerShare { get; init; } = 1.0;

    /// <summary>
    /// What the tables' multiple scattering keeps in the terrain's full shadow (the sunlight volume's): the tables light every point as
    /// if the sun reached the air around it, while in a ridge's shadow the light scattered many times by the shaded air near it is gone
    /// and what is left comes from the sky above. Scaled between this and 1 by the shadow. Half, against the shadowed reference on the
    /// misty ridges (vista-log/M1.13b.md): 1 left the march +23% over it, 0.5 +19%, 0 +15%, the rest the surfaces' and the horizon's
    /// sky, which the sky view lights as if no ridge stood; 0 would leave a mist in shadow without the sky's light the webcam shows.
    /// </summary>
    public double ShadowedScattering { get; init; } = 0.5;
}

/// <summary>What a render of a <see cref="LowAir"/> depends on, by value (a render's key holds it).</summary>
/// <param name="CellsHash">A fingerprint of the packed cells.</param>
/// <param name="Settings">How the renderer takes them.</param>
/// <param name="NoiseSeed">The noise's seed.</param>
/// <param name="Air">The air whose haze unit counts their haze, and whose flat layer the volume subtracts.</param>
public readonly record struct LowAirKey(ulong CellsHash, LowAirSettings Settings, uint NoiseSeed, AtmosphereModel Air);

/// <summary>A pool where a point lies (the shader's <c>PoolSample</c>).</summary>
/// <param name="Floor">The valley floor, m above sea level.</param>
/// <param name="Depth">The pool's depth above it, m.</param>
/// <param name="Mist">The mist's depth above it, m.</param>
/// <param name="HazeFloor">The pool's haze at its floor, in the haze's unit.</param>
/// <param name="HazeTop">And at its top.</param>
/// <param name="Water">The mist's liquid water before the cap, g/m³.</param>
/// <param name="Cover">How far the mist's top stands above the ground, m (<see cref="Vista.Weather.PoolCell.CoverMetres"/>).</param>
public readonly record struct PoolSample(float Floor, float Depth, float Mist, float HazeFloor, float HazeTop, float Water, float Cover)
{
    /// <summary>The value <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static PoolSample Lerp(PoolSample a, PoolSample b, float t) => new(
        a.Floor + (b.Floor - a.Floor) * t, a.Depth + (b.Depth - a.Depth) * t, a.Mist + (b.Mist - a.Mist) * t,
        a.HazeFloor + (b.HazeFloor - a.HazeFloor) * t, a.HazeTop + (b.HazeTop - a.HazeTop) * t, a.Water + (b.Water - a.Water) * t,
        a.Cover + (b.Cover - a.Cover) * t);
}

/// <summary>
/// The valleys' pools as the renderer takes them (vista-plan §10 M1.13b): the packed cells and the sunlight volume's layout on the GPU,
/// and the float twins of the shader's functions (<c>VistaLowAir.hlsli</c>), operation for operation, so <c>--gpu-selftest</c> can
/// hold the GPU's against them: the noise, the pool at a place (the world's level blending into the far field's at the world's edge),
/// and the density at a point.
/// <list type="bullet">
///   <item><b>The haze</b> in a pool: its profile from the floor's density to the top's (<see cref="ValleyPools"/>), under a logistic
///   top <see cref="LowAirSettings.HazeSoftnessMetres"/> soft, moved by a 3D noise (the wisps) of up to
///   <see cref="LowAirSettings.WispAmplitudeMetres"/>.</item>
///   <item><b>The mist</b>: its water the floor's throughout (a fog layer is mixed by its own turbulence), Kunkel's extinction of it, under
///   a logistic top <see cref="LowAirSettings.MistSoftnessMetres"/> soft; its top moved by the wisps and by a 2D noise (the patches),
///   which opens clear patches where it would sink below the floor, never above its pool's.</item>
/// </list>
/// </summary>
public sealed class LowAir
{
    private LowAir(ValleyPools pools, LowAirSettings settings, uint[] cells, uint noiseSeed, AtmosphereModel air)
    {
        Pools = pools;
        Settings = settings;
        Cells = cells;
        NoiseSeed = noiseSeed;
        Air = air;
        Level0 = pools.Levels[0];
        Level1 = pools.Levels.Count > 1 ? pools.Levels[1] : null;
        Level1Offset = Level0.Size * Level0.Size;
        HazeUnitPerExtinction = air.AerosolScaleHeightMetres / Math.Max(air.HazeUnitOpticalDepth, 1e-12);
        float highest = float.MinValue, lowest = float.MaxValue, hazeMax = 0f, water = 0f;
        foreach (PoolCell[] level in pools.Cells)
        {
            foreach (PoolCell c in level)
            {
                if (!(c.DepthMetres > 0.0))
                    continue;
                highest = Math.Max(highest, (float)(c.FloorMetres + Math.Max(c.DepthMetres, c.MistMetres)));
                lowest = Math.Min(lowest, (float)c.FloorMetres);
                hazeMax = Math.Max(hazeMax, (float)(Math.Max(c.HazeFloor550, c.HazeTop550) * HazeUnitPerExtinction));
                water = Math.Max(water, (float)c.MistWater);
            }
        }
        SlabTop = highest + (float)(settings.WispAmplitudeMetres + 8.0 * settings.HazeSoftnessMetres);
        Ceilings = CeilingsOf(pools, settings);
        SlabBottom = lowest;
        HazeMax = 1.01f * hazeMax;
        MistMax = 1.01f * MistExtinction(water, (float)ValleyPools.MistWaterLimit);
    }

    /// <summary>The pools.</summary>
    public ValleyPools Pools { get; }

    /// <summary>How the renderer takes them.</summary>
    public LowAirSettings Settings { get; }

    /// <summary>The cells as the GPU reads them, four words a sample, the world's level then the far field's (<see cref="ValleyPools.Packed"/>).</summary>
    public uint[] Cells { get; }

    /// <summary>The 32-bit seed of the noise.</summary>
    public uint NoiseSeed { get; }

    /// <summary>The air whose haze unit the pools' haze is counted in.</summary>
    public AtmosphereModel Air { get; }

    /// <summary>The world's level.</summary>
    public PoolLevel Level0 { get; }

    /// <summary>The far field's level; null for none.</summary>
    public PoolLevel? Level1 { get; }

    /// <summary>The far field's first cell, in cells.</summary>
    public int Level1Offset { get; }

    /// <summary>The haze's unit per unit of extinction at 550 nm (the pools' haze is counted in the haze's unit, as the air's own).</summary>
    public double HazeUnitPerExtinction { get; }

    /// <summary>The highest any pool's haze or mist reaches, m above sea level (its top, the noise's reach and the soft top's).</summary>
    public float SlabTop { get; }

    /// <summary>The lowest floor, m above sea level.</summary>
    public float SlabBottom { get; }

    /// <summary>A bound on the pools' haze anywhere, in the haze's unit.</summary>
    public float HazeMax { get; }

    /// <summary>A bound on the mist's extinction anywhere, per metre.</summary>
    public float MistMax { get; }

    /// <summary>The low air of <paramref name="pools"/> in <paramref name="air"/>, its noise seeded from the world's <paramref name="worldSeed"/>; null without pools.</summary>
    public static LowAir? For(ValleyPools pools, AtmosphereModel air, ulong worldSeed, LowAirSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(pools);
        ArgumentNullException.ThrowIfNull(air);
        if (!pools.Any)
            return null;
        double unit = air.AerosolScaleHeightMetres / Math.Max(air.HazeUnitOpticalDepth, 1e-12);
        return new LowAir(pools, settings ?? LowAirSettings.Default, pools.Packed(unit), SeedOf(worldSeed), air);
    }

    /// <summary>The flat layer the atmosphere's tables carry for <paramref name="pools"/> (<see cref="LowAirSettings.TablesLayerShare"/> of their average): (top, extinction at 550 nm), none without haze.</summary>
    public static (double TopMetres, double Extinction550) TablesLayer(ValleyPools pools, LowAirSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(pools);
        (double top, double extinction) = pools.AverageLayer() ?? (0.0, 0.0);
        return (top, extinction * (settings ?? LowAirSettings.Default).TablesLayerShare);
    }

    /// <summary>
    /// How far under its floor a pool reaches, m: nothing lies in the rock (the march never looks there; the reference, which ignores
    /// the terrain inside the air, would otherwise find a pool hundreds of metres deep under every valley).
    /// </summary>
    public const float FloorDepthMetres = 25f;

    /// <summary>The noise's 32-bit seed for a world whose seed is <paramref name="worldSeed"/>.</summary>
    public static uint SeedOf(ulong worldSeed) => (uint)(StableHash.Mix(worldSeed ^ 0x4D157A11u) >> 32);

    /// <summary>The sunlight volume's samples a side over <paramref name="level"/>.</summary>
    public int VolumeSize(PoolLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        return (level.Size - 1) / Settings.VolumeRatio + 1;
    }

    /// <summary>The voxels of the volume over <paramref name="level"/>.</summary>
    public int VolumeVoxels(PoolLevel level) => VolumeSize(level) * VolumeSize(level) * Settings.VolumeLayers;

    /// <summary>The GPU buffer's first element of the volume over level <paramref name="level"/> (0 or 1), in four-word elements: after the cells.</summary>
    public int VolumeOffset(int level)
    {
        int cells = Cells.Length / 4;
        if (level == 0)
            return cells;
        return cells + (VolumeVoxels(Level0) + 1) / 2;
    }

    /// <summary>The GPU buffer's first element of the ceilings (<see cref="Ceilings"/>, four to an element): after both volumes.</summary>
    public int CeilingOffset => VolumeOffset(1) + (Level1 is null ? 0 : (VolumeVoxels(Level1) + 1) / 2);

    /// <summary>The GPU buffer's four-word elements: the cells, then both volumes (two voxels an element), then the ceilings.</summary>
    public int BufferElements => CeilingOffset + (Ceilings.Length + 3) / 4;

    /// <summary>
    /// The ceiling map of both levels in turn (the world's, then the far field's), every <see cref="LowAirSettings.CeilingBlock"/>-th cell
    /// a block: the highest any pool reaches over the block and one block around it, m above sea level (its top or its mist's, the
    /// noise's reach and the soft top's); the floor where none does. A point's block is the one its cell lies in, so a step of up to a
    /// block from it stays under its bound (the shader's <c>CeilingAt</c>).
    /// </summary>
    public float[] Ceilings { get; }

    /// <summary>The ceiling map's blocks a side over <paramref name="level"/>.</summary>
    public int CeilingSize(PoolLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        return (level.Size - 1) / Settings.CeilingBlock + 1;
    }

    private static float[] CeilingsOf(ValleyPools pools, LowAirSettings settings)
    {
        int block = settings.CeilingBlock;
        float reach = (float)(settings.WispAmplitudeMetres + 8.0 * settings.HazeSoftnessMetres);
        var all = new List<float>();
        for (int l = 0; l < pools.Levels.Count; l++)
        {
            PoolLevel level = pools.Levels[l];
            PoolCell[] cells = pools.Cells[l];
            int size = level.Size, blocks = (size - 1) / block + 1;
            var ceilings = new float[blocks * blocks];
            Parallel.For(0, blocks, bj =>
            {
                for (int bi = 0; bi < blocks; bi++)
                {
                    float top = float.MinValue;
                    for (int j = Math.Max((bj - 1) * block, 0); j <= Math.Min((bj + 2) * block, size - 1); j++)
                    {
                        for (int i = Math.Max((bi - 1) * block, 0); i <= Math.Min((bi + 2) * block, size - 1); i++)
                        {
                            PoolCell c = cells[j * size + i];
                            // Every cell's reach: a point between a pool and a cell without one blends their floors and depths.
                            top = Math.Max(top, (float)(c.FloorMetres + Math.Max(c.DepthMetres, c.MistMetres)) + reach);
                        }
                    }
                    ceilings[bj * blocks + bi] = top;
                }
            });
            all.AddRange(ceilings);
        }
        return [.. all];
    }

    /// <summary>
    /// The ceiling over world (<paramref name="x"/>, <paramref name="z"/>) (the shader's <c>CeilingAt</c>): its block's on the world's
    /// level, the higher of both levels' where the world's blends into the far field's, the far field's beyond; below sea level outside both.
    /// </summary>
    public float CeilingAt(float x, float z)
    {
        bool on0 = OnLevel(Level0, x, z, out float u0, out float v0);
        float u1 = 0f, v1 = 0f;
        bool on1 = Level1 is not null && OnLevel(Level1, x, z, out u1, out v1);
        float Block(int level, float u, float v)
        {
            PoolLevel grid = level == 0 ? Level0 : Level1!;
            int blocks = CeilingSize(grid);
            int offset = level == 0 ? 0 : CeilingSize(Level0) * CeilingSize(Level0);
            int bi = Math.Min((int)(u / Settings.CeilingBlock), blocks - 1), bj = Math.Min((int)(v / Settings.CeilingBlock), blocks - 1);
            return Ceilings[offset + bj * blocks + bi];
        }
        if (!on0)
            return on1 ? Block(1, u1, v1) : -1e9f;
        float fine = Block(0, u0, v0);
        if (!on1)
            return fine;
        float edgeU = Math.Min(u0, Level0.Size - 1 - u0) * (float)Level0.CellMetres;
        float edgeV = Math.Min(v0, Level0.Size - 1 - v0) * (float)Level0.CellMetres;
        return Math.Min(edgeU, edgeV) >= (float)Settings.BlendMetres ? fine : Math.Max(fine, Block(1, u1, v1));
    }

    /// <summary>What a render of this low air depends on, by value: the cells' bits, the settings, the noise's seed and the air.</summary>
    public LowAirKey Key => _key ??= new LowAirKey(CellsHash(), Settings, NoiseSeed, Air);

    private LowAirKey? _key;

    private ulong CellsHash()
    {
        ulong hash = StableHash.FnvStart;
        Span<byte> buffer = stackalloc byte[4];
        foreach (uint word in Cells)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buffer, word);
            hash = StableHash.Fnv1a(hash, buffer);
        }
        return StableHash.Combine(hash, (ulong)Level0.Size * 31 + (ulong)(Level1?.Size ?? 0));
    }

    // ── The noise ─────────────────────────────────────────────────────────────────────────────────────

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private static uint Hash(uint v) => MaterialNoise.Hash(v);

    private static float Corner2(uint seed, int ix, int iy, float dx, float dy)
    {
        uint g = Hash(seed ^ Hash(unchecked((uint)ix) ^ Hash(unchecked((uint)iy)))) >> 29;
        return Gradient2(g, dx, dy);
    }

    /// <summary>
    /// The 2D gradient <paramref name="g"/> (0 to 7: 22.5° + 45°·g) dotted with (<paramref name="dx"/>, <paramref name="dy"/>), by
    /// arithmetic rather than a table (the shader's threads would read a table one index at a time): (cos, sin) or, for 1, 2, 5 and 6,
    /// (sin, cos), with x negative for 2 to 5 and y for 4 to 7.
    /// </summary>
    public static float Gradient2(uint g, float dx, float dy)
    {
        const float C = 0.9238795f, S = 0.3826834f;
        bool swap = (((g + 1u) >> 1) & 1u) != 0u;
        float gx = swap ? S : C, gy = swap ? C : S;
        if (g - 2u < 4u)
            gx = -gx;
        if (g >= 4u)
            gy = -gy;
        return gx * dx + gy * dy;
    }

    /// <summary>
    /// Perlin's (2002) gradient <paramref name="h"/> (0 to 15: the twelve edges of a cube, four twice) dotted with (<paramref name="x"/>,
    /// <paramref name="y"/>, <paramref name="z"/>), by his arithmetic.
    /// </summary>
    public static float Gradient3(uint h, float x, float y, float z)
    {
        float u = h < 8u ? x : y;
        float v = h < 4u ? y : h == 12u || h == 14u ? x : z;
        return ((h & 1u) == 0u ? u : -u) + ((h & 2u) == 0u ? v : -v);
    }

    /// <summary>2D gradient noise in lattice units, roughly in [−1, 1] (the shader's <c>LowNoise2</c>).</summary>
    public static float Noise2(uint seed, float x, float y)
    {
        float fx = MathF.Floor(x), fy = MathF.Floor(y);
        int ix = (int)fx, iy = (int)fy;
        float dx = x - fx, dy = y - fy;
        float n00 = Corner2(seed, ix, iy, dx, dy);
        float n10 = Corner2(seed, ix + 1, iy, dx - 1f, dy);
        float n01 = Corner2(seed, ix, iy + 1, dx, dy - 1f);
        float n11 = Corner2(seed, ix + 1, iy + 1, dx - 1f, dy - 1f);
        float u = Fade(dx), v = Fade(dy);
        float a = n00 + (n10 - n00) * u;
        float b = n01 + (n11 - n01) * u;
        return (a + (b - a) * v) * 1.4142135f;
    }

    private static float Corner3(uint seed, int ix, int iy, int iz, float dx, float dy, float dz)
    {
        uint h = Hash(seed ^ Hash(unchecked((uint)ix) ^ Hash(unchecked((uint)iy) ^ Hash(unchecked((uint)iz))))) >> 28;
        return Gradient3(h, dx, dy, dz);
    }

    /// <summary>3D gradient noise in lattice units, clamped to [−1, 1] (the shader's <c>LowNoise3</c>): Perlin's (2002) edge gradients.</summary>
    public static float Noise3(uint seed, float x, float y, float z)
    {
        float fx = MathF.Floor(x), fy = MathF.Floor(y), fz = MathF.Floor(z);
        int ix = (int)fx, iy = (int)fy, iz = (int)fz;
        float dx = x - fx, dy = y - fy, dz = z - fz;
        float n000 = Corner3(seed, ix, iy, iz, dx, dy, dz);
        float n100 = Corner3(seed, ix + 1, iy, iz, dx - 1f, dy, dz);
        float n010 = Corner3(seed, ix, iy + 1, iz, dx, dy - 1f, dz);
        float n110 = Corner3(seed, ix + 1, iy + 1, iz, dx - 1f, dy - 1f, dz);
        float n001 = Corner3(seed, ix, iy, iz + 1, dx, dy, dz - 1f);
        float n101 = Corner3(seed, ix + 1, iy, iz + 1, dx - 1f, dy, dz - 1f);
        float n011 = Corner3(seed, ix, iy + 1, iz + 1, dx, dy - 1f, dz - 1f);
        float n111 = Corner3(seed, ix + 1, iy + 1, iz + 1, dx - 1f, dy - 1f, dz - 1f);
        float u = Fade(dx), v = Fade(dy), w = Fade(dz);
        float a0 = n000 + (n100 - n000) * u, a1 = n010 + (n110 - n010) * u;
        float b0 = n001 + (n101 - n001) * u, b1 = n011 + (n111 - n011) * u;
        float a = a0 + (a1 - a0) * v, b = b0 + (b1 - b0) * v;
        return Math.Clamp(a + (b - a) * w, -1f, 1f);
    }

    // ── The pools ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A packed cell (<see cref="ValleyPools.Pack"/>) as the shader reads it.</summary>
    public static PoolSample Unpack(ReadOnlySpan<uint> cell) => new(
        BitConverter.UInt32BitsToSingle(cell[0]),
        (float)BitConverter.UInt16BitsToHalf((ushort)(cell[1] & 0xFFFF)), (float)BitConverter.UInt16BitsToHalf((ushort)(cell[1] >> 16)),
        (float)BitConverter.UInt16BitsToHalf((ushort)(cell[2] & 0xFFFF)), (float)BitConverter.UInt16BitsToHalf((ushort)(cell[2] >> 16)),
        (float)BitConverter.UInt16BitsToHalf((ushort)(cell[3] & 0xFFFF)), (float)BitConverter.UInt16BitsToHalf((ushort)(cell[3] >> 16)));

    private static bool OnLevel(PoolLevel level, float x, float z, out float u, out float v)
    {
        u = (x - (float)level.OriginX) / (float)level.CellMetres;
        v = (z - (float)level.OriginZ) / (float)level.CellMetres;
        return u >= 0f && v >= 0f && u <= level.Size - 1 && v <= level.Size - 1;
    }

    private PoolSample LevelSample(PoolLevel level, int offset, float u, float v)
    {
        int size = level.Size;
        int i = Math.Min((int)u, size - 2), j = Math.Min((int)v, size - 2);
        float fu = u - i, fv = v - j;
        int k = offset + j * size + i;
        PoolSample At(int c) => Unpack(Cells.AsSpan(c * 4, 4));
        PoolSample bottom = PoolSample.Lerp(At(k), At(k + 1), fu);
        PoolSample top = PoolSample.Lerp(At(k + size), At(k + size + 1), fu);
        return PoolSample.Lerp(bottom, top, fv);
    }

    /// <summary>
    /// The pool over world (<paramref name="x"/>, <paramref name="z"/>) (the shader's <c>PoolAt</c>): the world's level, blending into
    /// the far field's over the world's outer <see cref="LowAirSettings.BlendMetres"/>; the far field's beyond it; none outside both.
    /// </summary>
    public PoolSample PoolAt(float x, float z, out bool any)
    {
        bool on0 = OnLevel(Level0, x, z, out float u0, out float v0);
        float u1 = 0f, v1 = 0f;
        bool on1 = Level1 is not null && OnLevel(Level1, x, z, out u1, out v1);
        any = on0 || on1;
        if (!any)
            return default;
        if (!on0)
            return LevelSample(Level1!, Level1Offset, u1, v1);
        PoolSample fine = LevelSample(Level0, 0, u0, v0);
        if (!on1)
            return fine;
        float edgeU = Math.Min(u0, Level0.Size - 1 - u0) * (float)Level0.CellMetres;
        float edgeV = Math.Min(v0, Level0.Size - 1 - v0) * (float)Level0.CellMetres;
        float t = Math.Clamp(Math.Min(edgeU, edgeV) / (float)Settings.BlendMetres, 0f, 1f);
        return t >= 1f ? fine : PoolSample.Lerp(LevelSample(Level1!, Level1Offset, u1, v1), fine, t);
    }

    /// <summary>The mist's extinction at liquid water <paramref name="lwc"/>, g/m³, capped at <paramref name="limit"/>: the shader's <c>MistExtinction</c>.</summary>
    public static float MistExtinction(float lwc, float limit) =>
        lwc > 0f ? (float)ValleyPools.KunkelCoefficient * MathF.Pow(Math.Min(lwc, limit), (float)ValleyPools.KunkelExponent) : 0f;

    /// <summary>
    /// The pools' haze (in the haze's unit) and mist (extinction per metre) at <paramref name="altitude"/> m above sea level over world
    /// (<paramref name="x"/>, <paramref name="z"/>) in pool <paramref name="s"/> (the shader's <c>PoolDensity</c>).
    /// </summary>
    public (float Haze, float Mist) Density(PoolSample s, float x, float z, float altitude)
    {
        if (!(s.Depth > 0f))
            return (0f, 0f);
        float u = altitude - s.Floor;
        float amplitude = (float)Settings.WispAmplitudeMetres, softness = (float)Settings.HazeSoftnessMetres;
        if (u > s.Depth + amplitude + 8f * softness || u < -FloorDepthMetres)
            return (0f, 0f);
        // Deep in a pool of haze alone the wisps would move a top 8 softnesses away: its logistic stands at 1 within 3·10⁻⁴.
        float wisp = s.Mist > 0f || u > s.Depth - amplitude - 8f * softness
            ? Noise3(NoiseSeed, x / (float)Settings.WispScaleMetres, altitude / (float)Settings.WispVerticalMetres, z / (float)Settings.WispScaleMetres)
            : 0f;
        float top = s.Depth + amplitude * wisp;
        float profile = s.HazeFloor + (s.HazeTop - s.HazeFloor) * Math.Clamp(u / s.Depth, 0f, 1f);
        float haze = profile / (1f + MathF.Exp((u - top) / softness));
        float mist = 0f;
        if (s.Mist > 0f)
        {
            float patch = Noise2(Hash(NoiseSeed ^ 0x9E3779B9u), x / (float)Settings.PatchScaleMetres, z / (float)Settings.PatchScaleMetres);
            float fray = Fray(s);
            float mistTop = Math.Min(s.Mist * (1f + (float)Settings.PatchStrength * fray * patch), s.Depth) + amplitude * fray * wisp; // never above its pool
            float mistSoftness = (float)Settings.MistSoftnessMetres;
            if (u < mistTop + 8f * mistSoftness)
                mist = MistExtinction(s.Water, (float)ValleyPools.MistWaterLimit) / (1f + MathF.Exp((u - mistTop) / mistSoftness));
        }
        return (haze, mist);
    }

    /// <summary>
    /// How much of the wisps and patches a mist takes (the shader's <c>MistFray</c>): the larger of its edge's share (1 where the ground
    /// rises to its top, 0 <see cref="LowAirSettings.FrayMetres"/> under it) and its weakness (1 less its water over the cap).
    /// </summary>
    public float Fray(PoolSample s)
    {
        float edge = 1f - Math.Clamp(s.Cover / (float)Settings.FrayMetres, 0f, 1f);
        float weak = 1f - Math.Clamp(s.Water / (float)ValleyPools.MistWaterLimit, 0f, 1f);
        return Math.Max(edge, weak);
    }

    /// <summary>The density at a point, the pool looked up (the shader's <c>LowDensityAt</c>).</summary>
    public (float Haze, float Mist) DensityAt(float x, float z, float altitude) => Density(PoolAt(x, z, out _), x, z, altitude);

    /// <summary>
    /// The sun's direction at world (<paramref name="x"/>, <paramref name="height"/> above the ground sphere, <paramref name="z"/>) in its
    /// own level frame (world x, up, z), the frame bent around <paramref name="frame"/>'s tangent point (the shader's <c>LocalToSun</c>):
    /// the sun's bearing in the world, and its height against the point's own vertical, which the Earth's curve tilts by s/R.
    /// </summary>
    public static (float X, float Y, float Z) LocalToSun(EarthFrame frame, double x, double height, double z, (float X, float Y, float Z) toSun)
    {
        Vector3D up = frame.LocalUp(frame.ToFrame(x, height, z));
        float muS = (float)(toSun.X * up.X + toSun.Y * up.Y + toSun.Z * up.Z);
        float length = MathF.Sqrt(toSun.X * toSun.X + toSun.Z * toSun.Z);
        (float bx, float bz) = length > 1e-6f ? (toSun.X / length, toSun.Z / length) : (0f, 0f);
        float cosE = MathF.Sqrt(Math.Max(1f - muS * muS, 0f));
        return (bx * cosE, muS, bz * cosE);
    }

    /// <summary>
    /// Whether the sun reaches world point (<paramref name="x"/>, <paramref name="altitude"/>, <paramref name="z"/>) over
    /// <paramref name="surface"/> standing <paramref name="ground"/> above sea level, with the sun's bearing (<paramref name="bx"/>,
    /// <paramref name="bz"/>, a unit vector in world x and z) and height <paramref name="sinE"/>: the sunlight volume's terrain shadow
    /// (the shader's, on the render's mesh, in <c>CSSunlightVolume</c>) on a flat world. From the ground above the point when it lies under
    /// it, as the shader's; marched in steps of a quarter cell to the grid's edge or above its highest point.
    /// </summary>
    public static bool TerrainLit(HeightField surface, double ground, double x, double altitude, double z, double bx, double bz, double sinE)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (!(sinE > 0.0))
            return false;
        double h = altitude - ground;
        double below = surface.HeightAt(x, z);
        if (h < below)
            h = below + 0.5;
        double cosE = Math.Sqrt(1.0 - sinE * sinE);
        double step = surface.CellSize / 4.0;
        double top = surface.MaxHeight;
        double half = 0.5 * surface.Extent;
        for (double t = step; ; t += step)
        {
            double px = x + bx * cosE * t, pz = z + bz * cosE * t, py = h + sinE * t;
            if (py > top || Math.Abs(px) > half || Math.Abs(pz) > half)
                return true;
            if (surface.HeightAt(px, pz) > py)
                return false;
        }
    }

    /// <summary>The span of a pool's sunlight volume column above its floor, m (the shader's <c>LowVolumeSpan</c>).</summary>
    public float VolumeSpan(PoolSample s) => Math.Max(s.Depth, s.Mist) + (float)Settings.VolumeMarginMetres;

    /// <summary>
    /// The height above its floor of a volume column's layer <paramref name="layer"/> (the shader's <c>LowLayerHeight</c>): the first
    /// <see cref="LowAirSettings.VolumePoolLayers"/> over the pool (<see cref="VolumeSpan"/>), the rest from there up to the pools'
    /// highest reach (<see cref="SlabTop"/>).
    /// </summary>
    public float LayerHeight(PoolSample s, int layer)
    {
        float pool = VolumeSpan(s), top = Math.Max(SlabTop - s.Floor, pool + 1f);
        int np = Settings.VolumePoolLayers - 1;
        return layer <= np ? pool * layer / np : pool + (top - pool) * (layer - np) / (Settings.VolumeLayers - Settings.VolumePoolLayers);
    }

    /// <summary>
    /// The pools' columns from voxel (<paramref name="i"/>, <paramref name="j"/>, <paramref name="layer"/>) of the volume over level
    /// <paramref name="level"/> (0 or 1), the shader's <c>CSSunlightVolume</c> without the terrain: along <paramref name="toSun"/> (a unit
    /// direction in world x, up, z) to the pools' top or <see cref="LowAirSettings.SunReachMetres"/>, the haze column less the tables'
    /// layer's (<paramref name="layer"/> of <paramref name="air"/>, in the haze's unit) and the mist's optical depth, midpoints of
    /// steps growing quadratically from the voxel; straight up through its column, the mist's optical depth. Returns the voxel's place
    /// too.
    /// </summary>
    public (float HazeDelta, float MistDepth, float MistUp, float X, float Z, float Altitude) VoxelColumns(int level, int i, int j, int layer, (float X, float Y, float Z) toSun)
    {
        PoolLevel grid = level == 0 ? Level0 : Level1 ?? throw new ArgumentOutOfRangeException(nameof(level), level, "No far level.");
        float cell = (float)grid.CellMetres * Settings.VolumeRatio;
        float x = (float)grid.OriginX + i * cell, z = (float)grid.OriginZ + j * cell;
        PoolSample s = PoolAt(x, z, out _);
        float altitude = s.Floor + LayerHeight(s, layer);
        float haze = 0f, mist = 0f;
        if (toSun.Y > 0f)
        {
            float reach = Math.Min((float)Settings.SunReachMetres, Math.Max(SlabTop - altitude, 0f) / toSun.Y);
            int n = Settings.SunSteps;
            for (int k = 0; k < n; k++)
            {
                float a = reach * (k / (float)n) * (k / (float)n), b = reach * ((k + 1) / (float)n) * ((k + 1) / (float)n);
                float t = 0.5f * (a + b), dt = b - a;
                float px = x + toSun.X * t, pz = z + toSun.Z * t, py = altitude + toSun.Y * t;
                (float h, float m) = DensityAt(px, pz, py);
                haze += (h - (float)Air.ValleyHaze(py)) * dt;
                mist += m * dt;
            }
        }
        float up = 0f;
        float span = s.Floor + VolumeSpan(s) - altitude;
        if (span > 0f)
        {
            float dz = span / Settings.UpSteps;
            for (int k = 0; k < Settings.UpSteps; k++)
                up += Density(s, x, z, altitude + (k + 0.5f) * dz).Mist * dz;
        }
        return (haze, mist, up, x, z, altitude);
    }
}
