using System.Runtime.InteropServices;

namespace Vista.World;

/// <summary>
/// Where the ground's materials lie (vista-plan §4.3, §10 M1.12): rock, scree, soil and grass, and how wet the ground is, from
/// the slope, the altitude, the aspect and the erosion's maps — the loose material it left (the deposition map), the land that
/// drains through a place (the flow accumulation) and the water that stood there (the wetness map) — and from the hardness of
/// the bed the surface cuts (M1.11d's strata), so the bare rock follows the beds that shaped the cliffs. These are the thresholds
/// in degrees, metres and the hardness's own units; <see cref="Evaluate"/> is the rule, which the shader evaluates per hit
/// (<c>MaterialWeightsOf</c> in <c>VistaMaterials.hlsli</c>) and the mesh per vertex, both from <see cref="MaterialConstants"/>
/// so the two read one definition. Every weight blends smoothly, and two seeded noises (<see cref="MaterialNoise"/>) shift the
/// thresholds from place to place, so the boundaries wander as they do on real ground instead of following contour lines.
/// </summary>
public sealed record MaterialRules
{
    /// <summary>The slope above which the ground is bare rock, for rock of middling hardness, degrees: steeper than loose material lies (35°).</summary>
    public float RockSlopeDegrees { get; init; } = 40f;

    /// <summary>Half the width of the change from ground to bare rock, degrees.</summary>
    public float RockSlopeWidthDegrees { get; init; } = 5f;

    /// <summary>How much lower the hardest rock stands bare than the softest, degrees: hard beds hold faces where soft ones weather to ground.</summary>
    public float HardnessSlopeShiftDegrees { get; init; } = 12f;

    /// <summary>How far the breakup noise moves the rock's slope, degrees.</summary>
    public float RockNoiseDegrees { get; init; } = 4f;

    /// <summary>Loose material thinner than this leaves the ground uncovered, m (the erosion leaves 1–4 cm on bare slopes).</summary>
    public float CoverFromMetres { get; init; } = 0.15f;

    /// <summary>Loose material at least this deep covers the ground, m.</summary>
    public float CoverToMetres { get; init; } = 0.8f;

    /// <summary>The share of a steep slope that thick loose material turns from rock to scree.</summary>
    public float CoverOnSteep { get; init; } = 0.6f;

    /// <summary>The slope from which hard rock crops out through thin cover, degrees.</summary>
    public float OutcropSlopeFromDegrees { get; init; } = 24f;

    /// <summary>The slope at which hard rock under thin cover is all outcrop, degrees.</summary>
    public float OutcropSlopeToDegrees { get; init; } = 36f;

    /// <summary>The hardness from which rock crops out on moderate slopes.</summary>
    public float OutcropHardFrom { get; init; } = 0.55f;

    /// <summary>The hardness at which it does fully.</summary>
    public float OutcropHardTo { get; init; } = 0.9f;

    /// <summary>How far the second breakup noise moves the outcrops' and the scree's slopes, degrees.</summary>
    public float SlopeNoiseDegrees { get; init; } = 4f;

    /// <summary>The slope from which loose material is scree (talus below faces), degrees; gentler loose ground is soil or meadow.</summary>
    public float ScreeSlopeFromDegrees { get; init; } = 24f;

    /// <summary>The slope at which loose material is all scree, degrees (the loose talus angle is 35°).</summary>
    public float ScreeSlopeToDegrees { get; init; } = 30f;

    /// <summary>
    /// The upstream area from which a place is a stream's channel (wet, its loose material gravel), m²: about where alpine streams
    /// run all summer. Smaller gullies on a planar slope drain in parallel lines (the drainage's eight directions), which read as
    /// ruled lines if they darken.
    /// </summary>
    public float ChannelAreaFromSquareMetres { get; init; } = 1e5f;

    /// <summary>The upstream area at which it is fully a channel, m².</summary>
    public float ChannelAreaToSquareMetres { get; init; } = 1e6f;

    /// <summary>The erosion's mean water depth from which the ground is wet, m (flats hold 0.2–0.5 m in the simulation).</summary>
    public float WetDepthFromMetres { get; init; } = 0.5f;

    /// <summary>The mean water depth at which it is fully wet, m.</summary>
    public float WetDepthToMetres { get; init; } = 1.5f;

    /// <summary>Half the width of the change from meadow to bare soil across the grass line, m.</summary>
    public float GrassBandMetres { get; init; } = 120f;

    /// <summary>How much lower the grass line lies on a fully poleward face (and higher on an equator-facing one), m per unit of slope.</summary>
    public float AspectShiftMetres { get; init; } = 200f;

    /// <summary>How far the breakup noise moves the grass line, m.</summary>
    public float GrassNoiseMetres { get; init; } = 90f;

    /// <summary>The slope from which grass thins out, degrees.</summary>
    public float GrassSlopeFromDegrees { get; init; } = 36f;

    /// <summary>The slope above which no grass holds, degrees.</summary>
    public float GrassSlopeToDegrees { get; init; } = 46f;

    /// <summary>The fine breakup noise below which a meadow shows bare soil (erosion scars, paths), starting.</summary>
    public float BareFrom { get; init; } = -0.35f;

    /// <summary>The fine breakup noise at which a meadow is all bare soil.</summary>
    public float BareTo { get; init; } = -0.75f;

    /// <summary>How much darker fully wet ground is (wet rock and soil reflect about half as much, Lekner and Dorf 1988).</summary>
    public float WetDarkening { get; init; } = 0.45f;

    /// <summary>The bed hardness at which rock's colour is halfway from the soft rock's to the hard rock's.</summary>
    public float HardColourMid { get; init; } = 0.5f;

    /// <summary>Half the width of the change from the soft rock's colour to the hard rock's, in hardness.</summary>
    public float HardColourWidth { get; init; } = 0.3f;

    /// <summary>How much a bed's own tint brightens or darkens its rock (a fraction), so beds of like hardness still differ.</summary>
    public float BedBrightness { get; init; } = 0.25f;

    /// <summary>How much a bed's own tint warms or cools its rock: a fraction of its reflectance per 150 nm from 550 nm.</summary>
    public float BedWarmth { get; init; } = 0.15f;

    /// <summary>How much lighter or darker one lamina of a bed is than the next, at most (a fraction): layered rock near the camera.</summary>
    public float LaminaContrast { get; init; } = 0.12f;

    /// <summary>
    /// How strongly the laminae step a rock face, at most (vista-plan §10 M1.12): the face's slope swings between 1 − this and 1 + this
    /// times its own through a lamina, a riser then a tread, in the shading (the mesh cannot afford steps whose bound grows with the
    /// slope). Each lamina takes its own share (a uniform number cubed: most faint, a few strong); hard beds step, soft ones not.
    /// </summary>
    public float StepStrength { get; init; } = 0.6f;

    /// <summary>The laminae per metre of stratigraphic height (a bed of 60 m holds about 30).</summary>
    public float LaminaePerMetre { get; init; } = 0.5f;

    /// <summary>
    /// The shading's roughness below the mesh's detail (vista-plan §10 M1.12): the finest bump's wavelength, m; its octaves double
    /// from there (<see cref="BumpOctaves"/>), each fading out where a pixel covers more than a quarter of it.
    /// </summary>
    public float BumpFinestMetres { get; init; } = 0.1f;

    /// <summary>The bump's octaves: 0.1, 0.2, 0.4, 0.8 and 1.6 m by default.</summary>
    public int BumpOctaves { get; init; } = 5;

    /// <summary>Bare rock's bump: ridged noise, each octave's height over its wavelength.</summary>
    public float RoughRock { get; init; } = 0.12f;

    /// <summary>Scree's bump: ridged noise, each octave's height over its wavelength.</summary>
    public float RoughScree { get; init; } = 0.08f;

    /// <summary>Soil's bump: smooth noise, each octave's height over its wavelength.</summary>
    public float RoughSoil { get; init; } = 0.03f;

    /// <summary>Grass's bump: smooth noise in the two finest octaves only (the blades' tufts), each octave's height over its wavelength.</summary>
    public float RoughGrass { get; init; } = 0.2f;

    /// <summary>
    /// Scree's stones in the shading (vista-plan §10 M1.12): stones of 0.4, 0.8 and 1.6 m, each a flat facet tilted up to this slope
    /// (rise over run) in a random direction, the sizes' tilts adding, so loose rock reads as broken stones rather than a sheet.
    /// </summary>
    public float StoneTilt { get; init; } = 0.55f;

    /// <summary>How much darker the gap between two stones is, at most, per size (a fraction): their shadowed joints.</summary>
    public float StoneGap { get; init; } = 0.12f;

    /// <summary>The wavelength of the coarse breakup noise, m.</summary>
    public float BreakupWavelengthMetres { get; init; } = 60f;

    /// <summary>The wavelength of the fine breakup noise, m.</summary>
    public float FineBreakupWavelengthMetres { get; init; } = 11f;

    /// <summary>The rules every world uses.</summary>
    public static MaterialRules Default { get; } = new();

    /// <summary>Problems that make these rules unusable (empty when valid).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        void Rising(string name, float from, float to)
        {
            if (!(float.IsFinite(from) && float.IsFinite(to) && from < to))
                errors.Add($"{name}: the start must be below the end (got {from} and {to}).");
        }
        void NotNegative(string name, float value)
        {
            if (!(float.IsFinite(value) && value >= 0f))
                errors.Add($"{name} must be finite and not negative (got {value}).");
        }
        Rising("Cover", CoverFromMetres, CoverToMetres);
        Rising("Outcrop slope", OutcropSlopeFromDegrees, OutcropSlopeToDegrees);
        Rising("Outcrop hardness", OutcropHardFrom, OutcropHardTo);
        Rising("Scree slope", ScreeSlopeFromDegrees, ScreeSlopeToDegrees);
        Rising("Channel area", ChannelAreaFromSquareMetres, ChannelAreaToSquareMetres);
        Rising("Wet depth", WetDepthFromMetres, WetDepthToMetres);
        Rising("Grass slope", GrassSlopeFromDegrees, GrassSlopeToDegrees);
        Rising("Bare soil", BareTo, BareFrom);
        foreach ((string name, float value) in new[]
        {
            (nameof(RockSlopeDegrees), RockSlopeDegrees), (nameof(HardnessSlopeShiftDegrees), HardnessSlopeShiftDegrees),
            (nameof(RockNoiseDegrees), RockNoiseDegrees), (nameof(SlopeNoiseDegrees), SlopeNoiseDegrees), (nameof(AspectShiftMetres), AspectShiftMetres),
            (nameof(GrassNoiseMetres), GrassNoiseMetres),
        })
        {
            NotNegative(name, value);
        }
        if (!(RockSlopeWidthDegrees > 0f) || !(GrassBandMetres > 0f))
            errors.Add("The rock's slope width and the grass band must be positive.");
        if (!(CoverOnSteep is >= 0f and <= 1f) || !(WetDarkening is >= 0f and < 1f))
            errors.Add("The cover on steep slopes must be in [0, 1] and the wet darkening in [0, 1).");
        if (!(BreakupWavelengthMetres > 0f) || !(FineBreakupWavelengthMetres > 0f))
            errors.Add("The breakup noise's wavelengths must be positive.");
        if (!(HardColourWidth > 0f) || !(LaminaePerMetre > 0f) || !(BumpFinestMetres > 0f) || BumpOctaves is < 0 or > 8)
            errors.Add("The rock colour's width, the laminae per metre and the finest bump must be positive, and the bump's octaves 0 to 8.");
        foreach ((string name, float value) in new[]
        {
            (nameof(HardColourMid), HardColourMid), (nameof(BedBrightness), BedBrightness), (nameof(BedWarmth), BedWarmth), (nameof(LaminaContrast), LaminaContrast),
            (nameof(StepStrength), StepStrength), (nameof(RoughRock), RoughRock), (nameof(RoughScree), RoughScree), (nameof(RoughSoil), RoughSoil),
            (nameof(RoughGrass), RoughGrass), (nameof(StoneTilt), StoneTilt), (nameof(StoneGap), StoneGap),
        })
        {
            if (!(value is >= 0f and < 1f))
                errors.Add($"{name} must be in [0, 1) (got {value}).");
        }
        return errors;
    }

    /// <summary>
    /// The constants the shader and the mesh evaluate these rules with, for a scene whose grass line stands at
    /// <paramref name="grassLineMetres"/> above sea level, with the pole toward +z (<paramref name="poleward"/> 1, the northern
    /// hemisphere) or −z (−1), and the material noise of <paramref name="noiseSeed"/>.
    /// </summary>
    public MaterialConstants Constants(float grassLineMetres, float poleward, uint noiseSeed) => new()
    {
        RockSlope = RockSlopeDegrees,
        RockWidth = RockSlopeWidthDegrees,
        HardnessShift = HardnessSlopeShiftDegrees,
        RockNoise = RockNoiseDegrees,
        CoverFrom = CoverFromMetres,
        CoverTo = CoverToMetres,
        CoverOnSteep = CoverOnSteep,
        OutcropSlopeFrom = OutcropSlopeFromDegrees,
        OutcropSlopeTo = OutcropSlopeToDegrees,
        OutcropHardFrom = OutcropHardFrom,
        OutcropHardTo = OutcropHardTo,
        SlopeNoise = SlopeNoiseDegrees,
        ScreeSlopeFrom = ScreeSlopeFromDegrees,
        ScreeSlopeTo = ScreeSlopeToDegrees,
        ChannelLogFrom = MathF.Log10(ChannelAreaFromSquareMetres),
        ChannelLogTo = MathF.Log10(ChannelAreaToSquareMetres),
        WetDepthFrom = WetDepthFromMetres,
        WetDepthTo = WetDepthToMetres,
        GrassLine = grassLineMetres,
        GrassBand = GrassBandMetres,
        AspectShift = AspectShiftMetres,
        GrassNoise = GrassNoiseMetres,
        GrassSlopeFrom = GrassSlopeFromDegrees,
        GrassSlopeTo = GrassSlopeToDegrees,
        BareFrom = BareFrom,
        BareTo = BareTo,
        WetDarkening = WetDarkening,
        Poleward = poleward,
        NoiseSeed = noiseSeed,
        Breakup1 = BreakupWavelengthMetres,
        Breakup2 = FineBreakupWavelengthMetres,
        HardColourMid = HardColourMid,
        HardColourWidth = HardColourWidth,
        BedBrightness = BedBrightness,
        BedWarmth = BedWarmth,
        LaminaContrast = LaminaContrast,
        StepStrength = StepStrength,
        LaminaePerEntry = LaminaePerMetre * RockHardness.TableSpacingMetres,
        BumpFinest = BumpFinestMetres,
        BumpOctaves = (uint)BumpOctaves,
        RoughRock = RoughRock,
        RoughScree = RoughScree,
        RoughSoil = RoughSoil,
        RoughGrass = RoughGrass,
        StoneTilt = StoneTilt,
        StoneGap = StoneGap,
    };

    /// <summary>
    /// The weights of the materials at a place with <paramref name="p"/> (<see cref="MaterialWeights"/>: rock, scree, soil and grass
    /// summing to 1, and the wetness on top). In single precision, operation for operation as the shader's <c>MaterialWeightsOf</c>:
    /// <list type="number">
    ///   <item>bare rock where the slope is steeper than loose material lies, earlier for hard beds (and later, by
    ///   <see cref="CoverOnSteep"/>, under thick loose material), and outcrops of hard rock through thin cover on moderate slopes;</item>
    ///   <item>of the rest, scree where loose material lies at the talus angle (deposition below the faces) or in a channel (gravel);</item>
    ///   <item>the remaining ground is meadow below the grass line (lower on poleward faces), on slopes grass holds to, and away from
    ///   the bare patches the fine noise opens in it; else soil, which is stony (<see cref="MaterialWeights.Stony"/>: frost-shattered
    ///   debris, coloured toward scree) above the grass line and on slopes too steep for grass;</item>
    ///   <item>the ground is wet in channels that are not rock faces and where the erosion's water stood deep.</item>
    /// </list>
    /// </summary>
    public static MaterialWeights Evaluate(in MaterialInputs p, in MaterialConstants k)
    {
        float cover = SmoothStep(k.CoverFrom, k.CoverTo, p.LooseMetres);
        float rockAt = k.RockSlope - k.HardnessShift * (p.Hardness - 0.5f) + k.RockNoise * p.Breakup1;
        float steep = SmoothStep(rockAt - k.RockWidth, rockAt + k.RockWidth, p.SlopeDegrees);
        float outcrop = (1f - cover) * SmoothStep(k.OutcropSlopeFrom, k.OutcropSlopeTo, p.SlopeDegrees + k.SlopeNoise * p.Breakup2)
            * SmoothStep(k.OutcropHardFrom, k.OutcropHardTo, p.Hardness);
        float rock = MathF.Max(steep * (1f - k.CoverOnSteep * cover), outcrop);
        float rest = 1f - rock;
        float channel = SmoothStep(k.ChannelLogFrom, k.ChannelLogTo, p.Log10Area);
        float talus = SmoothStep(k.ScreeSlopeFrom, k.ScreeSlopeTo, p.SlopeDegrees + k.SlopeNoise * p.Breakup2);
        float scree = rest * cover * MathF.Max(talus, channel);
        float ground = rest - scree;
        float line = k.GrassLine - k.AspectShift * p.Poleward + k.GrassNoise * p.Breakup1;
        float high = SmoothStep(line - k.GrassBand, line + k.GrassBand, p.AltitudeMetres);
        float tooSteep = SmoothStep(k.GrassSlopeFrom, k.GrassSlopeTo, p.SlopeDegrees);
        float bare = SmoothStep(k.BareFrom, k.BareTo, p.Breakup2);
        float grass = ground * ((1f - high) * (1f - tooSteep) * (1f - bare));
        float soil = ground - grass;
        float wet = MathF.Max(channel * (1f - steep), SmoothStep(k.WetDepthFrom, k.WetDepthTo, p.WaterDepthMetres) * (1f - talus));
        return new MaterialWeights(rock, scree, soil, grass, wet, MathF.Max(high, tooSteep));
    }

    /// <summary>HLSL's <c>smoothstep</c>: 0 below <paramref name="a"/>, 1 above <paramref name="b"/>, the cubic between.</summary>
    public static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}

/// <summary>What the material rules read at a place (<see cref="MaterialRules.Evaluate"/>).</summary>
/// <param name="SlopeDegrees">The slope of the world grid's surface there, degrees.</param>
/// <param name="Poleward">How much the surface faces the pole: its normal's horizontal component toward the pole (−1 to 1; 0 on the flat).</param>
/// <param name="AltitudeMetres">The altitude above sea level, m.</param>
/// <param name="LooseMetres">The loose material on the bedrock, m.</param>
/// <param name="Log10Area">log₁₀ of the area that drains through the place, m².</param>
/// <param name="WaterDepthMetres">The erosion's mean water depth there, m.</param>
/// <param name="Hardness">The hardness of the bed the surface cuts, 0 (softest) to 1 (hardest).</param>
/// <param name="Breakup1">The coarse breakup noise, about −1 to 1.</param>
/// <param name="Breakup2">The fine breakup noise, about −1 to 1.</param>
public readonly record struct MaterialInputs(
    float SlopeDegrees,
    float Poleward,
    float AltitudeMetres,
    float LooseMetres,
    float Log10Area,
    float WaterDepthMetres,
    float Hardness,
    float Breakup1,
    float Breakup2);

/// <summary>
/// The materials at a place: rock, scree, soil and grass sum to 1; <paramref name="Wet"/> (0 to 1) darkens whichever lies there, and
/// <paramref name="Stony"/> (0 to 1) turns the soil's colour toward scree's.
/// </summary>
/// <param name="Rock">Bare rock (hard or soft by the bed, <see cref="MaterialField"/>).</param>
/// <param name="Scree">Loose rock: talus below faces and channel gravel.</param>
/// <param name="Soil">Bare soil.</param>
/// <param name="Grass">Meadow.</param>
/// <param name="Wet">How wet the ground is.</param>
/// <param name="Stony">How stony the soil is: above the grass line and on slopes too steep for grass, frost-shattered debris.</param>
public readonly record struct MaterialWeights(float Rock, float Scree, float Soil, float Grass, float Wet, float Stony)
{
    /// <summary>The weight of the smooth ground, soil and grass together (<see cref="DetailKind.Smooth"/>).</summary>
    public float Smooth => Soil + Grass;
}

/// <summary>
/// The material rules' constants as the shader's <c>MaterialConstants</c> cbuffer holds them (16-byte rows; vista-plan §10 M1.12),
/// derived once from <see cref="MaterialRules"/> and the scene, so the shader and the mesh compute with the same floats. The later
/// rows carry what the shader needs besides the rules: the material map's place and the bed table (<see cref="MaterialField.Apply"/>),
/// the rocks' colours by bed, the shading's bump and the camera's place for it (<see cref="ForCamera"/>), and which shading runs. Sixteen
/// rows: the 256-byte buffer is full.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MaterialConstants
{
    /// <summary><see cref="MaterialRules.RockSlopeDegrees"/>.</summary>
    public float RockSlope;

    /// <summary><see cref="MaterialRules.RockSlopeWidthDegrees"/>.</summary>
    public float RockWidth;

    /// <summary><see cref="MaterialRules.HardnessSlopeShiftDegrees"/>.</summary>
    public float HardnessShift;

    /// <summary><see cref="MaterialRules.RockNoiseDegrees"/>.</summary>
    public float RockNoise;

    /// <summary><see cref="MaterialRules.CoverFromMetres"/>.</summary>
    public float CoverFrom;

    /// <summary><see cref="MaterialRules.CoverToMetres"/>.</summary>
    public float CoverTo;

    /// <summary><see cref="MaterialRules.CoverOnSteep"/>.</summary>
    public float CoverOnSteep;

    /// <summary><see cref="MaterialRules.OutcropSlopeFromDegrees"/>.</summary>
    public float OutcropSlopeFrom;

    /// <summary><see cref="MaterialRules.OutcropSlopeToDegrees"/>.</summary>
    public float OutcropSlopeTo;

    /// <summary><see cref="MaterialRules.OutcropHardFrom"/>.</summary>
    public float OutcropHardFrom;

    /// <summary><see cref="MaterialRules.OutcropHardTo"/>.</summary>
    public float OutcropHardTo;

    /// <summary><see cref="MaterialRules.SlopeNoiseDegrees"/>.</summary>
    public float SlopeNoise;

    /// <summary><see cref="MaterialRules.ScreeSlopeFromDegrees"/>.</summary>
    public float ScreeSlopeFrom;

    /// <summary><see cref="MaterialRules.ScreeSlopeToDegrees"/>.</summary>
    public float ScreeSlopeTo;

    /// <summary>log₁₀ of <see cref="MaterialRules.ChannelAreaFromSquareMetres"/>.</summary>
    public float ChannelLogFrom;

    /// <summary>log₁₀ of <see cref="MaterialRules.ChannelAreaToSquareMetres"/>.</summary>
    public float ChannelLogTo;

    /// <summary><see cref="MaterialRules.WetDepthFromMetres"/>.</summary>
    public float WetDepthFrom;

    /// <summary><see cref="MaterialRules.WetDepthToMetres"/>.</summary>
    public float WetDepthTo;

    /// <summary>The grass line above sea level, m (<see cref="Weather.GrassLine"/>).</summary>
    public float GrassLine;

    /// <summary><see cref="MaterialRules.GrassBandMetres"/>.</summary>
    public float GrassBand;

    /// <summary><see cref="MaterialRules.AspectShiftMetres"/>.</summary>
    public float AspectShift;

    /// <summary><see cref="MaterialRules.GrassNoiseMetres"/>.</summary>
    public float GrassNoise;

    /// <summary><see cref="MaterialRules.GrassSlopeFromDegrees"/>.</summary>
    public float GrassSlopeFrom;

    /// <summary><see cref="MaterialRules.GrassSlopeToDegrees"/>.</summary>
    public float GrassSlopeTo;

    /// <summary><see cref="MaterialRules.BareFrom"/>.</summary>
    public float BareFrom;

    /// <summary><see cref="MaterialRules.BareTo"/>.</summary>
    public float BareTo;

    /// <summary><see cref="MaterialRules.WetDarkening"/>.</summary>
    public float WetDarkening;

    /// <summary>1 when the pole is toward +z (the northern hemisphere), −1 when toward −z.</summary>
    public float Poleward;

    /// <summary>The material noise's seed (<see cref="MaterialNoise.SeedOf"/>).</summary>
    public uint NoiseSeed;

    /// <summary>Padding to a 16-byte row.</summary>
    public uint Pad0;

    /// <summary>Padding to a 16-byte row.</summary>
    public uint Pad4;

    /// <summary>Padding to a 16-byte row.</summary>
    public uint Pad5;

    /// <summary><see cref="MaterialRules.BreakupWavelengthMetres"/>.</summary>
    public float Breakup1;

    /// <summary><see cref="MaterialRules.FineBreakupWavelengthMetres"/>.</summary>
    public float Breakup2;

    /// <summary>Bed-table entries per metre of world height (<see cref="StrataGrid.TableScale"/>).</summary>
    public float TableScale;

    /// <summary>The largest bed-table coordinate a lookup reads (the table's last entry less 1.001).</summary>
    public float TableLast;

    /// <summary>World x of the material map's sample (0, 0), m.</summary>
    public float MapOriginX;

    /// <summary>World z of the material map's sample (0, 0), m.</summary>
    public float MapOriginZ;

    /// <summary>Metres between the material map's samples.</summary>
    public float MapCell;

    /// <summary>The material map's samples a side (0: no map; the ground away from the maps reads defaults).</summary>
    public uint MapSize;

    /// <summary>The hardness at which a bed's colour is halfway from the soft rock's to the hard rock's.</summary>
    public float HardColourMid;

    /// <summary>Half the width of the change from the soft rock's colour to the hard rock's, in hardness.</summary>
    public float HardColourWidth;

    /// <summary>How much a bed's own tint brightens or darkens its rock (a fraction).</summary>
    public float BedBrightness;

    /// <summary>How much a bed's own tint warms or cools its rock (a fraction of the reflectance per 150 nm from 550 nm).</summary>
    public float BedWarmth;

    /// <summary><see cref="MaterialRules.LaminaContrast"/>.</summary>
    public float LaminaContrast;

    /// <summary><see cref="MaterialRules.StepStrength"/>.</summary>
    public float StepStrength;

    /// <summary>Laminae per bed-table entry (the table's entries are 2 m of stratigraphic height apart).</summary>
    public float LaminaePerEntry;

    /// <summary>1 when the rules shade the ground; 0 for M0's single material everywhere (<c>--materials off</c>).</summary>
    public uint Mode;

    /// <summary>1 for Oren–Nayar-style rough diffuse surfaces; 0 for Lambert (<c>--diffuse</c>).</summary>
    public uint Diffuse;

    /// <summary>The frame's pixel at the centre per metre of distance (2·tan(fov/2) / its height): a camera hit's footprint, for the bump's fading.</summary>
    public float PixelAngle;

    /// <summary><see cref="MaterialRules.BumpFinestMetres"/>.</summary>
    public float BumpFinest;

    /// <summary><see cref="MaterialRules.BumpOctaves"/>.</summary>
    public uint BumpOctaves;

    /// <summary><see cref="MaterialRules.RoughRock"/>.</summary>
    public float RoughRock;

    /// <summary><see cref="MaterialRules.RoughScree"/>.</summary>
    public float RoughScree;

    /// <summary><see cref="MaterialRules.RoughSoil"/>.</summary>
    public float RoughSoil;

    /// <summary><see cref="MaterialRules.RoughGrass"/>.</summary>
    public float RoughGrass;

    /// <summary>
    /// The camera's world x in whole <see cref="BumpFinest"/> cells: with <see cref="CamFracX"/> the camera's place, split so the
    /// bump's lattice is the world's (a pure function of position) yet single precision keeps its finest cells near the camera.
    /// </summary>
    public int CamCellX;

    /// <summary>The camera's world z in whole cells.</summary>
    public int CamCellZ;

    /// <summary>The camera's world x past its cell, m.</summary>
    public float CamFracX;

    /// <summary>The camera's world z past its cell, m.</summary>
    public float CamFracZ;

    /// <summary><see cref="MaterialRules.StoneTilt"/>.</summary>
    public float StoneTilt;

    /// <summary><see cref="MaterialRules.StoneGap"/>.</summary>
    public float StoneGap;

    /// <summary>Padding to a 16-byte row.</summary>
    public float Pad6;

    /// <summary>Padding to a 16-byte row.</summary>
    public float Pad7;

    /// <summary>
    /// These constants for a camera at world <c>(x, z)</c> whose frame's centre pixel covers <paramref name="pixelAngle"/> radians:
    /// its place split into whole bump cells and the rest (in double precision, then each part rounded).
    /// </summary>
    public readonly MaterialConstants ForCamera(double x, double z, double pixelAngle)
    {
        double cell = BumpFinest > 0f ? BumpFinest : 1.0;
        double cx = Math.Floor(x / cell), cz = Math.Floor(z / cell);
        return this with
        {
            PixelAngle = (float)pixelAngle,
            CamCellX = (int)cx,
            CamCellZ = (int)cz,
            CamFracX = (float)(x - cx * cell),
            CamFracZ = (float)(z - cz * cell),
        };
    }
}
