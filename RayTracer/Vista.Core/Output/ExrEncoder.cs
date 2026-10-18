using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Vista.Output;

/// <summary>EXR sample types the encoder writes.</summary>
public enum ExrPixelType
{
    /// <summary>16-bit IEEE half float — the usual choice for scene-referred colour.</summary>
    Half = 1,
    /// <summary>32-bit IEEE float.</summary>
    Float = 2,
}

/// <summary>EXR compression schemes the encoder writes.</summary>
public enum ExrCompression
{
    /// <summary>No compression.</summary>
    None = 0,
    /// <summary>zlib, one scanline per block.</summary>
    Zips = 2,
    /// <summary>zlib, sixteen scanlines per block.</summary>
    Zip = 3,
}

/// <summary>One named image channel: <c>width × height</c> floats, row 0 at the top.</summary>
/// <param name="Name">Channel name, e.g. "R".</param>
/// <param name="Data">Samples, row-major.</param>
public sealed record ExrChannel(string Name, float[] Data);

/// <summary>
/// A dependency-free OpenEXR writer (vista-plan §5.7): single-part scanline files, HALF or FLOAT channels,
/// NONE / ZIPS / ZIP compression, tagged with Rec.709 chromaticities (linear sRGB primaries, D65). Enough for
/// the scene-referred beauty output; tiled, multi-part and deep files are out of scope.
/// </summary>
public static class ExrEncoder
{
    private const int Magic = 20000630;
    private const int Version = 2; // single-part scanline, short names

    /// <summary>Encodes interleaved linear RGB (3 floats per pixel) as channels R, G, B.</summary>
    public static byte[] EncodeRgb(
        ReadOnlySpan<float> rgb, int width, int height,
        ExrPixelType type = ExrPixelType.Half, ExrCompression compression = ExrCompression.Zip)
    {
        int pixels = width * height;
        if (rgb.Length != pixels * 3)
            throw new ArgumentException($"Expected {pixels * 3} floats, got {rgb.Length}.", nameof(rgb));
        var r = new float[pixels];
        var g = new float[pixels];
        var b = new float[pixels];
        for (int p = 0; p < pixels; p++)
        {
            r[p] = rgb[p * 3];
            g[p] = rgb[p * 3 + 1];
            b[p] = rgb[p * 3 + 2];
        }
        return Encode(width, height, [new("R", r), new("G", g), new("B", b)], type, compression);
    }

    /// <summary>Encodes <paramref name="channels"/> (any order; stored sorted by name as the format requires).</summary>
    public static byte[] Encode(
        int width, int height, IReadOnlyList<ExrChannel> channels,
        ExrPixelType type = ExrPixelType.Half, ExrCompression compression = ExrCompression.Zip)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(channels);
        if (channels.Count == 0)
            throw new ArgumentException("At least one channel.", nameof(channels));
        if (type is not (ExrPixelType.Half or ExrPixelType.Float))
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported pixel type.");
        if (compression is not (ExrCompression.None or ExrCompression.Zips or ExrCompression.Zip))
            throw new ArgumentOutOfRangeException(nameof(compression), compression, "Unsupported compression.");
        var sorted = channels.OrderBy(c => c.Name, StringComparer.Ordinal).ToArray();
        foreach (ExrChannel c in sorted)
        {
            if (string.IsNullOrEmpty(c.Name) || c.Name.Length > 31 || c.Name.Contains('\0'))
                throw new ArgumentException($"Invalid channel name '{c.Name}'.", nameof(channels));
            if (c.Data.Length != width * height)
                throw new ArgumentException($"Channel '{c.Name}' has {c.Data.Length} samples, expected {width * height}.", nameof(channels));
        }
        if (sorted.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != sorted.Length)
            throw new ArgumentException("Channel names must be unique.", nameof(channels));

        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file, Encoding.ASCII, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        WriteHeader(writer, width, height, sorted, type, compression);

        int linesPerBlock = compression == ExrCompression.Zip ? 16 : 1;
        int blocks = (height + linesPerBlock - 1) / linesPerBlock;
        long offsetTable = file.Position;
        for (int k = 0; k < blocks; k++)
            writer.Write(0L); // patched below

        int bytesPerSample = type == ExrPixelType.Half ? 2 : 4;
        var offsets = new long[blocks];
        for (int k = 0; k < blocks; k++)
        {
            int y0 = k * linesPerBlock;
            int lines = Math.Min(linesPerBlock, height - y0);
            byte[] raw = new byte[lines * width * sorted.Length * bytesPerSample];
            int at = 0;
            for (int y = y0; y < y0 + lines; y++)
            {
                foreach (ExrChannel c in sorted)
                {
                    ReadOnlySpan<float> line = c.Data.AsSpan(y * width, width);
                    foreach (float v in line)
                    {
                        if (type == ExrPixelType.Half)
                            BinaryPrimitives.WriteHalfLittleEndian(raw.AsSpan(at), (Half)v);
                        else
                            BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(at), v);
                        at += bytesPerSample;
                    }
                }
            }

            byte[] payload = compression == ExrCompression.None ? raw : ZipBlock(raw);
            offsets[k] = file.Position;
            writer.Write(y0);
            writer.Write(payload.Length);
            writer.Write(payload);
        }

        file.Position = offsetTable;
        foreach (long offset in offsets)
            writer.Write(offset);
        writer.Flush();
        return file.ToArray();
    }

    /// <summary>Encodes and writes an EXR file of interleaved linear RGB.</summary>
    public static void WriteRgb(string path, ReadOnlySpan<float> rgb, int width, int height,
        ExrPixelType type = ExrPixelType.Half, ExrCompression compression = ExrCompression.Zip) =>
        File.WriteAllBytes(path, EncodeRgb(rgb, width, height, type, compression));

    private static void WriteHeader(BinaryWriter w, int width, int height, ExrChannel[] channels, ExrPixelType type, ExrCompression compression)
    {
        using (var chlist = new MemoryStream())
        using (var c = new BinaryWriter(chlist, Encoding.ASCII))
        {
            foreach (ExrChannel channel in channels)
            {
                WriteCString(c, channel.Name);
                c.Write((int)type);
                c.Write((byte)0);              // pLinear
                c.Write((byte)0); c.Write((byte)0); c.Write((byte)0); // reserved
                c.Write(1);                    // xSampling
                c.Write(1);                    // ySampling
            }
            c.Write((byte)0);
            c.Flush();
            WriteAttribute(w, "channels", "chlist", chlist.ToArray());
        }

        // Rec.709 / sRGB primaries and the D65 white point: red, green, blue, white (x, y).
        WriteAttribute(w, "chromaticities", "chromaticities", Floats(0.64f, 0.33f, 0.30f, 0.60f, 0.15f, 0.06f, 0.3127f, 0.3290f));
        WriteAttribute(w, "compression", "compression", [(byte)compression]);
        byte[] window = Ints(0, 0, width - 1, height - 1);
        WriteAttribute(w, "dataWindow", "box2i", window);
        WriteAttribute(w, "displayWindow", "box2i", window);
        WriteAttribute(w, "lineOrder", "lineOrder", [0]); // increasing y
        WriteAttribute(w, "pixelAspectRatio", "float", Floats(1f));
        WriteAttribute(w, "screenWindowCenter", "v2f", Floats(0f, 0f));
        WriteAttribute(w, "screenWindowWidth", "float", Floats(1f));
        w.Write((byte)0); // end of header
    }

    // OpenEXR's ZIP predictor: split even/odd bytes into two halves, delta-encode, then zlib (RFC 1950).
    // Stored raw when compression would not shrink the block, which readers detect from the size.
    private static byte[] ZipBlock(byte[] raw)
    {
        var shuffled = new byte[raw.Length];
        int half = (raw.Length + 1) / 2;
        for (int i = 0; i < raw.Length; i++)
            shuffled[(i & 1) == 0 ? i / 2 : half + i / 2] = raw[i];
        for (int i = shuffled.Length - 1; i > 0; i--)
            shuffled[i] = unchecked((byte)(shuffled[i] - shuffled[i - 1] + 128));

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(shuffled);
        return compressed.Length < raw.Length ? compressed.ToArray() : raw;
    }

    private static void WriteAttribute(BinaryWriter w, string name, string type, byte[] value)
    {
        WriteCString(w, name);
        WriteCString(w, type);
        w.Write(value.Length);
        w.Write(value);
    }

    private static void WriteCString(BinaryWriter w, string text)
    {
        w.Write(Encoding.ASCII.GetBytes(text));
        w.Write((byte)0);
    }

    private static byte[] Floats(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), values[i]);
        return bytes;
    }

    private static byte[] Ints(params int[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
        return bytes;
    }
}
