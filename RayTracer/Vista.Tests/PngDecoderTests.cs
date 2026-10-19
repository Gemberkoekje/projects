using System.Buffers.Binary;
using System.IO.Compression;
using Vista.Output;

namespace Vista.Tests;

/// <summary>Reading PNGs back for <c>--sheet-from</c> (vista-plan §7.4).</summary>
[TestClass]
public sealed class PngDecoderTests
{
    [TestMethod]
    [DataRow(PngColor.Gray)]
    [DataRow(PngColor.Rgb)]
    [DataRow(PngColor.Rgba)]
    public void Decode_RoundTripsTheEncoder(PngColor color)
    {
        // Noise, gradients and flat runs, so the encoder's adaptive choice uses several filter types.
        const int width = 37, height = 23;
        int channels = PngEncoder.Channels(color);
        var pixels = new byte[width * height * channels];
        var random = new Random(5);
        for (int y = 0; y < height; y++)
        {
            for (int i = 0; i < width * channels; i++)
            {
                pixels[y * width * channels + i] = (y % 3) switch
                {
                    0 => (byte)random.Next(256),
                    1 => (byte)(i * 7 + y),
                    _ => 128,
                };
            }
        }
        PngImage image = PngDecoder.Decode(PngEncoder.Encode(pixels, width, height, color));
        Assert.AreEqual((width, height, color), (image.Width, image.Height, image.Color));
        CollectionAssert.AreEqual(pixels, image.Pixels);
    }

    [TestMethod]
    public void EveryFilterType_IsUndone()
    {
        // One RGB row per filter type 0–4, written by hand so each is exercised whatever the encoder picks.
        const int width = 4, height = 5;
        var expected = new byte[width * height * 3];
        for (int i = 0; i < expected.Length; i++)
            expected[i] = (byte)(i * 37 % 251);
        var raw = new byte[(width * 3 + 1) * height];
        for (int y = 0; y < height; y++)
        {
            raw[y * (width * 3 + 1)] = (byte)y;
            for (int i = 0; i < width * 3; i++)
            {
                int a = i >= 3 ? expected[y * width * 3 + i - 3] : 0;
                int b = y > 0 ? expected[(y - 1) * width * 3 + i] : 0;
                int c = i >= 3 && y > 0 ? expected[(y - 1) * width * 3 + i - 3] : 0;
                int predictor = y switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2, _ => Paeth(a, b, c) };
                raw[y * (width * 3 + 1) + 1 + i] = (byte)(expected[y * width * 3 + i] - predictor);
            }
        }
        PngImage image = PngDecoder.Decode(Png(width, height, colorType: 2, bitDepth: 8, interlace: 0, raw));
        CollectionAssert.AreEqual(expected, image.Pixels);
    }

    [TestMethod]
    public void ToRgb_ExpandsGreyAndDropsAlpha()
    {
        CollectionAssert.AreEqual(new byte[] { 7, 7, 7, 9, 9, 9 }, new PngImage([7, 9], 2, 1, PngColor.Gray).ToRgb());
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 5, 6, 7 }, new PngImage([1, 2, 3, 4, 5, 6, 7, 8], 2, 1, PngColor.Rgba).ToRgb());
    }

    [TestMethod]
    public void UnsupportedFiles_FailLoudly()
    {
        byte[] row = [0, 1, 2, 3];
        Assert.ThrowsExactly<InvalidDataException>(() => PngDecoder.Decode("not a png at all"u8));
        StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => PngDecoder.Decode(Png(1, 1, 2, 16, 0, row))).Message, "8-bit");
        StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => PngDecoder.Decode(Png(1, 1, 2, 8, 1, row))).Message, "Interlaced");
        StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => PngDecoder.Decode(Png(1, 1, 3, 8, 0, row))).Message, "colour type 3");
        StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => PngDecoder.Decode(Png(1, 1, 2, 8, 0, [9, 1, 2, 3]))).Message, "filter type 9");
        byte[] good = Png(1, 1, 2, 8, 0, row);
        Assert.ThrowsExactly<InvalidDataException>(() => PngDecoder.Decode(good.AsSpan(0, good.Length - 20)));
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
            return a;
        return pb <= pc ? b : c;
    }

    /// <summary>A minimal PNG (CRCs left zero; the decoder does not check them) around already-filtered rows.</summary>
    private static byte[] Png(int width, int height, byte colorType, byte bitDepth, byte interlace, byte[] filteredRows)
    {
        using var file = new MemoryStream();
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = bitDepth;
        ihdr[9] = colorType;
        ihdr[12] = interlace;
        Chunk(file, "IHDR", ihdr);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(filteredRows);
        Chunk(file, "IDAT", compressed.ToArray());
        Chunk(file, "IEND", []);
        return file.ToArray();
    }

    private static void Chunk(Stream file, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        file.Write(length);
        file.Write(System.Text.Encoding.ASCII.GetBytes(type));
        file.Write(data);
        file.Write(new byte[4]);
    }
}
