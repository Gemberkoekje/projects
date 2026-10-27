using System.Globalization;
using Vista.Atmosphere;
using Vista.World;

namespace Vista.Weather;

/// <summary>
/// What the weather chain tells the valleys' pools (vista-plan §10 M1.13b), read at the scene's valley floor: the night's pool there
/// (M1.13a's chain, <see cref="ValleyHaze"/>), the air's temperature and dew point, and the haze it holds. Every valley's pool is worked
/// out from these and its own shape (<see cref="ValleyPools"/>).
/// </summary>
public sealed record PoolWeather
{
    /// <summary>No pools: no night's pool and no mist.</summary>
    public static PoolWeather None { get; } = new();

    /// <summary>The scene's valley floor, m above sea level: the reference valley's floor, where the sliders read the air.</summary>
    public double FloorMetres { get; init; }

    /// <summary>The night's pool at its deepest, at dawn, over a valley that holds all of it, m (<see cref="ValleyHaze.Depth"/>).</summary>
    public double DawnDepthMetres { get; init; }

    /// <summary>
    /// The sun's heat against the dawn pool's deficit: by the hour before the day's high, by the high after it (<see cref="ValleyHaze.PoolShare"/>'s
    /// heat over <see cref="ValleyHaze.HeatDeficit"/>); 0 before sunrise.
    /// </summary>
    public double HeatRatio { get; init; }

    /// <summary>True before the day's high (the pool breaking up), false after it (forming again).</summary>
    public bool Morning { get; init; } = true;

    /// <summary>After the day's high, the share of the night's cooling the air has done since (0 to 1).</summary>
    public double Cooled { get; init; }

    /// <summary>True when every valley keeps the night's whole pool at any hour: the valley haze or the mist is pinned.</summary>
    public bool WholePool { get; init; }

    /// <summary>A pinned valley haze: the extinction it adds at 550 nm at the floor of the densest pool, per metre; null for auto.</summary>
    public double? PinnedExtinction550 { get; init; }

    /// <summary>The air at the scene's valley floor at the hour, °C (the temperature slider): inside the reference valley's pool.</summary>
    public double TemperatureCelsius { get; init; }

    /// <summary>The dew point at the scene's valley floor, °C: the place's humidity normals, or a pinned humidity's.</summary>
    public double DewPointCelsius { get; init; }

    /// <summary>The valley's own haze at its floor at 550 nm, per metre (the fog/haze slider's haze there).</summary>
    public double HazeExtinction550 { get; init; }

    /// <summary>The humidity the day's haze grew by, % (<see cref="Haze.OpticalDepth"/>): the pools' haze grows from it to theirs.</summary>
    public double DayHumidityPercent { get; init; } = Haze.ReferenceHumidityPercent;

    /// <summary>The haze's scale height, m: a higher valley holds less of it.</summary>
    public double AerosolScaleHeightMetres { get; init; } = 1200.0;

    /// <summary>A pinned mist: the visibility at the floor of the reference valley's mist, km; null for auto.</summary>
    public double? MistVisibilityKm { get; init; }

    /// <summary>True when the mist is pinned off: haze pools only.</summary>
    public bool MistOff { get; init; }

    /// <summary>True when there is a night's pool to scale.</summary>
    public bool Any => DawnDepthMetres > 0.0;
}

/// <summary>One place's pool at the hour: its floor, how deep its pool and mist stand, and their densities.</summary>
/// <param name="FloorMetres">The valley floor, m above sea level.</param>
/// <param name="DepthMetres">The pool's depth above its floor at the hour, m (0 for none).</param>
/// <param name="MistMetres">The mist's depth above the floor, m (0 for none; never more than the pool's).</param>
/// <param name="HazeFloor550">The pool's haze at its floor at 550 nm, per metre, on top of the valley's own.</param>
/// <param name="HazeTop550">The same at the pool's top.</param>
/// <param name="MistWater">The mist's liquid water before the cap, g/m³ (<see cref="ValleyPools.MistWaterPerDegree"/> per °C its floor's air is below the dew point).</param>
public readonly record struct PoolCell(double FloorMetres, double DepthMetres, double MistMetres, double HazeFloor550, double HazeTop550, double MistWater)
{
    /// <summary>The pool's top, m above sea level.</summary>
    public double TopMetres => FloorMetres + DepthMetres;

    /// <summary>
    /// How far the mist's top stands above the cell's ground, m (negative where the ground rises through it; 0 without mist): small at
    /// a deck's edge, where it laps the slopes and frays (<see cref="Vista.Atmosphere.LowAirSettings.FrayMetres"/>).
    /// </summary>
    public double CoverMetres { get; init; }
}

/// <summary>
/// The valleys' pools at the hour (vista-plan §10 M1.13b): the night's pool of M1.13a's chain scaled by each valley's shape
/// (<see cref="PoolMap"/>), each on its own floor, with the haze it holds thinning upward and mist where its air reaches the dew point.
/// <list type="bullet">
///   <item><b>Each valley's pool</b>: the dawn pool a valley holds is the night's depth times its hold, at most its cap. Its cooling
///   scales with it (the inversion keeps its strength), so its heat deficit goes with its depth squared, and the sun breaks a shallow
///   pool sooner: what is left of it is <c>1 − q/r²</c>, with <c>q</c> the sun's heat against the full pool's deficit and <c>r</c> its
///   depth over the full pool's; after the day's high it forms again with the night's cooling, as the full one does. A pinned valley
///   haze or mist keeps every valley's whole pool.</item>
///   <item><b>Its air</b>: the free air above the pools is the scene's valley floor's air (the temperature slider, inside the reference
///   pool) warmed by that pool's cooling, falling with height along <see cref="Lapse.RateCelsiusPerMetre"/>; a valley's floor is colder
///   by its own pool's cooling, and the air warms up through the pool along the inversion's <see cref="ValleyHaze.InversionLapseCelsiusPerMetre"/>.
///   The dew point falls <see cref="DewPointLapseCelsiusPerMetre"/> with height inside a pool (its air mixed by the night's drainage),
///   and from one valley floor to another as the climate's water vapour does (<see cref="VapourTenfoldMetres"/>).</item>
///   <item><b>The haze it holds</b>: M1.13a's, per valley and per height: the valley's own haze (thinner on a higher floor), held under
///   the lid (up to <see cref="ValleyHaze.Trapping"/> times by dawn) and grown by the humidity at each height against the day's, so it is
///   densest at the humid floor and thins upward. A pinned valley haze sets its floor's density, and the profile keeps its shape.</item>
///   <item><b>Mist</b>: where the pool's air is colder than its dew point, the water condenses into droplets: from the floor up to where
///   the warming air and the falling dew point meet. The droplets' water grows with how far below its dew point the floor's air is
///   (<see cref="MistWaterPerDegree"/>), at most <see cref="MistWaterLimit"/> (fog proper is M4's), the same through the layer (its own
///   turbulence mixes it), and their extinction follows Kunkel's (1984) fit for fog, <c>β = 0.1447·LWC^0.88</c> per metre. A pinned mist sets the reference valley's mist to fill
///   <see cref="PinnedMistShare"/> of its pool, at the pinned visibility at its floor, and the other valleys follow from their own floors.</item>
/// </list>
///
/// <para>Pure: the same map and weather give the same pools, bit for bit.</para>
/// </summary>
public sealed class ValleyPools
{
    /// <summary>
    /// How fast the dew point falls with height inside a pool, °C per metre: about 1.8 °C per km, in air that keeps its water (the
    /// night's drainage flows mix the pool).
    /// </summary>
    public const double DewPointLapseCelsiusPerMetre = 0.0018;

    /// <summary>
    /// How far up the air's water vapour falls tenfold, m (Hann's formula, <c>e = e₀·10^(−z/6300)</c>): what carries a dew point from
    /// one valley floor to another, or from the humidity's station to the scene's valley floor (about 5 °C per km near 0 °C).
    /// </summary>
    public const double VapourTenfoldMetres = 6300.0;

    /// <summary>The dew point <paramref name="metres"/> above a place whose dew point is <paramref name="dewPointCelsius"/>, by <see cref="VapourTenfoldMetres"/>.</summary>
    public static double DewPointAbove(double dewPointCelsius, double metres) =>
        Humidity.DewPointCelsius(Humidity.SaturationVapourPressureHpa(dewPointCelsius) * Math.Pow(10.0, -metres / VapourTenfoldMetres));

    /// <summary>The mist's liquid water per °C its air is below the dew point, g/m³: about a sixth of what the cooling condenses near 0 °C (the rest settles as dew).</summary>
    public const double MistWaterPerDegree = 0.06;

    /// <summary>The most liquid water the mist holds, g/m³: about 200 m to see (the meteorological optical range); denser is fog proper, M4's.</summary>
    public const double MistWaterLimit = 0.075;

    /// <summary>Kunkel's (1984) fog extinction: per metre at 1 g/m³ of liquid water.</summary>
    public const double KunkelCoefficient = 0.1447;

    /// <summary>Kunkel's exponent of the liquid water.</summary>
    public const double KunkelExponent = 0.88;

    /// <summary>The share of the reference valley's pool a pinned mist fills.</summary>
    public const double PinnedMistShare = 0.6;

    private ValleyPools(PoolWeather weather, IReadOnlyList<PoolLevel> levels, IReadOnlyList<PoolCell[]> cells, PoolStats stats)
    {
        Weather = weather;
        Levels = levels;
        Cells = cells;
        Stats = stats;
    }

    /// <summary>No pools.</summary>
    public static ValleyPools None { get; } = new(PoolWeather.None, [], [], PoolStats.None);

    /// <summary>The weather they came from.</summary>
    public PoolWeather Weather { get; }

    /// <summary>The map's levels the cells lie on, finest first (empty for none).</summary>
    public IReadOnlyList<PoolLevel> Levels { get; }

    /// <summary>Each level's cells, row-major like the level.</summary>
    public IReadOnlyList<PoolCell[]> Cells { get; }

    /// <summary>What they add up to, for the sidecar.</summary>
    public PoolStats Stats { get; }

    /// <summary>True when any valley holds a pool.</summary>
    public bool Any => Stats.PooledShare > 0.0;

    /// <summary>The mist's extinction at liquid water <paramref name="lwc"/> (g/m³, capped at <see cref="MistWaterLimit"/>), per metre.</summary>
    public static double MistExtinction(double lwc) =>
        lwc > 0.0 ? KunkelCoefficient * Math.Pow(Math.Min(lwc, MistWaterLimit), KunkelExponent) : 0.0;

    /// <summary>The liquid water whose mist gives <paramref name="visibilityKm"/> to see, g/m³ (<see cref="MistExtinction"/> inverted, uncapped).</summary>
    public static double MistWaterFor(double visibilityKm) =>
        Math.Pow(Haze.VisibilityConstant / (visibilityKm * 1000.0) / KunkelCoefficient, 1.0 / KunkelExponent);

    /// <summary>
    /// What is left of a valley's dawn pool <paramref name="r"/> times as deep as the full one (0 to 1) at the hour of
    /// <paramref name="weather"/>: <c>1 − q/r²</c> before the day's high, that or the share formed since after it.
    /// </summary>
    public static double Share(PoolWeather weather, double r)
    {
        ArgumentNullException.ThrowIfNull(weather);
        if (weather.WholePool)
            return 1.0;
        if (!(r > 0.0))
            return 0.0;
        double left = Math.Clamp(1.0 - weather.HeatRatio / (r * r), 0.0, 1.0);
        return weather.Morning ? left : Math.Max(left, Math.Clamp(weather.Cooled, 0.0, 1.0));
    }

    /// <summary>
    /// The pool over a valley whose floor stands at <paramref name="floorMetres"/> above sea level, holding <paramref name="hold"/> of the
    /// night's pool at most <paramref name="capMetres"/> deep, at the hour of <paramref name="weather"/>.
    /// </summary>
    public static PoolCell Cell(PoolWeather weather, double floorMetres, double hold, double capMetres)
    {
        ArgumentNullException.ThrowIfNull(weather);
        double full = weather.DawnDepthMetres;
        double dawn = Math.Min(full * Math.Clamp(hold, 0.0, 1.0), Math.Max(capMetres, 0.0));
        double depth = full > 0.0 ? dawn * Share(weather, dawn / full) : 0.0;
        if (!(depth > 0.0))
            return new PoolCell(floorMetres, 0.0, 0.0, 0.0, 0.0, 0.0);

        // The air: the reference pool's cooling at the hour warms the free air over the scene's floor; this valley's own cools its floor.
        const double closing = ValleyHaze.InversionLapseCelsiusPerMetre + Lapse.RateCelsiusPerMetre;
        double above = floorMetres - weather.FloorMetres;
        double reference = full * Share(weather, 1.0);
        double free = weather.TemperatureCelsius + closing * reference - Lapse.RateCelsiusPerMetre * above;
        double floorAir = free - closing * depth;
        (double dewPoint, double water) = Dew(weather, above);

        double TemperatureAt(double z) => floorAir + ValleyHaze.InversionLapseCelsiusPerMetre * z;
        double DewAt(double z) => dewPoint - DewPointLapseCelsiusPerMetre * z;

        // The haze: the valley's own, held and grown by the humidity at each height against the day's.
        double held = 1.0 + (ValleyHaze.Trapping - 1.0) * Share(weather, dawn / full);
        double day = Haze.OpticalDepth(weather.DayHumidityPercent);
        double Grown(double z) => held * Haze.OpticalDepth(Humidity.RelativePercent(TemperatureAt(z), DewAt(z))) / day;
        double hazeFloor, hazeTop;
        if (weather.PinnedExtinction550 is double pinned)
        {
            double floorShape = Grown(0.0);
            hazeFloor = pinned;
            hazeTop = floorShape > 0.0 ? pinned * Grown(depth) / floorShape : pinned;
        }
        else
        {
            double own = weather.HazeExtinction550 * Math.Exp(-above / weather.AerosolScaleHeightMetres);
            hazeFloor = own * Math.Max(Grown(0.0) - 1.0, 0.0);
            hazeTop = own * Math.Max(Grown(depth) - 1.0, 0.0);
        }

        // The mist: up to where the air warms past its dew point.
        double excess = DewAt(0.0) - floorAir;
        double mist = excess > 0.0 && !weather.MistOff
            ? Math.Min(excess / (ValleyHaze.InversionLapseCelsiusPerMetre + DewPointLapseCelsiusPerMetre), depth)
            : 0.0;
        return new PoolCell(floorMetres, depth, mist, hazeFloor, hazeTop, mist > 0.0 ? water * excess : 0.0);
    }

    /// <summary>
    /// The dew point at a valley floor <paramref name="above"/> m over the scene's, and the mist's water per °C below it: the weather's,
    /// or for a pinned mist the reference valley's dew point and water set so its mist fills <see cref="PinnedMistShare"/> of its pool
    /// at the hour, at the pinned visibility at its floor.
    /// </summary>
    private static (double DewPoint, double WaterPerDegree) Dew(PoolWeather weather, double above)
    {
        if (weather.MistVisibilityKm is not double pinned || weather.MistOff)
            return (DewPointAbove(weather.DewPointCelsius, above), MistWaterPerDegree);
        double reference = weather.DawnDepthMetres * Share(weather, 1.0);
        double excess = (ValleyHaze.InversionLapseCelsiusPerMetre + DewPointLapseCelsiusPerMetre) * PinnedMistShare * reference;
        return (DewPointAbove(weather.TemperatureCelsius + excess, above), excess > 0.0 ? MistWaterFor(pinned) / excess : 0.0);
    }

    /// <summary>The pools of <paramref name="map"/> under <paramref name="weather"/>, the world standing <paramref name="baseMetres"/> above sea level.</summary>
    public static ValleyPools Build(PoolMap map, PoolWeather weather, double baseMetres)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(weather);
        if (!weather.Any)
            return None;
        var cells = new List<PoolCell[]>(map.Levels.Count);
        foreach (PoolLevel level in map.Levels)
        {
            var row = new PoolCell[level.Size * level.Size];
            Parallel.For(0, level.Size, j =>
            {
                for (int k = j * level.Size; k < (j + 1) * level.Size; k++)
                {
                    PoolCell c = Cell(weather, baseMetres + level.Floor[k], level.Hold[k], level.Cap[k]);
                    double ground = level.Ground.Length == row.Length ? baseMetres + level.Ground[k] : c.FloorMetres;
                    row[k] = c.MistMetres > 0.0 ? c with { CoverMetres = c.FloorMetres + c.MistMetres - ground } : c;
                }
            });
            cells.Add(row);
        }
        return new ValleyPools(weather, map.Levels, cells, PoolStats.Of(map.Levels[0], cells[0], baseMetres));
    }

    /// <summary>
    /// The pools' haze as one layer at each altitude, for the atmosphere's tables (vista-plan §10 M1.13b: M1.13a's flat layer becomes
    /// the altitude average the tables carry): the world's pools' haze averaged over its map, matched by a uniform layer with the same
    /// column, mean altitude and spread (a box <c>√12·σ</c> deep about the mean). Mist is left out: it is far too dense for the tables,
    /// and the march takes it alone. Null with no haze.
    /// </summary>
    public (double TopMetres, double Extinction550)? AverageLayer()
    {
        if (!Any)
            return null;
        PoolCell[] cells = Cells[0];
        double column = 0.0, moment = 0.0, second = 0.0;
        foreach (PoolCell c in cells)
        {
            if (!(c.DepthMetres > 0.0))
                continue;
            // The linear profile's column, and its first and second moments about the floor.
            double d = c.DepthMetres, a = c.HazeFloor550, b = c.HazeTop550 - c.HazeFloor550;
            double m0 = d * (a + b / 2.0);
            double m1 = d * d * (a / 2.0 + b / 3.0);
            double m2 = d * d * d * (a / 3.0 + b / 4.0);
            column += m0;
            moment += m1 + c.FloorMetres * m0;
            second += m2 + 2.0 * c.FloorMetres * m1 + c.FloorMetres * c.FloorMetres * m0;
        }
        if (!(column > 0.0))
            return null;
        double mean = moment / column;
        double spread = Math.Sqrt(Math.Max(second / column - mean * mean, 0.0));
        double depth = Math.Max(Math.Sqrt(12.0) * spread, 1.0);
        return (mean + depth / 2.0, column / cells.Length / depth);
    }

    /// <summary>The cells packed for the GPU, four words a sample, every level in turn (see <see cref="Pack"/>).</summary>
    public uint[] Packed(double hazeUnitPerExtinction)
    {
        int count = Cells.Sum(c => c.Length);
        var packed = new uint[count * 4];
        int at = 0;
        foreach (PoolCell[] level in Cells)
        {
            foreach (PoolCell c in level)
            {
                Pack(c, hazeUnitPerExtinction, packed.AsSpan(at, 4));
                at += 4;
            }
        }
        return packed;
    }

    /// <summary>
    /// One cell in four words: the floor's altitude (single precision), the pool's and the mist's depths (half each), the haze at the
    /// floor and the top in the haze's unit (<paramref name="hazeUnitPerExtinction"/> per unit of extinction at 550 nm, half each), the
    /// mist's water (half) and how far its top stands above the cell's ground (half).
    /// </summary>
    public static void Pack(PoolCell cell, double hazeUnitPerExtinction, Span<uint> into)
    {
        into[0] = BitConverter.SingleToUInt32Bits((float)cell.FloorMetres);
        into[1] = Halves(cell.DepthMetres, cell.MistMetres);
        into[2] = Halves(cell.HazeFloor550 * hazeUnitPerExtinction, cell.HazeTop550 * hazeUnitPerExtinction);
        into[3] = Halves(cell.MistWater, cell.CoverMetres);
    }

    /// <summary>Two values as half-precision bits, the first in the low half.</summary>
    public static uint Halves(double low, double high) =>
        BitConverter.HalfToUInt16Bits((Half)low) | ((uint)BitConverter.HalfToUInt16Bits((Half)high) << 16);

    /// <summary>A 64-bit fingerprint of the cells' bits, for the render's key.</summary>
    public ulong ContentHash()
    {
        ulong hash = StableHash.FnvStart;
        foreach (PoolCell[] level in Cells)
        {
            foreach (PoolCell c in level)
            {
                foreach (double v in new[] { c.FloorMetres, c.DepthMetres, c.MistMetres, c.HazeFloor550, c.HazeTop550, c.MistWater, c.CoverMetres })
                    hash = StableHash.Combine(hash, BitConverter.DoubleToUInt64Bits(v));
            }
        }
        return hash;
    }
}

/// <summary>What a world's pools add up to at the hour (the sidecar's <c>pools</c>), over its own map (the finest level).</summary>
/// <param name="PooledShare">The share of the world's ground under a pool (its cell's mean height below the pool's top), 0 to 1.</param>
/// <param name="MistShare">The share under mist.</param>
/// <param name="DeepestMetres">The deepest pool, m.</param>
/// <param name="DeepestMistMetres">The deepest mist, m.</param>
/// <param name="LowestTopMetres">The lowest pool top, m above sea level.</param>
/// <param name="HighestTopMetres">The highest pool top, m above sea level.</param>
/// <param name="ThinnestMistKm">The shortest visibility in any mist, at its floor, km (0 with no mist).</param>
public sealed record PoolStats(double PooledShare, double MistShare, double DeepestMetres, double DeepestMistMetres, double LowestTopMetres,
    double HighestTopMetres, double ThinnestMistKm)
{
    /// <summary>No pools.</summary>
    public static PoolStats None { get; } = new(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

    /// <summary>The stats of <paramref name="cells"/> on <paramref name="level"/>, the world standing <paramref name="baseMetres"/> above sea level.</summary>
    public static PoolStats Of(PoolLevel level, PoolCell[] cells, double baseMetres)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(cells);
        int pooled = 0, misty = 0;
        double deepest = 0.0, deepestMist = 0.0, low = double.MaxValue, high = double.MinValue, water = 0.0;
        for (int k = 0; k < cells.Length; k++)
        {
            PoolCell c = cells[k];
            if (!(c.DepthMetres > 0.0))
                continue;
            deepest = Math.Max(deepest, c.DepthMetres);
            low = Math.Min(low, c.TopMetres);
            high = Math.Max(high, c.TopMetres);
            double ground = level.Ground.Length == cells.Length ? baseMetres + level.Ground[k] : c.FloorMetres;
            if (c.TopMetres > ground)
                pooled++;
            if (c.MistMetres > 0.0)
            {
                if (c.FloorMetres + c.MistMetres > ground)
                    misty++;
                deepestMist = Math.Max(deepestMist, c.MistMetres);
                water = Math.Max(water, c.MistWater);
            }
        }
        if (deepest == 0.0)
            return None;
        double thinnest = water > 0.0 ? Haze.VisibilityConstant / ValleyPools.MistExtinction(water) / 1000.0 : 0.0;
        return new PoolStats((double)pooled / cells.Length, (double)misty / cells.Length, deepest, deepestMist, low, high, thinnest);
    }

    /// <summary>A line for the slider's reading.</summary>
    public string Describe() => PooledShare > 0.0
        ? string.Create(CultureInfo.InvariantCulture,
            $"pools over {PooledShare:0%} of the world, up to {DeepestMetres:0} m deep, their tops {LowestTopMetres:0}–{HighestTopMetres:0} m; mist over {MistShare:0%}")
        : "no pools";
}
