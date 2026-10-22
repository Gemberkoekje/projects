using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Vista.Output;

/// <summary>PNG colour layouts the encoder writes (8 or 16 bits per channel).</summary>
public enum PngColor
{
    /// <summary>One grey channel.</summary>
    Gray = 0,
    /// <summary>Red, green, blue.</summary>
    Rgb = 2,
    /// <summary>Red, green, blue, alpha.</summary>
    Rgba = 6,
}

/// <summary>
/// A small, dependency-free PNG encoder (vista-plan §5.7) — <c>System.Drawing</c> is Windows-only and the
/// encoder must run in the net10.0 tests. 8-bit (or, since M1.11c, 16-bit), non-interlaced, tagged sRGB, with the usual per-row
/// adaptive filter choice (minimum sum of absolute differences) so large wallpapers compress well.
/// </summary>
public static class PngEncoder
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Channels per pixel for <paramref name="color"/>.</summary>
    public static int Channels(PngColor color) => color switch
    {
        PngColor.Gray => 1,
        PngColor.Rgb => 3,
        PngColor.Rgba => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, "Unsupported PNG colour type."),
    };

    /// <summary>Encodes tightly packed, top-row-first 8-bit <paramref name="pixels"/> as a PNG file.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> pixels, int width, int height, PngColor color) => Encode(pixels, width, height, color, bitDepth: 8);

    /// <summary>
    /// Encodes tightly packed, top-row-first 16-bit <paramref name="samples"/> as a 16-bit PNG file (vista-plan §10 M1.11c); the file
    /// holds them big-endian, as PNG's samples are.
    /// </summary>
    public static byte[] Encode16(ReadOnlySpan<ushort> samples, int width, int height, PngColor color)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i * 2), samples[i]);
        return Encode(bytes, width, height, color, bitDepth: 16);
    }

    /// <summary>Encodes and writes a 16-bit PNG file.</summary>
    public static void Write16(string path, ReadOnlySpan<ushort> samples, int width, int height, PngColor color) =>
        File.WriteAllBytes(path, Encode16(samples, width, height, color));

    // Raw big-endian samples of bitDepth bits; the filters work on bytes, bpp of them a pixel.
    private static byte[] Encode(ReadOnlySpan<byte> pixels, int width, int height, PngColor color, int bitDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int bpp = Channels(color) * bitDepth / 8;
        int stride = width * bpp;
        if (pixels.Length != (long)stride * height)
            throw new ArgumentException($"Expected {(long)stride * height / (bitDepth / 8)} samples, got {pixels.Length / (bitDepth / 8)}.", nameof(pixels));

        using var file = new MemoryStream();
        file.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = (byte)bitDepth;
        ihdr[9] = (byte)color;
        ihdr[10] = 0;           // deflate
        ihdr[11] = 0;           // adaptive filtering
        ihdr[12] = 0;           // no interlace
        WriteChunk(file, "IHDR", ihdr);

        // sRGB (perceptual intent) plus the gAMA / cHRM fallbacks the PNG spec recommends alongside it.
        WriteChunk(file, "sRGB", [0]);
        Span<byte> gama = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(gama, 45455);
        WriteChunk(file, "gAMA", gama);
        Span<byte> chrm = stackalloc byte[32];
        int[] chromaticities = [31270, 32900, 64000, 33000, 30000, 60000, 15000, 6000];
        for (int k = 0; k < chromaticities.Length; k++)
            BinaryPrimitives.WriteInt32BigEndian(chrm[(k * 4)..], chromaticities[k]);
        WriteChunk(file, "cHRM", chrm);

        WriteChunk(file, "IDAT", Compress(pixels, stride, height, bpp));
        WriteChunk(file, "IEND", []);
        return file.ToArray();
    }

    /// <summary>Encodes and writes a PNG file.</summary>
    public static void Write(string path, ReadOnlySpan<byte> pixels, int width, int height, PngColor color) =>
        File.WriteAllBytes(path, Encode(pixels, width, height, color));

    private static byte[] Compress(ReadOnlySpan<byte> pixels, int stride, int height, int bpp)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var candidates = new byte[5][];
            for (int f = 0; f < 5; f++)
                candidates[f] = new byte[stride + 1];
            byte[] zero = new byte[stride];

            for (int y = 0; y < height; y++)
            {
                ReadOnlySpan<byte> row = pixels.Slice(y * stride, stride);
                ReadOnlySpan<byte> prior = y > 0 ? pixels.Slice((y - 1) * stride, stride) : zero;
                int best = 0;
                long bestScore = long.MaxValue;
                for (int f = 0; f < 5; f++)
                {
                    long score = Filter(f, row, prior, bpp, candidates[f]);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = f;
                    }
                }
                zlib.Write(candidates[best]);
            }
        }
        return compressed.ToArray();
    }

    // Applies PNG filter type f (0 None, 1 Sub, 2 Up, 3 Average, 4 Paeth) into dest (type byte + row) and
    // returns the minimum-sum-of-absolute-differences score the spec suggests for choosing between them.
    private static long Filter(int f, ReadOnlySpan<byte> row, ReadOnlySpan<byte> prior, int bpp, Span<byte> dest)
    {
        dest[0] = (byte)f;
        long score = 0;
        for (int x = 0; x < row.Length; x++)
        {
            int a = x >= bpp ? row[x - bpp] : 0;
            int b = prior[x];
            int c = x >= bpp ? prior[x - bpp] : 0;
            int predictor = f switch
            {
                0 => 0,
                1 => a,
                2 => b,
                3 => (a + b) >> 1,
                _ => Paeth(a, b, c),
            };
            byte value = unchecked((byte)(row[x] - predictor));
            dest[x + 1] = value;
            score += value < 128 ? value : 256 - value; // |value as a signed byte|, without Math.Abs(sbyte.MinValue) overflowing
        }
        return score;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
            return a;
        return pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
        Encoding.ASCII.GetBytes(type, header[4..]);
        stream.Write(header);
        stream.Write(data);
        uint crc = Crc32.Update(Crc32.Update(0xFFFFFFFFu, header[4..]), data) ^ 0xFFFFFFFFu;
        Span<byte> trailer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(trailer, crc);
        stream.Write(trailer);
    }
}

/// <summary>The CRC-32 (IEEE 802.3, reflected 0xEDB88320) PNG chunks carry.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    /// <summary>Continues a CRC register (start at 0xFFFFFFFF, finish by XOR with 0xFFFFFFFF).</summary>
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
