using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Vista.Output;

namespace Vista.Tests;

/// <summary>
/// The EXR writer, checked by an independent minimal reader (header attributes, offset table, ZIP
/// inverse) so a round trip proves the file layout. Out-of-band, the files also open in the reference
/// OpenEXR library.
/// </summary>
[TestClass]
public sealed class ExrEncoderTests
{
    [TestMethod]
    [DataRow(ExrPixelType.Half, ExrCompression.None)]
    [DataRow(ExrPixelType.Half, ExrCompression.Zip)]
    [DataRow(ExrPixelType.Float, ExrCompression.Zips)]
    [DataRow(ExrPixelType.Float, ExrCompression.Zip)]
    public void Rgb_RoundTrips(ExrPixelType type, ExrCompression compression)
    {
        const int width = 23, height = 37; // not a multiple of the 16-line ZIP block
        var rgb = new float[width * height * 3];
        for (int i = 0; i < rgb.Length; i++)
            rgb[i] = (float)((i % 97) * 0.173 - 2.0 + (i % 3) * 100.0);

        byte[] file = ExrEncoder.EncodeRgb(rgb, width, height, type, compression);
        ParsedExr exr = Read(file);

        Assert.AreEqual(width, exr.Width);
        Assert.AreEqual(height, exr.Height);
        Assert.AreEqual((byte)compression, exr.Compression);
        CollectionAssert.AreEqual(new[] { "B", "G", "R" }, exr.Channels.Select(c => c.Name).ToArray(), "channels sorted by name");
        Assert.IsTrue(exr.Channels.All(c => c.Type == (int)type));
        for (int p = 0; p < width * height; p++)
        {
            for (int c = 0; c < 3; c++)
            {
                float expected = type == ExrPixelType.Half ? (float)(Half)rgb[p * 3 + c] : rgb[p * 3 + c];
                string name = "RGB"[c].ToString();
                Assert.AreEqual(expected, exr.Data[name][p], $"pixel {p} {name}");
            }
        }
    }

    [TestMethod]
    public void Header_CarriesTheRequiredAttributes()
    {
        ParsedExr exr = Read(ExrEncoder.EncodeRgb(new float[2 * 2 * 3], 2, 2));
        foreach (string name in new[] { "channels", "compression", "dataWindow", "displayWindow", "lineOrder", "pixelAspectRatio", "screenWindowCenter", "screenWindowWidth", "chromaticities" })
            Assert.IsTrue(exr.Attributes.ContainsKey(name), name);
        Assert.AreEqual("chromaticities", exr.Attributes["chromaticities"].Type);
        Assert.AreEqual(0.64f, BinaryPrimitives.ReadSingleLittleEndian(exr.Attributes["chromaticities"].Value));
    }

    [TestMethod]
    public void Zip_ShrinksSmoothImages()
    {
        var rgb = new float[256 * 256 * 3];
        for (int i = 0; i < rgb.Length; i++)
            rgb[i] = (i / 3 % 256) / 256f;
        Assert.IsTrue(ExrEncoder.EncodeRgb(rgb, 256, 256, compression: ExrCompression.Zip).Length
                      < ExrEncoder.EncodeRgb(rgb, 256, 256, compression: ExrCompression.None).Length / 4);
    }

    [TestMethod]
    public void ArbitraryChannels_AreStoredSorted()
    {
        float[] a = [1, 2], z = [3, 4];
        ParsedExr exr = Read(ExrEncoder.Encode(2, 1, [new("Z", z), new("A", a)], ExrPixelType.Float, ExrCompression.None));
        CollectionAssert.AreEqual(new[] { "A", "Z" }, exr.Channels.Select(c => c.Name).ToArray());
        CollectionAssert.AreEqual(a, exr.Data["A"]);
        CollectionAssert.AreEqual(z, exr.Data["Z"]);
    }

    [TestMethod]
    public void RejectsBadInput()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ExrEncoder.EncodeRgb(new float[5], 2, 1));
        Assert.ThrowsExactly<ArgumentException>(() => ExrEncoder.Encode(1, 1, [new("R", [1]), new("R", [2])]));
        Assert.ThrowsExactly<ArgumentException>(() => ExrEncoder.Encode(2, 1, [new("R", [1])]));
    }

    private sealed record ParsedChannel(string Name, int Type);

    private sealed record ParsedExr(
        int Width, int Height, byte Compression, List<ParsedChannel> Channels,
        Dictionary<string, (string Type, byte[] Value)> Attributes, Dictionary<string, float[]> Data);

    private static ParsedExr Read(byte[] file)
    {
        Assert.AreEqual(20000630, BinaryPrimitives.ReadInt32LittleEndian(file));
        Assert.AreEqual(2, BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(4)));
        int at = 8;
        var attributes = new Dictionary<string, (string, byte[])>();
        while (file[at] != 0)
        {
            string name = CString(file, ref at);
            string type = CString(file, ref at);
            int size = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at));
            attributes[name] = (type, file.AsSpan(at + 4, size).ToArray());
            at += 4 + size;
        }
        at++; // end of header

        var channels = new List<ParsedChannel>();
        byte[] chlist = attributes["channels"].Item2;
        int c = 0;
        while (chlist[c] != 0)
        {
            string name = CString(chlist, ref c);
            channels.Add(new ParsedChannel(name, BinaryPrimitives.ReadInt32LittleEndian(chlist.AsSpan(c))));
            Assert.AreEqual(1, BinaryPrimitives.ReadInt32LittleEndian(chlist.AsSpan(c + 8)), "xSampling");
            Assert.AreEqual(1, BinaryPrimitives.ReadInt32LittleEndian(chlist.AsSpan(c + 12)), "ySampling");
            c += 16;
        }

        byte[] window = attributes["dataWindow"].Item2;
        int width = BinaryPrimitives.ReadInt32LittleEndian(window.AsSpan(8)) - BinaryPrimitives.ReadInt32LittleEndian(window) + 1;
        int height = BinaryPrimitives.ReadInt32LittleEndian(window.AsSpan(12)) - BinaryPrimitives.ReadInt32LittleEndian(window.AsSpan(4)) + 1;
        byte compression = attributes["compression"].Item2[0];
        int lines = compression == 3 ? 16 : 1;
        int blocks = (height + lines - 1) / lines;

        var data = channels.ToDictionary(ch => ch.Name, _ => new float[width * height]);
        for (int k = 0; k < blocks; k++)
        {
            int chunk = checked((int)BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(at + k * 8)));
            int y0 = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(chunk));
            int size = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(chunk + 4));
            Assert.AreEqual(k * lines, y0);
            int blockLines = Math.Min(lines, height - y0);
            int rawSize = blockLines * width * channels.Sum(ch => ch.Type == 1 ? 2 : 4);
            byte[] payload = file.AsSpan(chunk + 8, size).ToArray();
            byte[] raw = compression == 0 || size == rawSize ? payload : Unzip(payload, rawSize);
            Assert.AreEqual(rawSize, raw.Length);

            int r = 0;
            for (int y = y0; y < y0 + blockLines; y++)
            {
                foreach (ParsedChannel ch in channels)
                {
                    for (int x = 0; x < width; x++)
                    {
                        data[ch.Name][y * width + x] = ch.Type == 1
                            ? (float)BinaryPrimitives.ReadHalfLittleEndian(raw.AsSpan(r))
                            : BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(r));
                        r += ch.Type == 1 ? 2 : 4;
                    }
                }
            }
        }
        return new ParsedExr(width, height, compression, channels, attributes, data);
    }

    // Inverse of OpenEXR's ZIP predictor: inflate, undo the delta, re-interleave the two halves.
    private static byte[] Unzip(byte[] payload, int rawSize)
    {
        using var input = new MemoryStream(payload);
        using var inflate = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflate.CopyTo(output);
        byte[] t = output.ToArray();
        Assert.AreEqual(rawSize, t.Length);
        for (int i = 1; i < t.Length; i++)
            t[i] = (byte)(t[i - 1] + t[i] - 128);
        var raw = new byte[t.Length];
        int half = (t.Length + 1) / 2;
        for (int i = 0; i < t.Length; i++)
            raw[i] = (i & 1) == 0 ? t[i / 2] : t[half + i / 2];
        return raw;
    }

    private static string CString(byte[] bytes, ref int at)
    {
        int end = Array.IndexOf(bytes, (byte)0, at);
        string s = Encoding.ASCII.GetString(bytes, at, end - at);
        at = end + 1;
        return s;
    }
}
