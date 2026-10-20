namespace Vista.World;

/// <summary>
/// The state of an erosion run, laid out as the GPU's buffers are (vista-plan §10 M1.7), so a test or the self-test can feed
/// both sides the same numbers: per cell, the ground as (bedrock, loose) heights, the water depth, the suspended sediment,
/// the outflow to the four neighbours (−x, +x, −z, +z) in m³/s, the thermal move of the last step, and the flow sums.
/// Cell <c>(i, j)</c> is index <c>j·Size + i</c>, as in <see cref="HeightField"/>.
/// </summary>
public sealed class ErosionState
{
    /// <summary>A dry, clean state on <paramref name="ground"/>: every sample bedrock, nothing loose.</summary>
    public ErosionState(HeightField ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        Size = ground.Size;
        CellSize = ground.CellSize;
        int cells = Size * Size;
        Terrain = new float[cells * 2];
        ReadOnlySpan<float> heights = ground.Heights;
        for (int c = 0; c < cells; c++)
            Terrain[c * 2] = heights[c];
        Water = new float[cells];
        Sediment = new float[cells];
        Flux = new float[cells * 4];
        Thermal = new float[cells * 4];
        Flow = new float[cells * 2];
    }

    /// <summary>
    /// Floats per cell the GPU keeps: two of each double-buffered layer (ground ×2, water, sediment), the flux and thermal
    /// moves (×4 each) and the flow sums (×2).
    /// </summary>
    public const int FloatsPerCell = 2 * 2 + 2 + 2 + 4 + 4 + 2;

    /// <summary>Samples per side.</summary>
    public int Size { get; }

    /// <summary>Cell size in metres.</summary>
    public double CellSize { get; }

    /// <summary>Per cell (bedrock height, loose depth) in metres; the ground surface is their sum.</summary>
    public float[] Terrain { get; }

    /// <summary>Water depth per cell in metres.</summary>
    public float[] Water { get; }

    /// <summary>Suspended sediment per cell, as metres of material over the cell.</summary>
    public float[] Sediment { get; }

    /// <summary>Outflow per cell to −x, +x, −z, +z in m³/s.</summary>
    public float[] Flux { get; }

    /// <summary>Per cell, the last thermal step's (loose moved, rock moved, sum of the loose excess over the neighbours, 0).</summary>
    public float[] Thermal { get; }

    /// <summary>Per cell, the sums of (outflow in m³/s, water depth in m) over the steps that averaged.</summary>
    public float[] Flow { get; }

    /// <summary>Steps added to <see cref="Flow"/>.</summary>
    public int FlowSteps { get; set; }

    /// <summary>Steps run.</summary>
    public int Steps { get; set; }

    /// <summary>A deep copy, for running one pass on both sides from the same state.</summary>
    public ErosionState Clone()
    {
        var copy = new ErosionState(Size, CellSize) { FlowSteps = FlowSteps, Steps = Steps };
        Terrain.CopyTo(copy.Terrain, 0);
        Water.CopyTo(copy.Water, 0);
        Sediment.CopyTo(copy.Sediment, 0);
        Flux.CopyTo(copy.Flux, 0);
        Thermal.CopyTo(copy.Thermal, 0);
        Flow.CopyTo(copy.Flow, 0);
        return copy;
    }

    private ErosionState(int size, double cellSize)
    {
        Size = size;
        CellSize = cellSize;
        int cells = size * size;
        Terrain = new float[cells * 2];
        Water = new float[cells];
        Sediment = new float[cells];
        Flux = new float[cells * 4];
        Thermal = new float[cells * 4];
        Flow = new float[cells * 2];
    }

    /// <summary>Totals in double: ground (bedrock + loose), bedrock, loose, water and suspended sediment, in metres summed over the cells.</summary>
    public (double Ground, double Rock, double Loose, double Water, double Sediment) Totals()
    {
        double rock = 0, loose = 0, water = 0, sediment = 0;
        for (int c = 0; c < Water.Length; c++)
        {
            rock += Terrain[c * 2];
            loose += Terrain[c * 2 + 1];
            water += Water[c];
            sediment += Sediment[c];
        }
        return (rock + loose, rock, loose, water, sediment);
    }
}

/// <summary>
/// The C# reference of the GPU erosion (vista-plan §9: a small reference and a parity test for the shared math). One step is
/// five gather-only passes, each cell writing only itself from the previous buffers, so the result cannot depend on the order
/// cells run in — which is what makes the GPU's erosion deterministic without atomics:
/// <list type="number">
///   <item><see cref="Flux"/>: rain, then the virtual pipes' outflow to the four neighbours, accelerated by the head difference
///   and scaled down so no cell sends more water than it holds (Mei, Decaudin and Hu 2007). Past the grid's edge the ground
///   continues at the edge's slope, dry, so water leaves where the land falls away.</item>
///   <item><see cref="Water"/>: the water after this step's inflow and outflow; the discharge per metre of width from the net
///   flux through the cell; the sediment capacity <c>K·sin α·√q</c> (stream power with the usual m/n of ½: steep slopes and big
///   rivers cut, sheet flow on a ridge barely does); erosion of the spare capacity (loose material first, then rock at its
///   erodibility) or deposition of the excess as loose material; evaporation; the flow sums.</item>
///   <item><see cref="Transport"/>: suspended sediment moves with the water — each cell sends the fraction of its sediment
///   that its water sent — so sediment is conserved, unlike semi-Lagrangian advection.</item>
///   <item><see cref="ThermalOut"/>: where the ground is steeper than the loose talus angle, loose material slides; where it is
///   steeper than the rock talus angle, rock collapses. Each cell works out how much leaves it.</item>
///   <item><see cref="ThermalApply"/>: each cell loses what it sent and gains, as loose material, its share of what its
///   neighbours sent, split in proportion to how far each neighbour exceeds the talus toward it.</item>
/// </list>
/// Amounts that move between layers are made exactly representable first (<c>a − (a − x)</c>), so a transfer smaller than the
/// rounding of a 2 km height cannot create or destroy material. The arithmetic is <see cref="float"/> in the shader's order.
/// </summary>
public static class ErosionReference
{
    /// <summary>Runs <paramref name="settings"/> on <paramref name="ground"/> and returns the eroded world.</summary>
    public static ErosionResult Erode(HeightField ground, ErosionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(settings);
        var state = new ErosionState(ground);
        Run(state, settings, settings.Iterations);
        return ErosionResult.From(state);
    }

    /// <summary>Runs <paramref name="steps"/> more steps of <paramref name="settings"/> on <paramref name="state"/>.</summary>
    public static void Run(ErosionState state, ErosionSettings settings, int steps)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);
        IReadOnlyList<string> errors = settings.Validate();
        if (errors.Count > 0)
            throw new ArgumentException("Invalid erosion settings: " + string.Join(" ", errors), nameof(settings));
        int cells = state.Size * state.Size;
        var terrainB = new float[cells * 2];
        var waterB = new float[cells];
        var sedimentB = new float[cells];
        ErosionConstants k = ErosionConstants.For(settings, state.Size, state.CellSize);
        for (int s = 0; s < steps; s++)
        {
            k.Accumulate = state.Steps >= settings.AverageFrom ? 1u : 0u;
            Flux(k, state.Terrain, state.Water, state.Flux);
            Water(k, state.Terrain, state.Water, state.Sediment, state.Flux, terrainB, waterB, sedimentB, state.Flow);
            Transport(k, state.Water, state.Flux, sedimentB, state.Sediment);
            waterB.CopyTo(state.Water, 0);
            ThermalOut(k, terrainB, state.Thermal);
            ThermalApply(k, terrainB, state.Thermal, state.Terrain);
            if (k.Accumulate != 0)
                state.FlowSteps++;
            state.Steps++;
        }
    }

    /// <summary>Pass 1: rain and the outflow to each neighbour, in place in <paramref name="flux"/>.</summary>
    public static void Flux(ErosionConstants k, float[] terrain, float[] water, float[] flux)
    {
        int n = (int)k.Size;
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
                FluxCell(k, n, i, j, terrain, water, flux);
        });
    }

    /// <summary>Pass 2: water, speed, erosion or deposition, evaporation and the flow sums, from the A buffers into the B buffers.</summary>
    public static void Water(ErosionConstants k, float[] terrain, float[] water, float[] sediment, float[] flux,
        float[] terrainOut, float[] waterOut, float[] sedimentOut, float[] flow)
    {
        int n = (int)k.Size;
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
                WaterCell(k, n, i, j, terrain, water, sediment, flux, terrainOut, waterOut, sedimentOut, flow);
        });
    }

    /// <summary>Pass 3: the sediment after pass 2 (<paramref name="sediment"/>) moves with this step's water.</summary>
    public static void Transport(ErosionConstants k, float[] water, float[] flux, float[] sediment, float[] sedimentOut)
    {
        int n = (int)k.Size;
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
                TransportCell(k, n, i, j, water, flux, sediment, sedimentOut);
        });
    }

    /// <summary>Pass 4: how much loose material and rock leaves each cell.</summary>
    public static void ThermalOut(ErosionConstants k, float[] terrain, float[] thermal)
    {
        int n = (int)k.Size;
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
                ThermalOutCell(k, n, i, j, terrain, thermal);
        });
    }

    /// <summary>Pass 5: each cell loses what it sent and gains its share of its neighbours' moves.</summary>
    public static void ThermalApply(ErosionConstants k, float[] terrain, float[] thermal, float[] terrainOut)
    {
        int n = (int)k.Size;
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
                ThermalApplyCell(k, n, i, j, terrain, thermal, terrainOut);
        });
    }

    // ── The cells (mirrored line for line in Shaders/VistaErosion.hlsl) ─────────────────────────────────────────────

    // The eight neighbours, in the order both sides sum them; the first four are the edge neighbours' indices in Flux.
    private static readonly int[] Di = [-1, 0, 1, -1, 1, -1, 0, 1];
    private static readonly int[] Dj = [-1, -1, -1, 0, 0, 1, 1, 1];

    private static float Ground(float[] terrain, int c) => terrain[c * 2] + terrain[c * 2 + 1];

    private static void FluxCell(ErosionConstants k, int n, int i, int j, float[] terrain, float[] water, float[] flux)
    {
        int c = j * n + i;
        float ground = Ground(terrain, c);
        float surface = ground + (water[c] + k.Rain);
        // Past the edge the ground continues at the edge's slope and is dry.
        float xm = i > 0 ? Ground(terrain, c - 1) + (water[c - 1] + k.Rain) : ground + (ground - Ground(terrain, c + 1));
        float xp = i < n - 1 ? Ground(terrain, c + 1) + (water[c + 1] + k.Rain) : ground + (ground - Ground(terrain, c - 1));
        float zm = j > 0 ? Ground(terrain, c - n) + (water[c - n] + k.Rain) : ground + (ground - Ground(terrain, c + n));
        float zp = j < n - 1 ? Ground(terrain, c + n) + (water[c + n] + k.Rain) : ground + (ground - Ground(terrain, c - n));
        float fxm = MathF.Max(0f, flux[c * 4] * k.FluxKept + k.FluxPerHead * (surface - xm));
        float fxp = MathF.Max(0f, flux[c * 4 + 1] * k.FluxKept + k.FluxPerHead * (surface - xp));
        float fzm = MathF.Max(0f, flux[c * 4 + 2] * k.FluxKept + k.FluxPerHead * (surface - zm));
        float fzp = MathF.Max(0f, flux[c * 4 + 3] * k.FluxKept + k.FluxPerHead * (surface - zp));
        float leaving = ((fxm + fxp) + (fzm + fzp)) * k.TimeStep;
        float volume = (water[c] + k.Rain) * k.CellArea;
        float scale = leaving > volume ? volume / leaving : 1f;
        flux[c * 4] = fxm * scale;
        flux[c * 4 + 1] = fxp * scale;
        flux[c * 4 + 2] = fzm * scale;
        flux[c * 4 + 3] = fzp * scale;
    }

    private static void WaterCell(ErosionConstants k, int n, int i, int j, float[] terrain, float[] water, float[] sediment,
        float[] flux, float[] terrainOut, float[] waterOut, float[] sedimentOut, float[] flow)
    {
        int c = j * n + i;
        float rock = terrain[c * 2];
        float loose = terrain[c * 2 + 1];
        float ground = rock + loose;
        float before = water[c] + k.Rain;

        float fxm = flux[c * 4], fxp = flux[c * 4 + 1], fzm = flux[c * 4 + 2], fzp = flux[c * 4 + 3];
        float fromXm = i > 0 ? flux[(c - 1) * 4 + 1] : 0f;       // the west neighbour's flow east
        float fromXp = i < n - 1 ? flux[(c + 1) * 4] : 0f;       // the east neighbour's flow west
        float fromZm = j > 0 ? flux[(c - n) * 4 + 3] : 0f;
        float fromZp = j < n - 1 ? flux[(c + n) * 4 + 2] : 0f;
        float inflow = (fromXm + fromXp) + (fromZm + fromZp);
        float outflow = (fxm + fxp) + (fzm + fzp);
        float after = MathF.Max(0f, before + k.DepthPerFlux * (inflow - outflow));

        // The net flow through the cell, as discharge per metre of width (m²/s).
        float wx = 0.5f * ((fromXm - fxm) + (fxp - fromXp));
        float wz = 0.5f * ((fromZm - fzm) + (fzp - fromZp));
        float discharge = MathF.Sqrt(wx * wx + wz * wz) * k.InvCell;

        // The ground's slope: central differences inside, one-sided at the edges.
        float gxm = i > 0 ? Ground(terrain, c - 1) : ground;
        float gxp = i < n - 1 ? Ground(terrain, c + 1) : ground;
        float gzm = j > 0 ? Ground(terrain, c - n) : ground;
        float gzp = j < n - 1 ? Ground(terrain, c + n) : ground;
        float gx = (gxp - gxm) * (i > 0 && i < n - 1 ? 0.5f * k.InvCell : k.InvCell);
        float gz = (gzp - gzm) * (j > 0 && j < n - 1 ? 0.5f * k.InvCell : k.InvCell);
        float g2 = gx * gx + gz * gz;
        float sine = MathF.Sqrt(g2 / (1f + g2));
        float capacity = k.Capacity * MathF.Max(sine, k.MinSlope) * DischargeTerm(k, discharge);

        // A cell never cuts below its lowest edge neighbour nor fills above its highest in one step: without this the water
        // drills pits between which single cells stand as needles, and piles sediment into towers.
        float lowest = MathF.Min(MathF.Min(gxm, gxp), MathF.Min(gzm, gzp));
        float highest = MathF.Max(MathF.Max(gxm, gxp), MathF.Max(gzm, gzp));
        float carried = sediment[c];
        if (capacity > carried)
        {
            float demand = MathF.Min(k.Dissolve * (capacity - carried), MathF.Max(0f, ground - lowest));
            float looseLeft = loose - MathF.Min(demand, loose);
            float fromLoose = loose - looseLeft;
            float rockLeft = rock - (demand - fromLoose) * k.RockErodibility;
            float fromRock = rock - rockLeft;
            loose = looseLeft;
            rock = rockLeft;
            carried += fromLoose + fromRock;
        }
        else
        {
            float looseAfter = loose + MathF.Min(k.Deposition * (carried - capacity), MathF.Max(0f, highest - ground));
            float dropped = looseAfter - loose;
            loose = looseAfter;
            carried = MathF.Max(0f, carried - dropped);
        }

        terrainOut[c * 2] = rock;
        terrainOut[c * 2 + 1] = loose;
        sedimentOut[c] = carried;
        waterOut[c] = after * k.Retain;
        if (k.Accumulate != 0)
        {
            flow[c * 2] += outflow;
            flow[c * 2 + 1] += after;
        }
    }

    // q^e, with the exponents the profiles use done exactly: pow's last bit is the C library's, and the pinned hashes run on
    // every CI platform.
    private static float DischargeTerm(ErosionConstants k, float q) =>
        k.DischargeExponent == 1f ? q : k.DischargeExponent == 0.5f ? MathF.Sqrt(q) : MathF.Pow(q, k.DischargeExponent);

    // The fraction of cell c's water this step sent toward one neighbour, per m³/s of that outflow.
    private static float Share(ErosionConstants k, float[] water, int c)
    {
        float before = water[c] + k.Rain;
        return before > 0f ? k.DepthPerFlux / before : 0f;
    }

    private static void TransportCell(ErosionConstants k, int n, int i, int j, float[] water, float[] flux, float[] sediment, float[] sedimentOut)
    {
        int c = j * n + i;
        float outflow = (flux[c * 4] + flux[c * 4 + 1]) + (flux[c * 4 + 2] + flux[c * 4 + 3]);
        float leaving = sediment[c] * (outflow * Share(k, water, c));
        float fromXm = i > 0 ? sediment[c - 1] * (flux[(c - 1) * 4 + 1] * Share(k, water, c - 1)) : 0f;
        float fromXp = i < n - 1 ? sediment[c + 1] * (flux[(c + 1) * 4] * Share(k, water, c + 1)) : 0f;
        float fromZm = j > 0 ? sediment[c - n] * (flux[(c - n) * 4 + 3] * Share(k, water, c - n)) : 0f;
        float fromZp = j < n - 1 ? sediment[c + n] * (flux[(c + n) * 4 + 2] * Share(k, water, c + n)) : 0f;
        sedimentOut[c] = MathF.Max(0f, (sediment[c] - leaving) + ((fromXm + fromXp) + (fromZm + fromZp)));
    }

    private static void ThermalOutCell(ErosionConstants k, int n, int i, int j, float[] terrain, float[] thermal)
    {
        int c = j * n + i;
        float rock = terrain[c * 2];
        float loose = terrain[c * 2 + 1];
        float ground = rock + loose;
        float maxLoose = 0f, maxRock = 0f, sum = 0f;
        for (int q = 0; q < 8; q++)
        {
            int ni = i + Di[q], nj = j + Dj[q];
            if (ni < 0 || ni >= n || nj < 0 || nj >= n)
                continue;
            bool corner = Di[q] != 0 && Dj[q] != 0;
            float drop = ground - Ground(terrain, nj * n + ni);
            float overLoose = MathF.Max(0f, drop - (corner ? k.LooseDropCorner : k.LooseDropEdge));
            float overRock = MathF.Max(0f, drop - (corner ? k.RockDropCorner : k.RockDropEdge));
            maxLoose = MathF.Max(maxLoose, overLoose);
            maxRock = MathF.Max(maxRock, overRock);
            sum += overLoose;
        }
        float slide = MathF.Min(loose, k.HalfThermalRate * maxLoose);
        float collapse = MathF.Max(0f, k.HalfThermalRate * maxRock - slide);
        thermal[c * 4] = loose - (loose - slide);
        thermal[c * 4 + 1] = rock - (rock - collapse);
        thermal[c * 4 + 2] = sum;
        thermal[c * 4 + 3] = 0f;
    }

    private static void ThermalApplyCell(ErosionConstants k, int n, int i, int j, float[] terrain, float[] thermal, float[] terrainOut)
    {
        int c = j * n + i;
        float rock = terrain[c * 2];
        float loose = terrain[c * 2 + 1];
        float ground = rock + loose;
        float gained = 0f;
        for (int q = 0; q < 8; q++)
        {
            int ni = i + Di[q], nj = j + Dj[q];
            if (ni < 0 || ni >= n || nj < 0 || nj >= n)
                continue;
            int m = nj * n + ni;
            float sent = thermal[m * 4] + thermal[m * 4 + 1];
            float total = thermal[m * 4 + 2];
            if (!(sent > 0f && total > 0f))
                continue;
            bool corner = Di[q] != 0 && Dj[q] != 0;
            // The neighbour's excess toward this cell, computed exactly as the neighbour computed it.
            float over = MathF.Max(0f, (Ground(terrain, m) - ground) - (corner ? k.LooseDropCorner : k.LooseDropEdge));
            gained += sent * (over / total);
        }
        terrainOut[c * 2] = rock - thermal[c * 4 + 1];
        terrainOut[c * 2 + 1] = (loose - thermal[c * 4]) + gained;
    }
}
