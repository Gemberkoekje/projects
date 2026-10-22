using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Vista.World;

/// <summary>
/// The parameters of one terrain profile's erosion (vista-plan §4.3, §10 M1.7): virtual-pipe water flow with sediment
/// transport and deposition, plus thermal erosion to a talus angle, over two layers — bedrock and the loose material
/// erosion leaves behind. Everything is in metres and seconds on the world grid, and every value is a <see cref="float"/>
/// because that is what the GPU computes with: the C# reference (<see cref="ErosionReference"/>) and the shader read the
/// same numbers. Since M1.11a the <c>Incision*</c> settings run the stream-power incision on a coarse grid first
/// (<see cref="StreamPower"/>, on the CPU), which drains the base terrain's closed basins into valleys that join.
/// </summary>
public sealed record ErosionSettings
{
    /// <summary>Simulation steps.</summary>
    public int Iterations { get; init; }

    /// <summary>Seconds of water flow per step.</summary>
    public float TimeStep { get; init; }

    /// <summary>Cross-section of the virtual pipe between two cells, in m²: how fast a head difference accelerates the flow.</summary>
    public float PipeArea { get; init; }

    /// <summary>
    /// The fraction of each pipe's flow lost each step, before the head adds to it: friction. Without it the water keeps its
    /// momentum, sloshes uphill and over-shoots, and carries sediment onto piles.
    /// </summary>
    public float FluxDamping { get; init; }

    /// <summary>Rain added to every cell each step, in metres of water.</summary>
    public float RainPerStep { get; init; }

    /// <summary>The fraction of each cell's water that evaporates each step.</summary>
    public float Evaporation { get; init; }

    /// <summary>
    /// Sediment capacity per unit of slope sine and per square root of the discharge per metre of width (m²/s):
    /// <c>C = K·sin α·√q</c>, in metres of material over the cell.
    /// </summary>
    public float Capacity { get; init; }

    /// <summary>
    /// The power of the discharge in the capacity (stream power's m/n): ½ is the geomorphologists' usual value; larger values
    /// concentrate the cutting in the big channels and spare the faces between them.
    /// </summary>
    public float DischargeExponent { get; init; } = 0.5f;

    /// <summary>The smallest slope sine the capacity sees, so water on flat ground still carries a little.</summary>
    public float MinSlope { get; init; }

    /// <summary>The fraction of a cell's spare capacity it picks up from the ground per step.</summary>
    public float Dissolve { get; init; }

    /// <summary>How much less rock than loose material the same water picks up (1 = as soft as sediment).</summary>
    public float RockErodibility { get; init; }

    /// <summary>The fraction of a cell's excess sediment it drops per step.</summary>
    public float Deposition { get; init; }

    /// <summary>The angle loose material (scree, sediment) comes to rest at, in degrees.</summary>
    public float LooseTalusDegrees { get; init; }

    /// <summary>
    /// The steepest slope the softest bedrock holds, in degrees; steeper rock collapses into loose material. The hardest holds
    /// <see cref="RockTalusHardDegrees"/>; between them the slope's tangent follows the hardness.
    /// </summary>
    public float RockTalusDegrees { get; init; }

    /// <summary>The steepest slope the hardest bedrock holds, in degrees (vista-plan §10 M1.11d).</summary>
    public float RockTalusHardDegrees { get; init; }

    /// <summary>The fraction of the excess over a talus angle that moves per step (at most 0.5 keeps neighbours from overshooting).</summary>
    public float ThermalRate { get; init; }

    /// <summary>The step from which the flow maps (discharge, water depth) average.</summary>
    public int AverageFrom { get; init; }

    /// <summary>
    /// Steps of stream-power incision on the coarse grid before the pipe-model erosion (<see cref="StreamPower"/>, vista-plan §10
    /// M1.11a); 0 runs M1.7's erosion alone.
    /// </summary>
    public int IncisionSteps { get; init; }

    /// <summary>The incision grid's cell size in metres: a whole multiple of the world grid's that nests in it.</summary>
    public float IncisionCell { get; init; } = 64f;

    /// <summary>
    /// The stream-power law's <c>K·Δt</c> per incision step (dimensionless): an explicit step would lower a channel cell by this
    /// times the square root of its upstream area in m², times its slope.
    /// </summary>
    public float IncisionRate { get; init; }

    /// <summary>The upstream area from which a cell incises as a channel, in km²; below it a cell is hillslope.</summary>
    public float ChannelAreaKm2 { get; init; } = 0.1f;

    /// <summary>
    /// The steepest slope the softest rock keeps toward the cell it drains into while the valleys incise, in degrees (the
    /// hardest keeps <see cref="IncisionTalusHardDegrees"/>; between them the slope's tangent follows the hardness).
    /// </summary>
    public float IncisionTalusDegrees { get; init; } = 45f;

    /// <summary>The steepest slope the hardest rock keeps while the valleys incise, in degrees (vista-plan §10 M1.11d).</summary>
    public float IncisionTalusHardDegrees { get; init; } = 45f;

    /// <summary>
    /// Uplift per incision step where the uplift map is highest, in metres (<see cref="UpliftMap"/>; the grid's edge stays put):
    /// the ranges keep rising while the rivers cut.
    /// </summary>
    public float IncisionUplift { get; init; }

    /// <summary>
    /// The base terrain's octaves the uplift map follows (its large-scale shape; vista-plan §10 M1.11d); 0 is M1.11a's uplift, in
    /// proportion to the base terrain's own height on the incision grid.
    /// </summary>
    public int UpliftOctaves { get; init; } = 1;

    /// <summary>The power the uplift map's shape is raised to: above 1 the massifs rise and the foothills stay lower.</summary>
    public float UpliftContrast { get; init; } = 1f;

    /// <summary>
    /// How erodible the hardest rock is relative to the softest (vista-plan §10 M1.11d), for the incision and the erosion's rock
    /// alike; between them the erodibility follows the hardness (<see cref="RockHardness"/>). 1: hardness changes nothing.
    /// </summary>
    public float HardErodibility { get; init; } = 1f;

    /// <summary>The mean thickness of a bed of rock, in metres (<see cref="RockHardness"/>).</summary>
    public float StrataThickness { get; init; } = 60f;

    /// <summary>The steepest dip the beds may have, in degrees (each seed picks a direction and a dip up to it).</summary>
    public float StrataDipDegrees { get; init; } = 8f;

    /// <summary>How far the hardness strays from its bed's from place to place (± this, before the clamp to [0, 1]).</summary>
    public float HardnessMottle { get; init; } = 0.15f;

    /// <summary>
    /// The share of a lake's depth that fills with sediment per incision step (1: a lake fills to its outlet at once; 0: only
    /// the outlet cutting down drains it).
    /// </summary>
    public float IncisionLakeFill { get; init; }

    /// <summary>
    /// How deep the pools the erosion leaves fill with sediment, in metres (<see cref="ErosionResult.From(ErosionState, float)"/>):
    /// the lakes behind the fans side valleys build across the valleys below. 0 leaves them, as M1.7 did.
    /// </summary>
    public float PoolFillMetres { get; init; }

    /// <summary>The erosion of the mountain profiles (snow-capped mountains, alpine range) on the default grid.</summary>
    /// <remarks>
    /// Chosen by contact sheets of the measurement views on the 4097² × 8 m grid (vista-plan §10 M1.7): the discharge to the
    /// first power and hard rock carve gullies into the faces and keep the crests; heavy flux damping and the slow exchange of
    /// sediment keep the water from drilling needles into the valley floors and rippling the loose slopes; rock stands to 50°.
    /// The incision (vista-plan §10 M1.11a) was chosen by drainage measures and sheets of seed 42: 300 steps on a 64 m grid drain
    /// every closed basin (a coarser step rate leaves lakes; filling the lakes at once leaves them as plains; more uplift flattens
    /// the hillslopes into talus planes; a second, 32 m level grooves the faces).
    /// </remarks>
    public static ErosionSettings Mountains { get; } = new()
    {
        Iterations = 1500,
        TimeStep = 0.25f,
        PipeArea = 20.0f,
        FluxDamping = 0.8f,
        RainPerStep = 0.002f,
        Evaporation = 0.01f,
        Capacity = 2.0f,
        DischargeExponent = 1.0f,
        MinSlope = 0.05f,
        Dissolve = 0.1f,
        RockErodibility = 0.1f,
        Deposition = 0.1f,
        LooseTalusDegrees = 35.0f,
        RockTalusDegrees = 40.0f,
        RockTalusHardDegrees = 65.0f,
        ThermalRate = 0.25f,
        AverageFrom = 750,
        IncisionSteps = 300,
        IncisionCell = 64.0f,
        IncisionRate = 0.03f,
        ChannelAreaKm2 = 0.1f,
        IncisionTalusDegrees = 35.0f,
        IncisionTalusHardDegrees = 60.0f,
        IncisionUplift = 8.0f,
        UpliftOctaves = 1,
        UpliftContrast = 1.5f,
        HardErodibility = 0.3f,
        StrataThickness = 60.0f,
        StrataDipDegrees = 8.0f,
        HardnessMottle = 0.08f,
        IncisionLakeFill = 0.05f,
        PoolFillMetres = 100.0f,
    };

    /// <summary>
    /// The erosion of the hills profile (rolling hills, and the base ground of every other set), without the incision and the pool
    /// filling until the hills are tuned in their own milestone.
    /// </summary>
    public static ErosionSettings Hills { get; } = Mountains with
    {
        Capacity = 1.0f,
        RockTalusDegrees = 45.0f,
        RockTalusHardDegrees = 45.0f,
        HardErodibility = 1.0f,
        IncisionSteps = 0,
        PoolFillMetres = 0.0f,
    };

    /// <summary>
    /// An environment variable of setting overrides for tuning (<c>Name=value</c>, separated by <c>;</c>, e.g.
    /// <c>Capacity=3;Deposition=0.05</c>), applied by <see cref="For"/>. The settings are part of the world's cache key and are
    /// recorded in the sidecar, so an overridden world is never mistaken for the shipped one.
    /// </summary>
    public const string OverridesVariable = "VISTA_EROSION_SETTINGS";

    /// <summary>The erosion for <paramref name="profile"/>, with the overrides of <see cref="OverridesVariable"/> when it is set.</summary>
    public static ErosionSettings For(TerrainProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ErosionSettings settings = profile.Name == TerrainProfile.Mountains.Name ? Mountains : Hills;
        return Environment.GetEnvironmentVariable(OverridesVariable) is { Length: > 0 } overrides ? settings.With(overrides) : settings;
    }

    /// <summary>
    /// These settings with <paramref name="overrides"/> applied (<c>Name=value</c> pairs separated by <c>;</c> or <c>,</c>,
    /// property names as in C#). Throws <see cref="ArgumentException"/> for an unknown name or an unreadable value.
    /// </summary>
    public ErosionSettings With(string overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        ErosionSettings result = this with { };
        foreach (string pair in overrides.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] kv = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            System.Reflection.PropertyInfo property = typeof(ErosionSettings).GetProperty(kv[0])
                ?? throw new ArgumentException($"No erosion setting '{kv[0]}'.", nameof(overrides));
            if (kv.Length != 2)
                throw new ArgumentException($"'{pair}' is not Name=value.", nameof(overrides));
            object value = property.PropertyType == typeof(int)
                ? (object)int.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture)
                : float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
            property.SetValue(result, value);
        }
        return result;
    }

    /// <summary>The problems with these settings, empty when they are usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        void Positive(string name, float value)
        {
            if (!(value > 0f) || !float.IsFinite(value))
                errors.Add($"{name} must be positive (got {value}).");
        }
        void Fraction(string name, float value, float max = 1f)
        {
            if (!(value >= 0f && value <= max))
                errors.Add($"{name} must be in [0, {max}] (got {value}).");
        }
        if (Iterations < 0)
            errors.Add($"Iterations must not be negative (got {Iterations}).");
        if (AverageFrom < 0)
            errors.Add($"AverageFrom must not be negative (got {AverageFrom}).");
        Positive(nameof(TimeStep), TimeStep);
        Positive(nameof(PipeArea), PipeArea);
        Fraction(nameof(RainPerStep), RainPerStep, float.MaxValue);
        Fraction(nameof(Evaporation), Evaporation);
        Fraction(nameof(FluxDamping), FluxDamping);
        Fraction(nameof(Capacity), Capacity, float.MaxValue);
        Fraction(nameof(MinSlope), MinSlope);
        if (!(DischargeExponent > 0f && DischargeExponent <= 2f))
            errors.Add($"DischargeExponent must be in (0, 2] (got {DischargeExponent}).");
        Fraction(nameof(Dissolve), Dissolve);
        Fraction(nameof(RockErodibility), RockErodibility);
        Fraction(nameof(Deposition), Deposition);
        Fraction(nameof(ThermalRate), ThermalRate, 0.5f);
        if (!(LooseTalusDegrees > 0f && LooseTalusDegrees < RockTalusDegrees && RockTalusDegrees <= RockTalusHardDegrees && RockTalusHardDegrees < 90f))
            errors.Add($"Talus angles must satisfy 0 < loose < soft rock ≤ hard rock < 90° (got {LooseTalusDegrees}°, {RockTalusDegrees}°, {RockTalusHardDegrees}°).");
        if (IncisionSteps < 0)
            errors.Add($"IncisionSteps must not be negative (got {IncisionSteps}).");
        Positive(nameof(IncisionCell), IncisionCell);
        Fraction(nameof(IncisionRate), IncisionRate, float.MaxValue);
        Positive(nameof(ChannelAreaKm2), ChannelAreaKm2);
        Fraction(nameof(IncisionUplift), IncisionUplift, float.MaxValue);
        Fraction(nameof(IncisionLakeFill), IncisionLakeFill);
        Fraction(nameof(PoolFillMetres), PoolFillMetres, float.MaxValue);
        if (!(IncisionTalusDegrees > 0f && IncisionTalusDegrees <= IncisionTalusHardDegrees && IncisionTalusHardDegrees < 90f))
            errors.Add($"Incision talus angles must satisfy 0 < soft ≤ hard < 90° (got {IncisionTalusDegrees}°, {IncisionTalusHardDegrees}°).");
        if (UpliftOctaves < 0)
            errors.Add($"UpliftOctaves must not be negative (got {UpliftOctaves}).");
        Positive(nameof(UpliftContrast), UpliftContrast);
        if (!(HardErodibility > 0f && HardErodibility <= 1f))
            errors.Add($"HardErodibility must be in (0, 1] (got {HardErodibility}).");
        Positive(nameof(StrataThickness), StrataThickness);
        if (!(StrataDipDegrees >= 0f && StrataDipDegrees < 45f))
            errors.Add($"StrataDipDegrees must be in [0, 45) (got {StrataDipDegrees}°).");
        Fraction(nameof(HardnessMottle), HardnessMottle);
        return errors;
    }

    /// <summary>
    /// A 64-bit fingerprint of every parameter's exact bits, for the cache key: a world eroded with other settings is
    /// another world.
    /// </summary>
    public ulong ContentHash()
    {
        int[] words =
        [
            Iterations, AverageFrom, IncisionSteps, UpliftOctaves,
            .. new[]
            {
                TimeStep, PipeArea, FluxDamping, RainPerStep, Evaporation, Capacity, DischargeExponent, MinSlope, Dissolve, RockErodibility, Deposition,
                LooseTalusDegrees, RockTalusDegrees, ThermalRate, IncisionCell, IncisionRate, ChannelAreaKm2, IncisionTalusDegrees, IncisionUplift, IncisionLakeFill, PoolFillMetres,
                RockTalusHardDegrees, IncisionTalusHardDegrees, UpliftContrast, HardErodibility, StrataThickness, StrataDipDegrees, HardnessMottle,
            }.Select(BitConverter.SingleToInt32Bits),
        ];
        Span<byte> buffer = stackalloc byte[4];
        ulong hash = StableHash.FnvStart;
        foreach (int word in words)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer, word);
            hash = StableHash.Fnv1a(hash, buffer);
        }
        return hash;
    }
}

/// <summary>
/// The per-grid constants both the C# reference and the shader compute with, derived once (in <see cref="float"/>, in this
/// order) from the settings and the cell size, so neither side rounds them differently. The layout is the shader's
/// <c>ErosionConstants</c> cbuffer.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ErosionConstants
{
    /// <summary>Samples per side.</summary>
    public uint Size;

    /// <summary>1 when this step adds to the flow maps, else 0.</summary>
    public uint Accumulate;

    /// <summary>The 16-byte words the GPU's clear kernel zeroes (the host's, unused by the passes).</summary>
    public uint ClearCount;

    /// <summary>Padding to a 16-byte row.</summary>
    public uint Pad1;

    /// <summary>Cell size in metres.</summary>
    public float Cell;

    /// <summary>1 / cell size.</summary>
    public float InvCell;

    /// <summary>Cell area in m².</summary>
    public float CellArea;

    /// <summary>Seconds per step.</summary>
    public float TimeStep;

    /// <summary>Flux gained per metre of head per step: <c>Δt·A·g / l</c>.</summary>
    public float FluxPerHead;

    /// <summary>The fraction of the flux kept from the last step: <c>1 − damping</c>.</summary>
    public float FluxKept;

    /// <summary>Water depth gained per m³/s of net inflow per step: <c>Δt / l²</c>.</summary>
    public float DepthPerFlux;

    /// <summary>Rain per step in metres.</summary>
    public float Rain;

    /// <summary>The fraction of the water kept each step: <c>1 − evaporation</c>.</summary>
    public float Retain;

    /// <summary>Capacity constant.</summary>
    public float Capacity;

    /// <summary>The smallest slope sine.</summary>
    public float MinSlope;

    /// <summary>The power of the discharge in the capacity.</summary>
    public float DischargeExponent;

    /// <summary>The fraction of the spare capacity picked up per step.</summary>
    public float Dissolve;

    /// <summary>Rock erodibility relative to loose material.</summary>
    public float RockErodibility;

    /// <summary>The fraction of the excess sediment dropped per step.</summary>
    public float Deposition;

    /// <summary>The fraction of a talus excess that moves per step, halved (the move that would meet the neighbour half way).</summary>
    public float HalfThermalRate;

    /// <summary>Height a loose slope may drop to an edge neighbour (<c>tan θ_loose · l</c>).</summary>
    public float LooseDropEdge;

    /// <summary>The same to a corner neighbour (<c>tan θ_loose · l·√2</c>).</summary>
    public float LooseDropCorner;

    /// <summary>Height a rock slope may drop to an edge neighbour.</summary>
    public float RockDropEdge;

    /// <summary>The same to a corner neighbour.</summary>
    public float RockDropCorner;

    /// <summary>Rock erodibility of the hardest rock (<see cref="RockErodibility"/> is the softest's; vista-plan §10 M1.11d).</summary>
    public float RockErodibilityHard;

    /// <summary>Height the hardest rock may drop to an edge neighbour (<see cref="RockDropEdge"/> is the softest's).</summary>
    public float RockDropEdgeHard;

    /// <summary>The same to a corner neighbour.</summary>
    public float RockDropCornerHard;

    /// <summary>Bed-table entries per metre of rock height (<see cref="StrataGrid.TableScale"/>); 0 without strata.</summary>
    public float TableScale;

    /// <summary>The largest bed-table coordinate a lookup reads (the table's last entry less 1.001).</summary>
    public float TableLast;

    /// <summary>Padding to a 16-byte row.</summary>
    public uint Pad2;

    /// <summary>Padding to a 16-byte row.</summary>
    public uint Pad3;

    /// <summary>Padding to a 16-byte row.</summary>
    public uint Pad4;

    /// <summary>
    /// The constants for <paramref name="settings"/> on a grid of <paramref name="size"/>² cells <paramref name="cell"/> metres
    /// apart; the bed table's (<see cref="TableScale"/>, <see cref="TableLast"/>) come from the state (<see cref="WithStrata"/>).
    /// </summary>
    public static ErosionConstants For(ErosionSettings settings, int size, double cell)
    {
        ArgumentNullException.ThrowIfNull(settings);
        float l = (float)cell;
        const float Gravity = 9.81f;
        float tanLoose = (float)Math.Tan(settings.LooseTalusDegrees * Math.PI / 180.0);
        float tanRock = (float)Math.Tan(settings.RockTalusDegrees * Math.PI / 180.0);
        float tanHard = (float)Math.Tan(settings.RockTalusHardDegrees * Math.PI / 180.0);
        float diagonal = (float)Math.Sqrt(2.0);
        return new ErosionConstants
        {
            Size = (uint)size,
            Cell = l,
            InvCell = 1f / l,
            CellArea = l * l,
            TimeStep = settings.TimeStep,
            FluxPerHead = settings.TimeStep * settings.PipeArea * Gravity / l,
            FluxKept = 1f - settings.FluxDamping,
            DepthPerFlux = settings.TimeStep / (l * l),
            Rain = settings.RainPerStep,
            Retain = 1f - settings.Evaporation,
            Capacity = settings.Capacity,
            MinSlope = settings.MinSlope,
            DischargeExponent = settings.DischargeExponent,
            Dissolve = settings.Dissolve,
            RockErodibility = settings.RockErodibility,
            Deposition = settings.Deposition,
            HalfThermalRate = 0.5f * settings.ThermalRate,
            LooseDropEdge = tanLoose * l,
            LooseDropCorner = tanLoose * l * diagonal,
            RockDropEdge = tanRock * l,
            RockDropCorner = tanRock * l * diagonal,
            RockErodibilityHard = settings.RockErodibility * settings.HardErodibility,
            RockDropEdgeHard = tanHard * l,
            RockDropCornerHard = tanHard * l * diagonal,
        };
    }

    /// <summary>These constants with the bed table of <paramref name="state"/>.</summary>
    public readonly ErosionConstants WithStrata(ErosionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return WithStrata(state.TableScale, state.Bands.Length);
    }

    /// <summary>These constants with a bed table of <paramref name="entries"/> entries read at <paramref name="tableScale"/> per metre.</summary>
    public readonly ErosionConstants WithStrata(float tableScale, int entries) => this with { TableScale = tableScale, TableLast = entries - 1.001f };
}
