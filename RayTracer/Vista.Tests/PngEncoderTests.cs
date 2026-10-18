using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Vista.Output;

namespace Vista.Tests;

/// <summary>
/// The PNG encoder, checked by an independent minimal decoder (chunk walk, CRC, inflate, un-filter) so a
/// round trip proves the bytes, not just the API. Out-of-band, the files also open in PIL and libpng.
/// </summary>
[TestClass]
public sealed class PngEncoderTests
{
    [TestMethod]
    public void Crc32_MatchesTheCheckValue() =>
        Assert.AreEqual(0xCBF43926u, Crc32.Update(0xFFFFFFFFu, Encoding.ASCII.GetBytes("123456789")) ^ 0xFFFFFFFFu);

    [TestMethod]
    [DataRow(PngColor.Gray, 1, 1)]
    [DataRow(PngColor.Rgb, 7, 5)]
    [DataRow(PngColor.Rgba, 33, 17)]
    [DataRow(PngColor.Rgb, 320, 180)]
    public void RoundTrips(PngColor color, int width, int height)
    {
        int channels = PngEncoder.Channels(color);
        var pixels = new byte[width * height * channels];
        // Gradients, a hard edge and noise — exercises every filter type.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < channels; c++)
                {
                    int i = (y * width + x) * channels + c;
                    pixels[i] = (byte)(x < width / 2 ? x * 7 + y * 3 + c * 50 : (int)(World.StableHash.Mix((ulong)i) & 0xFF));
                }
            }
        }

        byte[] png = PngEncoder.Encode(pixels, width, height, color);
        (int w, int h, PngColor decodedColor, byte[] decoded, List<string> chunks) = Decode(png);
        Assert.AreEqual(width, w);
        Assert.AreEqual(height, h);
        Assert.AreEqual(color, decodedColor);
        CollectionAssert.AreEqual(pixels, decoded);
        CollectionAssert.AreEqual(new[] { "IHDR", "sRGB", "gAMA", "cHRM", "IDAT", "IEND" }, chunks);
    }

    [TestMethod]
    public void RejectsWrongBufferSize() =>
        Assert.ThrowsExactly<ArgumentException>(() => PngEncoder.Encode(new byte[10], 2, 2, PngColor.Rgb));

    // The PNG specification's Paeth predictor, written out independently of the encoder's copy.
    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
            return a;
        if (pb <= pc)
            return b;
        return c;
    }

    private static (int Width, int Height, PngColor Color, byte[] Pixels, List<string> Chunks) Decode(byte[] png)
    {
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        var chunks = new List<string>();
        int width = 0, height = 0;
        PngColor color = PngColor.Gray;
        using var idat = new MemoryStream();
        int at = 8;
        while (at < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            string type = Encoding.ASCII.GetString(png, at + 4, 4);
            ReadOnlySpan<byte> data = png.AsSpan(at + 8, length);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length));
            Assert.AreEqual(Crc32.Update(0xFFFFFFFFu, png.AsSpan(at + 4, 4 + length)) ^ 0xFFFFFFFFu, crc, $"{type} CRC");
            chunks.Add(type);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                Assert.AreEqual(8, data[8]);
                color = (PngColor)data[9];
            }
            else if (type == "IDAT")
            {
                idat.Write(data);
            }
            at += 12 + length;
        }

        int bpp = PngEncoder.Channels(color);
        int stride = width * bpp;
        idat.Position = 0;
        using var inflate = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflate.CopyTo(raw);
        byte[] filtered = raw.ToArray();
        Assert.AreEqual((stride + 1) * height, filtered.Length);

        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            byte filter = filtered[y * (stride + 1)];
            for (int x = 0; x < stride; x++)
            {
                int a = x >= bpp ? pixels[y * stride + x - bpp] : 0;
                int b = y > 0 ? pixels[(y - 1) * stride + x] : 0;
                int c = x >= bpp && y > 0 ? pixels[(y - 1) * stride + x - bpp] : 0;
                int predictor = filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new AssertFailedException($"bad filter {filter}"),
                };
                pixels[y * stride + x] = (byte)(filtered[y * (stride + 1) + 1 + x] + predictor);
            }
        }
        return (width, height, color, pixels, chunks);
    }
}
