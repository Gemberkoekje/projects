using System.Globalization;
using Vista.Recipes;

namespace Vista.World;

/// <summary>Where a world's erosion came from.</summary>
public enum ErosionSource
{
    /// <summary>Not set.</summary>
    None,

    /// <summary>Eroded just now, on <see cref="ErosionInfo.Device"/>.</summary>
    Eroded,

    /// <summary>Read from the cache; <see cref="ErosionInfo.Device"/> eroded it when <see cref="ErosionInfo.ErodedUtc"/> says.</summary>
    Cache,

    /// <summary>Not eroded: there was no cached world and nothing to erode it with (a dry run), so the un-eroded terrain stands in.</summary>
    Uneroded,

    /// <summary>Not eroded because the request said so.</summary>
    Off,
}

/// <summary>
/// The erosion of a world, for the sidecar (schema 8): what made it, with what, what it cost, how it drains (schema 13), and where
/// its rock's beds are (schema 16).
/// </summary>
public sealed record ErosionInfo
{
    /// <summary>Where it came from.</summary>
    public required ErosionSource Source { get; init; }

    /// <summary>The settings it ran (or would have run) with.</summary>
    public required ErosionSettings Settings { get; init; }

    /// <summary>The settings' <see cref="ErosionSettings.ContentHash"/>, hex.</summary>
    public required string SettingsHash { get; init; }

    /// <summary>The un-eroded height field's hash, hex (the eroded one's is <c>world.heightFieldHash</c>).</summary>
    public required string BaseHeightFieldHash { get; init; }

    /// <summary>The device that eroded it; null when it was not eroded.</summary>
    public string? Device { get; init; }

    /// <summary>That device's driver version; null when it was not eroded.</summary>
    public string? Driver { get; init; }

    /// <summary>When it was eroded (UTC, ISO 8601); null when it was not.</summary>
    public string? ErodedUtc { get; init; }

    /// <summary>The cache file it was read from or written to; null without a cache.</summary>
    public string? CacheFile { get; init; }

    /// <summary>What the erosion cost, when it ran in this process.</summary>
    public ErosionStats? Stats { get; init; }

    /// <summary>The eroded world's drainage measures (schema 13, vista-plan §10 M1.11a); null when it was not eroded.</summary>
    public DrainageStats? Drainage { get; init; }

    /// <summary>
    /// How the eroded world's heights map to its rock's frame (schema 16, vista-plan §10 M1.11d), so its beds can be found at any
    /// height (<see cref="RockHardness"/>); null when it was not eroded.
    /// </summary>
    public StrataFrame? Strata { get; init; }
}

/// <summary>A world ready to mesh: the surface, its erosion maps when it was eroded, and how it came about.</summary>
/// <param name="Surface">The ground to mesh and render.</param>
/// <param name="Erosion">The erosion result (the maps later stages read); null when not eroded.</param>
/// <param name="Info">Provenance for the sidecar.</param>
/// <param name="Warnings">Things the user should know (an un-eroded stand-in, a damaged cache file).</param>
public sealed record TerrainWorld(HeightField Surface, ErosionResult? Erosion, ErosionInfo Info, IReadOnlyList<string> Warnings);

/// <summary>
/// The world stage (vista-plan §4.1, §10 M1.7): the base terrain of the recipe and seed on the global grid, then its erosion —
/// read from the cache when it holds this world, else incised on the CPU (<see cref="StreamPower"/>, M1.11a), run on the eroder
/// (the GPU) and stored. With neither (a dry run with no cached world) the un-eroded terrain stands in, with a warning, so the
/// world side still runs anywhere.
/// </summary>
public static class WorldBuilder
{
    /// <summary>Builds the world of <paramref name="recipe"/> and <paramref name="seed"/> on <paramref name="grid"/>.</summary>
    public static TerrainWorld Build(Recipe recipe, ulong seed, WorldGrid grid, ITerrainEroder? eroder, ErosionCache? cache, bool erode = true,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(grid);
        time ??= TimeProvider.System;
        TerrainProfile profile = TerrainProfile.For(recipe.SetPieces);
        ErosionSettings settings = ErosionSettings.For(profile);
        string settingsHash = Hex(settings.ContentHash());
        var warnings = new List<string>();

        (ErosionResult Result, WorldOrigin Origin)? cached = null;
        if (erode && cache is not null)
        {
            cached = cache.TryLoad(profile, seed, grid, settings, out string? problem);
            if (problem is not null)
                warnings.Add(problem);
        }
        if (cached is { } hit)
        {
            return new TerrainWorld(hit.Result.Surface, hit.Result, new ErosionInfo
            {
                Source = ErosionSource.Cache,
                Settings = settings,
                SettingsHash = settingsHash,
                BaseHeightFieldHash = Hex(hit.Origin.BaseHash),
                Device = hit.Origin.Device,
                Driver = hit.Origin.Driver,
                ErodedUtc = hit.Origin.CreatedUtc,
                CacheFile = cache!.PathFor(profile, seed, grid, settings),
                Drainage = DrainageStats.Measure(hit.Result.Drainage),
                Strata = hit.Result.Strata,
            }, warnings);
        }

        HeightField ground = TerrainGenerator.Generate(recipe, seed, grid);
        string baseHash = Hex(ground.ContentHash());
        if (!erode || eroder is null)
        {
            if (erode)
                warnings.Add("Not eroded: no cached world for this recipe, seed and grid, and nothing to erode it with (a dry run has no GPU). The un-eroded terrain stands in.");
            return new TerrainWorld(ground, null, new ErosionInfo
            {
                Source = erode ? ErosionSource.Uneroded : ErosionSource.Off,
                Settings = settings,
                SettingsHash = settingsHash,
                BaseHeightFieldHash = baseHash,
            }, warnings);
        }

        var incision = System.Diagnostics.Stopwatch.StartNew();
        (HeightField incised, StrataGrid strata) = Prepare(ground, recipe, seed, settings);
        double incisionSeconds = incision.Elapsed.TotalSeconds;
        ErosionRun run = eroder.Erode(incised, settings, strata);
        string created = time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        string? file = cache?.Store(profile, seed, grid, settings, run.Result, new WorldOrigin(eroder.DeviceName, eroder.DriverVersion, created, ground.ContentHash()));
        return new TerrainWorld(run.Result.Surface, run.Result, new ErosionInfo
        {
            Source = ErosionSource.Eroded,
            Settings = settings,
            SettingsHash = settingsHash,
            BaseHeightFieldHash = baseHash,
            Device = eroder.DeviceName,
            Driver = eroder.DriverVersion,
            ErodedUtc = created,
            CacheFile = file,
            Stats = run.Stats with { IncisionSeconds = incisionSeconds },
            Drainage = DrainageStats.Measure(run.Result.Drainage),
            Strata = run.Result.Strata,
        }, warnings);
    }

    /// <summary>
    /// The ground the erosion starts from (<see cref="StreamPower.Prepare"/>) and its rock laid out on the world grid
    /// (<see cref="StrataGrid"/>), in the frame the incision left it in.
    /// </summary>
    public static (HeightField Ground, StrataGrid Strata) Prepare(HeightField ground, Recipe recipe, ulong seed, ErosionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(settings);
        (HeightField incised, StrataFrame frame, HeightField? uplift) = StreamPower.Prepare(ground, recipe, seed, settings);
        // The rock rose by the uplift the incision used, interpolated between its cells.
        StrataGrid strata = StrataGrid.For(RockHardness.For(seed, settings), ground.Size, ground.CellSize, frame,
            (x, z) => uplift is null ? 0.0 : uplift.HeightAt(x, z));
        return (incised, strata);
    }

    private static string Hex(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);
}
