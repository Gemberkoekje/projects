using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Vista.World;

/// <summary>Who made a cached world, read back with it.</summary>
/// <param name="Device">The device that eroded it.</param>
/// <param name="Driver">That device's driver version (empty when it has none).</param>
/// <param name="CreatedUtc">When it was eroded (UTC, ISO 8601).</param>
/// <param name="BaseHash">The un-eroded height field's <see cref="HeightField.ContentHash"/>.</param>
public sealed record WorldOrigin(string Device, string Driver, string CreatedUtc, ulong BaseHash);

/// <summary>
/// Eroded worlds on disk (vista-plan §10 M1 decisions: "the eroded field is cached per (recipe, seed)"), so a world is eroded
/// once per seed and every later render, sheet or dry run — on any machine that has the file — reads the same bits. The key is
/// everything the erosion reads: the terrain profile, the seed, the grid, the erosion settings and the versions of the terrain
/// generator and of the passes. A file holds the surface in full precision, the maps in half precision
/// (<see cref="ErosionResult.ToHalf"/>) and the drainage's directions and fill depths (its upstream areas follow from the
/// directions when it is read), with the hash of the whole and who made it; a file whose hash does not match is
/// treated as missing. Writes go to a temporary file first, so a crash never leaves half a world. The folder keeps at most
/// <see cref="MaxBytes"/>; the least recently used worlds go first.
/// </summary>
public sealed class ErosionCache
{
    /// <summary>The environment variable that moves the cache (default <c>%LOCALAPPDATA%\Vista\worlds</c>).</summary>
    public const string DirectoryVariable = "VISTA_WORLD_CACHE";

    /// <summary>
    /// The version of the erosion passes. Bump it whenever <see cref="ErosionReference"/> and the shader, or the incision before
    /// them (<see cref="StreamPower"/>), change what they compute, so worlds eroded by the old passes are not read as the new
    /// ones'. 2 (M1.11a): the stream-power incision.
    /// </summary>
    public const int PassesVersion = 2;

    /// <summary>
    /// The version of the base terrain generator, for the same reason (its pinned hashes in the tests catch a change).
    /// </summary>
    public const int TerrainVersion = 1;

    /// <summary>The file format version. 2 (M1.11a) added the drainage: its directions (a byte a cell) and fill depths (halves).</summary>
    public const int FormatVersion = 2;

    /// <summary>The file extension.</summary>
    public const string Extension = ".vworld";

    private static ReadOnlySpan<byte> Magic => "VISTAWLD"u8;

    /// <summary>A cache in <paramref name="directory"/>.</summary>
    public ErosionCache(string directory, long maxBytes = 8L << 30)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory = directory;
        MaxBytes = maxBytes;
    }

    /// <summary>The cache the environment names, else <c>%LOCALAPPDATA%\Vista\worlds</c>.</summary>
    public static ErosionCache Default => new(Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } dir
        ? dir
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vista", "worlds"));

    /// <summary>The folder.</summary>
    public string Directory { get; }

    /// <summary>The most the folder keeps, in bytes.</summary>
    public long MaxBytes { get; }

    /// <summary>The key of a world: a hash of everything its erosion reads.</summary>
    public static ulong Key(TerrainProfile profile, ulong seed, WorldGrid grid, ErosionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(settings);
        static string Bits(double value) => BitConverter.DoubleToInt64Bits(value).ToString("x16", CultureInfo.InvariantCulture);
        string text = string.Join('|',
            profile.Name, Bits(profile.ReliefMetres), Bits(profile.WavelengthMetres), profile.Octaves.ToString(CultureInfo.InvariantCulture),
            Bits(profile.Persistence), profile.Ridged ? "ridged" : "fbm", Bits(profile.WarpFraction),
            seed.ToString(CultureInfo.InvariantCulture), grid.Size.ToString(CultureInfo.InvariantCulture), Bits(grid.CellSize),
            settings.ContentHash().ToString("x16", CultureInfo.InvariantCulture),
            PassesVersion.ToString(CultureInfo.InvariantCulture), TerrainVersion.ToString(CultureInfo.InvariantCulture));
        return StableHash.Of(text);
    }

    /// <summary>The file of a world.</summary>
    public string PathFor(TerrainProfile profile, ulong seed, WorldGrid grid, ErosionSettings settings) =>
        Path.Combine(Directory, string.Create(CultureInfo.InvariantCulture,
            $"{profile.Name}-{seed}-{grid.Size}x{grid.CellSize:0.###}-{Key(profile, seed, grid, settings):x16}{Extension}"));

    /// <summary>
    /// The cached world, or null when there is none (or the file is damaged: <paramref name="problem"/> then says how, and the
    /// world should be eroded again).
    /// </summary>
    public (ErosionResult Result, WorldOrigin Origin)? TryLoad(TerrainProfile profile, ulong seed, WorldGrid grid, ErosionSettings settings, out string? problem)
    {
        problem = null;
        string path = PathFor(profile, seed, grid, settings);
        if (!File.Exists(path))
            return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic) || reader.ReadInt32() != FormatVersion)
                throw new InvalidDataException("not a Vista world file of this version");
            if (reader.ReadUInt64() != Key(profile, seed, grid, settings) || reader.ReadInt32() != grid.Size || reader.ReadDouble() != grid.CellSize)
                throw new InvalidDataException("it holds another world");
            int steps = reader.ReadInt32();
            ulong baseHash = reader.ReadUInt64();
            ulong hash = reader.ReadUInt64();
            var origin = new WorldOrigin(reader.ReadString(), reader.ReadString(), reader.ReadString(), baseHash);
            int cells = grid.Size * grid.Size;
            float[] heights = ReadFloats(stream, cells);
            float[][] maps = [ReadHalves(stream, cells), ReadHalves(stream, cells), ReadHalves(stream, cells), ReadHalves(stream, cells)];
            var directions = new byte[cells];
            stream.ReadExactly(directions);
            var drainage = new DrainageMap(grid.Size, grid.CellSize, directions, ReadHalves(stream, cells));
            var result = new ErosionResult(new HeightField(grid.Size, grid.CellSize, heights), maps[0], maps[1], maps[2], maps[3], steps, drainage);
            if (result.ContentHash() != hash)
                throw new InvalidDataException("its contents do not match its hash");
            Touch(path);
            return (result, origin);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            problem = $"The cached world {path} could not be read ({e.Message}); eroding it again.";
            return null;
        }
    }

    /// <summary>Writes <paramref name="result"/>; returns its path. Then trims the folder to <see cref="MaxBytes"/>.</summary>
    public string Store(TerrainProfile profile, ulong seed, WorldGrid grid, ErosionSettings settings, ErosionResult result, WorldOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(origin);
        if (result.Surface.Size != grid.Size || result.Surface.CellSize != grid.CellSize)
            throw new ArgumentException("The result is not on the grid.", nameof(result));
        System.IO.Directory.CreateDirectory(Directory);
        string path = PathFor(profile, seed, grid, settings);
        string temporary = path + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(Key(profile, seed, grid, settings));
            writer.Write(grid.Size);
            writer.Write(grid.CellSize);
            writer.Write(result.Steps);
            writer.Write(origin.BaseHash);
            writer.Write(result.ContentHash());
            writer.Write(origin.Device);
            writer.Write(origin.Driver);
            writer.Write(origin.CreatedUtc);
            writer.Flush();
            WriteFloats(stream, result.Surface.Heights);
            foreach (float[] map in new[] { result.Loose, result.Discharge, result.WaterDepth, result.StandingWater })
                WriteHalves(stream, map);
            stream.Write(result.Drainage.Direction);
            WriteHalves(stream, result.Drainage.FillDepth);
        }
        File.Move(temporary, path, overwrite: true);
        Trim(keep: path);
        return path;
    }

    private void Trim(string keep)
    {
        var files = new DirectoryInfo(Directory).GetFiles("*" + Extension).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        long total = 0;
        foreach (FileInfo file in files)
        {
            total += file.Length;
            if (total > MaxBytes && !string.Equals(file.FullName, Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase))
                file.Delete();
        }
    }

    // A read marks the world as recently used.
    private static void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (IOException)
        {
            // Another process has it open; the order of eviction is only a preference.
        }
    }

    private static void WriteFloats(Stream stream, ReadOnlySpan<float> values) => stream.Write(MemoryMarshal.AsBytes(values));

    private static void WriteHalves(Stream stream, float[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteHalfLittleEndian(bytes.AsSpan(i * 2), (Half)values[i]);
        stream.Write(bytes);
    }

    private static float[] ReadFloats(Stream stream, int count)
    {
        var values = new float[count];
        stream.ReadExactly(MemoryMarshal.AsBytes(values.AsSpan()));
        return values;
    }

    private static float[] ReadHalves(Stream stream, int count)
    {
        var bytes = new byte[count * 2];
        stream.ReadExactly(bytes);
        var values = new float[count];
        for (int i = 0; i < count; i++)
            values[i] = (float)BinaryPrimitives.ReadHalfLittleEndian(bytes.AsSpan(i * 2));
        return values;
    }
}
