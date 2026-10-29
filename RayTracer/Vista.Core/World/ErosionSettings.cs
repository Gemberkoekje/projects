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

    /// <summary>
    /// The times the discharge the capacity reads is spread over its neighbours' (<c>[1 2 1] / 4</c> along the rows, then the columns;
    /// vista-plan §10 M1a.2): two spread it over about five cells, a channel's width, so the water cuts gullies that follow the fall
    /// line instead of a rill in every lane of cells on a face that falls along the grid. 0: each cell's own, as M1.7.
    /// </summary>
    public int DischargeSpread { get; init; }

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
    /// How the incision is brought back to the world grid (vista-plan §10 M1a.2, <see cref="StreamPower.Refine"/>), and today's
    /// glaciers' surface, solved on the same grid (<see cref="Glaciers"/>): 2 interpolates the incision with a continuous slope but its
    /// high points pointed (<see cref="StreamPower.UpsampleSharp"/>); 1 smoothly throughout (<see cref="StreamPower.UpsampleSmooth"/>,
    /// which rounds every summit and crest a coarse cell wide); 0 bilinearly, M1.11a's flat facets with seams. The glaciers' surface
    /// is smooth for 1 and 2.
    /// </summary>
    public int RefineCubic { get; init; }

    /// <summary>
    /// The corner cuts the incision's channels get before they are carved into the world grid (vista-plan §10 M1a.2,
    /// <see cref="StreamPower.CarveChannels"/>): each smooths the eight-direction links into a curve; 0 carves M1.11a's straight links.
    /// </summary>
    public int RefineChannelCurves { get; init; }

    /// <summary>
    /// The share of the base terrain's finer detail kept on the ground the incision cut or buried (vista-plan §10 M1a.2), its part finer
    /// than the incision's cell (<see cref="StreamPower.FinerThanCell"/>): 0 fades it out over <see cref="StreamPower.DetailFadeMetres"/>
    /// of change, as M1.11a; 1 keeps all of it away from the channels.
    /// </summary>
    public float RefineFaceDetail { get; init; }

    /// <summary>
    /// The distance from a carved channel over which the detail <see cref="RefineFaceDetail"/> keeps fades in, metres, so the channels'
    /// floors stay as carved.
    /// </summary>
    public float RefineChannelDetailMetres { get; init; } = 24f;

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
    /// The beds' dip at the centre of each region, in degrees (vista-plan §10 M1.12c): the beds tilt by a dip and a strike of
    /// their own in every region <see cref="StrataRegionMetres"/> across, turning between them (<see cref="RockHardness"/>). 0: no
    /// regional dip, as M1.11d.
    /// </summary>
    public float StrataRegionalDipDegrees { get; init; }

    /// <summary>The size of the regions the beds keep one dip and strike over, in metres.</summary>
    public float StrataRegionMetres { get; init; } = 10000f;

    /// <summary>
    /// The height of the beds' long folds, in metres from the middle to a crest (vista-plan §10 M1.12c): anticlines and synclines
    /// whose axes wander. 0: none, as M1.11d.
    /// </summary>
    public float StrataFoldMetres { get; init; }

    /// <summary>The distance from one fold's crest to the next, in metres.</summary>
    public float StrataFoldWavelengthMetres { get; init; } = 6000f;

    /// <summary>
    /// The mean thickness of a formation, a package of beds with its own bed thickness and hardness, in metres (vista-plan §10
    /// M1.12c): thin-bedded, thick-bedded and massive units stacked unevenly. 0: M1.11d's even beds.
    /// </summary>
    public float StrataFormationMetres { get; init; }

    /// <summary>
    /// The share of the ground where the rock is massive, not layered (vista-plan §10 M1.12c): a seeded mask of regions where the
    /// beds fade into one rock whose hardness changes only slowly with height. 0: layered everywhere, as M1.11d.
    /// </summary>
    public float StrataMassiveShare { get; init; }

    /// <summary>
    /// Steps of glacial carving after the incision (vista-plan §10 M1.13, <see cref="StreamPower"/>), on the finished world without
    /// uplift: each step the steady ice of the step's climate (<see cref="GlacierFlow"/>) lowers its bed in proportion to how fast it
    /// slides, where the rivers would have cut, and the slopes keep to their talus; 0: none, as M1.12c.
    /// </summary>
    public int GlacialSteps { get; init; }

    /// <summary>
    /// The mean equilibrium line of the glaciers that carve, in metres above the world's height 0: for the mountain set pieces 600 m
    /// below today's glaciers' (<see cref="GlacierElaMetres"/>), so at a cycle's coldest it stands 900 m below it, as the Alps' stood
    /// about a thousand metres lower at the last glacial maximum. Only glaciers that large carve troughs round (vista-log/M1.13.md).
    /// </summary>
    public float GlacialElaMetres { get; init; } = 1200f;

    /// <summary>How far the carving glaciers' equilibrium line swings above and below <see cref="GlacialElaMetres"/> over a glacial cycle, m.</summary>
    public float GlacialElaSwingMetres { get; init; } = 300f;

    /// <summary>The carving steps of one glacial cycle: from the equilibrium line's lowest through its highest and back.</summary>
    public int GlacialCycleSteps { get; init; } = 20;

    /// <summary>The glaciers' mass-balance gradient, m of ice a year per m of height (<see cref="GlacierClimate"/>).</summary>
    public float GlacialBalanceGradient { get; init; } = 0.007f;

    /// <summary>The most a glacier's year adds above the equilibrium line, m of ice.</summary>
    public float GlacialAccumulationMetres { get; init; } = 2f;

    /// <summary>The most a glacier's year melts below it, m of ice.</summary>
    public float GlacialAblationMetres { get; init; } = 10f;

    /// <summary>
    /// How deep a carving step lowers the bed under ice sliding at 100 m a year, m, in proportion to the sliding speed (Humphrey and
    /// Raymond 1994), less on hard rock (<see cref="HardErodibility"/>).
    /// </summary>
    public float GlacialErosionMetres { get; init; } = 8f;

    /// <summary>
    /// The equilibrium line of today's glaciers, in metres above the world's height 0 (vista-plan §10 M1.13): the recipe's own, so
    /// they stand whatever the place and the season (<see cref="Glaciers"/>); 0 lays none.
    /// </summary>
    public float GlacierElaMetres { get; init; }

    /// <summary>True when the world has no glaciers: neither M1.13's carving nor today's ice.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Unglaciated => GlacialSteps == 0 && GlacierElaMetres == 0f;

    /// <summary>The climate of the carving glaciers at carving step <paramref name="step"/> (0 first), in the world's metres.</summary>
    public GlacierClimate CarvingClimate(int step)
    {
        // A smooth wave from the line's lowest at the cycle's start (a carving begins with its glaciers at their largest) to its
        // highest half way: a smoothstep of the triangle wave, cosine-like but arithmetic alone, so no library's bit moves a world.
        double t = (double)(step % GlacialCycleSteps) / GlacialCycleSteps;
        double rise = 1.0 - Math.Abs(1.0 - 2.0 * t);
        double wave = 1.0 - 2.0 * rise * rise * (3.0 - 2.0 * rise);
        double ela = GlacialElaMetres - GlacialElaSwingMetres * wave;
        return new GlacierClimate(ela, GlacialBalanceGradient, GlacialAccumulationMetres, GlacialAblationMetres);
    }

    /// <summary>The climate of today's glaciers, in the world's frame (<see cref="GlacierElaMetres"/>).</summary>
    public GlacierClimate PresentClimate =>
        new(GlacierElaMetres, GlacialBalanceGradient, GlacialAccumulationMetres, GlacialAblationMetres);

    /// <summary>True when the incision comes back to the world grid as M1.11a brought it: bilinear, straight links, the detail faded.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool BilinearRefine => RefineCubic == 0 && RefineChannelCurves == 0 && RefineFaceDetail == 0f;

    /// <summary>True when the rock is M1.11d's: none of M1.12c's regional dip, folds, formations or massive regions.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool EvenStrata => StrataRegionalDipDegrees == 0f && StrataFoldMetres == 0f && StrataFormationMetres == 0f && StrataMassiveShare == 0f;

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
    /// the hillslopes into talus planes; a second, 32 m level grooves the faces). The strata's regional dip, folds, formations and
    /// massive share (vista-plan §10 M1.12c) were chosen by the contour-line measure and sheets of seed 42 seen from the user's
    /// photo's height: dips like the hillsides' own take the beds' bands off the contours. The glaciers (vista-plan §10 M1.13) were
    /// chosen by sheets and maps of seed 42: three glacial cycles of carving with the line swinging from 1200 to 1800 m above the world's
    /// height 0 widen the valleys the ice fills, and today's line at 1800 m keeps glaciers on the high massif and in its valleys. The
    /// grid transfer, the spread discharge and the thermal rate (vista-plan §10 M1a.2) were chosen by the grid's own patterns
    /// (<see cref="GridArtefacts"/>) on seed 42 and a 1025² world, and tilted planes: the incision brought back smoothly with the base
    /// terrain's detail on its faces takes the incised ground's relief along the grid from 68% to 15%; the discharge spread twice turns
    /// the comb of rills on faces that fall along the grid into gullies along the fall line (the windows rippling from 7.5% to 2%), and
    /// a thermal rate of 0.02 lets the water and the talus compete instead of setting every steep face to one talus plane with
    /// creases in the grid's eight directions (the relief along the grid from 21% to 18%). The rework (the user: "they look more like
    /// blobs than M1") keeps the incision's high points pointed: brought back smoothly throughout, every summit and crest the coarse
    /// samples hold came back rounded a coarse cell wide.
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
        DischargeSpread = 2,
        Dissolve = 0.1f,
        RockErodibility = 0.1f,
        Deposition = 0.1f,
        LooseTalusDegrees = 35.0f,
        RockTalusDegrees = 40.0f,
        RockTalusHardDegrees = 65.0f,
        ThermalRate = 0.02f,
        AverageFrom = 750,
        IncisionSteps = 300,
        IncisionCell = 64.0f,
        IncisionRate = 0.03f,
        ChannelAreaKm2 = 0.1f,
        RefineCubic = 2,
        RefineChannelCurves = 2,
        RefineFaceDetail = 1.0f,
        IncisionTalusDegrees = 35.0f,
        IncisionTalusHardDegrees = 60.0f,
        IncisionUplift = 8.0f,
        UpliftOctaves = 1,
        UpliftContrast = 1.5f,
        HardErodibility = 0.3f,
        StrataThickness = 60.0f,
        StrataDipDegrees = 8.0f,
        HardnessMottle = 0.08f,
        StrataRegionalDipDegrees = 40.0f,
        StrataRegionMetres = 10000.0f,
        StrataFoldMetres = 600.0f,
        StrataFoldWavelengthMetres = 5000.0f,
        StrataFormationMetres = 300.0f,
        StrataMassiveShare = 0.3f,
        IncisionLakeFill = 0.05f,
        PoolFillMetres = 100.0f,
        GlacialSteps = 60,
        GlacialElaMetres = 1200.0f,
        GlacialElaSwingMetres = 300.0f,
        GlacialCycleSteps = 20,
        GlacialBalanceGradient = 0.007f,
        GlacialAccumulationMetres = 2.0f,
        GlacialAblationMetres = 10.0f,
        GlacialErosionMetres = 12.0f,
        GlacierElaMetres = 1800.0f,
    };

    /// <summary>
    /// The erosion of the hills profile (rolling hills, and the base ground of every other set), without the incision and the pool
    /// filling until the hills are tuned in their own milestone.
    /// </summary>
    public static ErosionSettings Hills { get; } = Mountains with
    {
        GlacialSteps = 0,
        GlacierElaMetres = 0.0f,
        Capacity = 1.0f,
        RockTalusDegrees = 45.0f,
        RockTalusHardDegrees = 45.0f,
        HardErodibility = 1.0f,
        IncisionSteps = 0,
        PoolFillMetres = 0.0f,
        // M1.7's passes until the hills' own milestone (M1a.2's spread discharge and slow thermal settle the mountains).
        DischargeSpread = 0,
        ThermalRate = 0.25f,
        RefineCubic = 0,
        RefineChannelCurves = 0,
        RefineFaceDetail = 0.0f,
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
        if (DischargeSpread is < 0 or > 8)
            errors.Add($"DischargeSpread must be in [0, 8] (got {DischargeSpread}).");
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
        if (RefineCubic is not (0 or 1 or 2) || RefineChannelCurves < 0)
            errors.Add($"RefineCubic must be 0, 1 or 2 and RefineChannelCurves not negative (got {RefineCubic}, {RefineChannelCurves}).");
        Fraction(nameof(RefineFaceDetail), RefineFaceDetail);
        Positive(nameof(RefineChannelDetailMetres), RefineChannelDetailMetres);
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
        if (!(StrataRegionalDipDegrees >= 0f && StrataRegionalDipDegrees < 60f))
            errors.Add($"StrataRegionalDipDegrees must be in [0, 60) (got {StrataRegionalDipDegrees}°).");
        Positive(nameof(StrataRegionMetres), StrataRegionMetres);
        Fraction(nameof(StrataFoldMetres), StrataFoldMetres, float.MaxValue);
        Positive(nameof(StrataFoldWavelengthMetres), StrataFoldWavelengthMetres);
        Fraction(nameof(StrataFormationMetres), StrataFormationMetres, float.MaxValue);
        if (!(StrataMassiveShare >= 0f && StrataMassiveShare < 1f))
            errors.Add($"StrataMassiveShare must be in [0, 1) (got {StrataMassiveShare}).");
        if (GlacialSteps < 0 || GlacialCycleSteps < 1)
            errors.Add($"GlacialSteps must not be negative and GlacialCycleSteps must be positive (got {GlacialSteps}, {GlacialCycleSteps}).");
        if (GlacialSteps > 0 && IncisionSteps == 0)
            errors.Add("Glacial carving runs on the incision's grid: it needs IncisionSteps.");
        Fraction(nameof(GlacialElaSwingMetres), GlacialElaSwingMetres, float.MaxValue);
        Positive(nameof(GlacialBalanceGradient), GlacialBalanceGradient);
        Positive(nameof(GlacialAccumulationMetres), GlacialAccumulationMetres);
        Positive(nameof(GlacialAblationMetres), GlacialAblationMetres);
        Fraction(nameof(GlacialErosionMetres), GlacialErosionMetres, float.MaxValue);
        Fraction(nameof(GlacierElaMetres), GlacierElaMetres, float.MaxValue);
        if (!float.IsFinite(GlacialElaMetres))
            errors.Add($"GlacialElaMetres must be finite (got {GlacialElaMetres}).");
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
            // M1.12c's strata only when they are on, so M1.11d's rock keeps its key and the worlds it eroded are found in the cache.
            .. (EvenStrata ? [] : new[]
            {
                StrataRegionalDipDegrees, StrataRegionMetres, StrataFoldMetres, StrataFoldWavelengthMetres, StrataFormationMetres, StrataMassiveShare,
            }.Select(BitConverter.SingleToInt32Bits)),
            // M1.13's glaciers likewise only when there are any, so M1.12c's worlds keep their key.
            .. (Unglaciated ? [] : new[] { GlacialSteps, GlacialCycleSteps }),
            .. (Unglaciated ? [] : new[]
            {
                GlacialElaMetres, GlacialElaSwingMetres, GlacialBalanceGradient, GlacialAccumulationMetres, GlacialAblationMetres, GlacialErosionMetres,
                GlacierElaMetres,
            }.Select(BitConverter.SingleToInt32Bits)),
            // M1a.2's grid transfer only when it is not M1.11a's, so M1.11a's transfer keeps its key.
            .. (BilinearRefine ? [] : new[] { RefineCubic, RefineChannelCurves }),
            .. (BilinearRefine ? [] : new[] { RefineFaceDetail, RefineChannelDetailMetres }.Select(BitConverter.SingleToInt32Bits)),
            // M1a.2's spread discharge likewise only when it is on.
            .. (DischargeSpread == 0 ? [] : new[] { DischargeSpread }),
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

    /// <summary>
    /// The times the discharge is spread before pass 2 erodes with it (<see cref="ErosionSettings.DischargeSpread"/>, vista-plan §10
    /// M1a.2); 0: each cell's own.
    /// </summary>
    public uint SpreadDischarge;

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
            SpreadDischarge = (uint)settings.DischargeSpread,
        };
    }

    /// <summary>These constants with the bed table of <paramref name="state"/> (two floats an entry: the bed, massive rock).</summary>
    public readonly ErosionConstants WithStrata(ErosionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return WithStrata(state.TableScale, state.Bands.Length / 2);
    }

    /// <summary>These constants with a bed table of <paramref name="entries"/> entries read at <paramref name="tableScale"/> per metre.</summary>
    public readonly ErosionConstants WithStrata(float tableScale, int entries) => this with { TableScale = tableScale, TableLast = entries - 1.001f };
}
