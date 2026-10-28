using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Vista.Output;

/// <summary>An EXR file read back (<see cref="ExrDecoder"/>): its size and its channels, each row-major from the top row.</summary>
/// <param name="Width">Pixels across.</param>
/// <param name="Height">Pixels down.</param>
/// <param name="Channels">The channels in the file's order (sorted by name).</param>
public sealed record ExrImage(int Width, int Height, IReadOnlyList<ExrChannel> Channels)
{
    /// <summary>The channel named <paramref name="name"/>.</summary>
    /// <exception cref="KeyNotFoundException">The file has no such channel.</exception>
    public ExrChannel Channel(string name) =>
        Channels.FirstOrDefault(c => c.Name == name) ?? throw new KeyNotFoundException($"The EXR has no channel '{name}'.");

    /// <summary>Channels R, G and B interleaved, three floats a pixel.</summary>
    public float[] InterleavedRgb()
    {
        float[] r = Channel("R").Data, g = Channel("G").Data, b = Channel("B").Data;
        var rgb = new float[Width * Height * 3];
        for (int p = 0; p < Width * Height; p++)
            (rgb[p * 3], rgb[p * 3 + 1], rgb[p * 3 + 2]) = (r[p], g[p], b[p]);
        return rgb;
    }
}

/// <summary>
/// Reads the EXR files <see cref="ExrEncoder"/> writes (vista-plan §10 M1.17, for the goldens): single-part scanline files with
/// HALF or FLOAT channels, NONE / ZIPS / ZIP compression, increasing line order, the data window at the origin. Anything else is
/// refused rather than misread.
/// </summary>
public static class ExrDecoder
{
    private const int Magic = 20000630;

    /// <summary>Reads the EXR file at <paramref name="path"/>.</summary>
    /// <exception cref="InvalidDataException">The file is not an EXR this reader takes.</exception>
    public static ExrImage Read(string path) => Decode(File.ReadAllBytes(path));

    /// <summary>Decodes an EXR file's bytes.</summary>
    /// <exception cref="InvalidDataException">The bytes are not an EXR this reader takes.</exception>
    public static ExrImage Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.ASCII);
        if (bytes.Length < 8 || reader.ReadInt32() != Magic)
            throw new InvalidDataException("Not an OpenEXR file.");
        int version = reader.ReadInt32();
        if ((version & 0xFF) != 2 || (version & ~0xFF) != 0)
            throw new InvalidDataException($"Only single-part scanline EXR files are read, version field 0x{version:x}.");

        var channels = new List<(string Name, ExrPixelType Type)>();
        int compression = -1;
        int[]? dataWindow = null;
        while (true)
        {
            string name = ReadCString(reader);
            if (name.Length == 0)
                break;
            _ = ReadCString(reader); // the attribute's type: each is known by its name
            int size = reader.ReadInt32();
            byte[] value = reader.ReadBytes(size);
            switch (name)
            {
                case "channels":
                    channels = ParseChannels(value);
                    break;
                case "compression":
                    compression = value[0];
                    break;
                case "dataWindow":
                    dataWindow = [.. Enumerable.Range(0, 4).Select(i => BinaryPrimitives.ReadInt32LittleEndian(value.AsSpan(i * 4)))];
                    break;
                case "lineOrder" when value[0] != 0:
                    throw new InvalidDataException("Only increasing line order is read.");
                default:
                    break;
            }
        }
        if (channels.Count == 0 || dataWindow is null || compression < 0)
            throw new InvalidDataException("The EXR header lacks its channels, data window or compression.");
        if (dataWindow[0] != 0 || dataWindow[1] != 0)
            throw new InvalidDataException("Only data windows at the origin are read.");
        if (compression is not ((int)ExrCompression.None or (int)ExrCompression.Zips or (int)ExrCompression.Zip))
            throw new InvalidDataException($"Unsupported EXR compression {compression}.");

        int width = dataWindow[2] + 1, height = dataWindow[3] + 1;
        int linesPerBlock = compression == (int)ExrCompression.Zip ? 16 : 1;
        int blocks = (height + linesPerBlock - 1) / linesPerBlock;
        var offsets = new long[blocks];
        for (int k = 0; k < blocks; k++)
            offsets[k] = reader.ReadInt64();

        var data = channels.Select(_ => new float[width * height]).ToArray();
        int bytesPerLine = channels.Sum(c => c.Type == ExrPixelType.Half ? 2 : 4) * width;
        for (int k = 0; k < blocks; k++)
        {
            stream.Position = offsets[k];
            int y0 = reader.ReadInt32();
            int size = reader.ReadInt32();
            byte[] payload = reader.ReadBytes(size);
            int lines = Math.Min(linesPerBlock, height - y0);
            int rawLength = lines * bytesPerLine;
            byte[] raw = compression == (int)ExrCompression.None || payload.Length == rawLength ? payload : UnzipBlock(payload, rawLength);
            if (raw.Length != rawLength)
                throw new InvalidDataException($"Block {k} holds {raw.Length} bytes, expected {rawLength}.");
            int at = 0;
            for (int y = y0; y < y0 + lines; y++)
            {
                for (int c = 0; c < channels.Count; c++)
                {
                    float[] target = data[c];
                    for (int x = 0; x < width; x++)
                    {
                        if (channels[c].Type == ExrPixelType.Half)
                        {
                            target[y * width + x] = (float)BinaryPrimitives.ReadHalfLittleEndian(raw.AsSpan(at));
                            at += 2;
                        }
                        else
                        {
                            target[y * width + x] = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(at));
                            at += 4;
                        }
                    }
                }
            }
        }
        return new ExrImage(width, height, [.. channels.Select((c, i) => new ExrChannel(c.Name, data[i]))]);
    }

    private static List<(string Name, ExrPixelType Type)> ParseChannels(byte[] value)
    {
        var channels = new List<(string, ExrPixelType)>();
        using var reader = new BinaryReader(new MemoryStream(value), Encoding.ASCII);
        while (true)
        {
            string name = ReadCString(reader);
            if (name.Length == 0)
                return channels;
            int type = reader.ReadInt32();
            reader.ReadBytes(4); // pLinear and reserved
            int xSampling = reader.ReadInt32(), ySampling = reader.ReadInt32();
            if (type is not ((int)ExrPixelType.Half or (int)ExrPixelType.Float) || xSampling != 1 || ySampling != 1)
                throw new InvalidDataException($"Channel '{name}': only HALF or FLOAT channels sampled at every pixel are read.");
            channels.Add((name, (ExrPixelType)type));
        }
    }

    /// <summary>The inverse of the encoder's ZIP block: zlib, the predictor's running sum, the two halves interleaved again.</summary>
    private static byte[] UnzipBlock(byte[] payload, int rawLength)
    {
        var shuffled = new byte[rawLength];
        using (var zlib = new ZLibStream(new MemoryStream(payload), CompressionMode.Decompress))
            zlib.ReadExactly(shuffled);
        for (int i = 1; i < shuffled.Length; i++)
            shuffled[i] = unchecked((byte)(shuffled[i - 1] + shuffled[i] - 128));
        var raw = new byte[rawLength];
        int half = (rawLength + 1) / 2;
        for (int i = 0; i < rawLength; i++)
            raw[i] = shuffled[(i & 1) == 0 ? i / 2 : half + i / 2];
        return raw;
    }

    private static string ReadCString(BinaryReader reader)
    {
        var text = new StringBuilder();
        for (byte b = reader.ReadByte(); b != 0; b = reader.ReadByte())
            text.Append((char)b);
        return text.ToString();
    }
}
