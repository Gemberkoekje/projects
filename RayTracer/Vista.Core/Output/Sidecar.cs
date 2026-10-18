using System.Reflection;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;

namespace Vista.Output;

/// <summary>
/// The JSON file written next to every image (vista-plan §5.7 / §7.4): everything needed to reproduce it
/// — recipe, seed, camera, data tier per material, engine version — plus what it cost (rays, samples,
/// timings), which is what the preview's first calibration of the estimate reads back (§7.2).
/// M0 has no sliders yet; they join under a new <see cref="Schema"/> with M1.
/// </summary>
public sealed record Sidecar
{
    /// <summary>The sidecar format version this build writes.</summary>
    public const int CurrentSchema = 1;

    /// <summary>Format version.</summary>
    public int Schema { get; init; } = CurrentSchema;

    /// <summary>"preview" for a rendered preview, "dryRun" when no GPU renderer ran.</summary>
    public required string Kind { get; init; }

    /// <summary>When the file was written (UTC, ISO 8601).</summary>
    public required string CreatedUtc { get; init; }

    /// <summary>Which build and device made the image.</summary>
    public required EngineInfo Engine { get; init; }

    /// <summary>The recipe.</summary>
    public required Recipe Recipe { get; init; }

    /// <summary>The world seed.</summary>
    public required ulong Seed { get; init; }

    /// <summary>The camera actually used (the default camera when none was given).</summary>
    public required CameraSpec Camera { get; init; }

    /// <summary>True when <see cref="Camera"/> is the default camera rather than one the caller supplied.</summary>
    public bool DefaultCamera { get; init; }

    /// <summary>Image size and quality settings.</summary>
    public required ImageInfo Image { get; init; }

    /// <summary>World generation facts.</summary>
    public required WorldInfo World { get; init; }

    /// <summary>The sun the image was lit by.</summary>
    public required SunInfo Sun { get; init; }

    /// <summary>Data tier per material key (vista-plan §6.2).</summary>
    public required SortedDictionary<string, DataTier> DataTiers { get; init; }

    /// <summary>Renderer statistics; absent for a dry run.</summary>
    public RenderInfo? Render { get; init; }

    /// <summary>Pipeline stage timings in seconds (world generation, mesh, render, write).</summary>
    public required SortedDictionary<string, double> TimingsSeconds { get; init; }

    /// <summary>Output role → file name (relative to the sidecar).</summary>
    public required SortedDictionary<string, string> Outputs { get; init; }

    /// <summary>Non-fatal problems, e.g. a camera below the terrain.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Build and device identification.</summary>
/// <param name="VistaVersion">Informational version of Vista.Core (includes the commit when the SDK knows it).</param>
/// <param name="Runtime">.NET runtime description.</param>
/// <param name="Device">GPU adapter name; null for a dry run.</param>
public sealed record EngineInfo(string VistaVersion, string Runtime, string? Device)
{
    /// <summary>The running build, on <paramref name="device"/>.</summary>
    public static EngineInfo Current(string? device) => new(
        typeof(Sidecar).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
        System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        device);
}

/// <summary>Image size and quality settings.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="SamplesPerPixel">Samples per pixel.</param>
/// <param name="MaxBounces">Maximum bounces.</param>
/// <param name="DisplayTransform">How the PNG was display-encoded.</param>
public sealed record ImageInfo(int Width, int Height, int SamplesPerPixel, int MaxBounces, string DisplayTransform);

/// <summary>World generation facts.</summary>
/// <param name="TerrainProfile">Terrain profile key.</param>
/// <param name="GridSize">Height-field samples per side.</param>
/// <param name="CellSizeMetres">Sample spacing.</param>
/// <param name="HeightFieldHash">Content hash of the height field (hex) — equal hashes mean the same world.</param>
/// <param name="MinHeightMetres">Lowest terrain point.</param>
/// <param name="MaxHeightMetres">Highest terrain point.</param>
/// <param name="Triangles">Terrain mesh triangles.</param>
/// <param name="MeshBytes">Terrain mesh upload size.</param>
public sealed record WorldInfo(
    string TerrainProfile,
    int GridSize,
    double CellSizeMetres,
    string HeightFieldHash,
    double MinHeightMetres,
    double MaxHeightMetres,
    long Triangles,
    long MeshBytes);

/// <summary>The sun.</summary>
/// <param name="Source">Where the sun came from (the M0 placeholder, later real astronomy).</param>
/// <param name="Direction">Sunlight travel direction (the sun is toward its negation).</param>
/// <param name="ElevationDegrees">Elevation above the horizon.</param>
/// <param name="TemperatureK">Blackbody colour temperature.</param>
public sealed record SunInfo(string Source, WorldPoint Direction, double ElevationDegrees, double TemperatureK);

/// <summary>Renderer statistics as written to the sidecar.</summary>
/// <param name="Stats">Raw counts and timings.</param>
/// <param name="RaysPerSample">Mean rays per pixel-sample.</param>
/// <param name="RaysPerSecond">Rays per second of trace time.</param>
public sealed record RenderInfo(RenderStats Stats, double RaysPerSample, double RaysPerSecond)
{
    /// <summary>Wraps <paramref name="stats"/> with its derived rates.</summary>
    public static RenderInfo From(RenderStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return new RenderInfo(stats, stats.RaysPerSample(), stats.RaysPerSecond());
    }
}
