using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Vista.Output;

/// <summary>An image read back from a PNG file, tightly packed, top row first.</summary>
/// <param name="Pixels">
/// The samples at 8 bits, <see cref="PngEncoder.Channels"/> of <paramref name="Color"/> per pixel: a 16-bit file's rounded to the
/// nearest 8-bit step, so a contact sheet can tile it.
/// </param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Color">Channel layout.</param>
public sealed record PngImage(byte[] Pixels, int Width, int Height, PngColor Color)
{
    /// <summary>The file's bits per sample: 8, or 16 (vista-plan §10 M1.11c).</summary>
    public int BitDepth { get; init; } = 8;

    /// <summary>A 16-bit file's samples as they are, laid out as <see cref="Pixels"/>; empty for an 8-bit file.</summary>
    public ushort[] Samples16 { get; init; } = [];

    /// <summary>The image as 8-bit RGB: grey is copied to all three channels, alpha is dropped.</summary>
    public byte[] ToRgb()
    {
        if (Color == PngColor.Rgb)
            return Pixels;
        int channels = PngEncoder.Channels(Color);
        var rgb = new byte[Width * Height * 3];
        for (int p = 0; p < Width * Height; p++)
        {
            int s = p * channels;
            if (Color == PngColor.Gray)
            {
                rgb[p * 3] = rgb[p * 3 + 1] = rgb[p * 3 + 2] = Pixels[s];
            }
            else
            {
                rgb[p * 3] = Pixels[s];
                rgb[p * 3 + 1] = Pixels[s + 1];
                rgb[p * 3 + 2] = Pixels[s + 2];
            }
        }
        return rgb;
    }
}

/// <summary>
/// Reads the PNG files Vista writes (and other 8- or 16-bit, non-interlaced grey, RGB or RGBA PNGs) back into pixels,
/// so <c>--sheet-from</c> can tile existing renders (vista-plan §7.4). Dependency-free like
/// <see cref="PngEncoder"/>: the zlib stream goes through <see cref="ZLibStream"/>, and the five scanline
/// filters are undone here. Chunk CRCs are not checked; a truncated or malformed file fails loudly.
/// </summary>
public static class PngDecoder
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Reads and decodes the PNG file at <paramref name="path"/>.</summary>
    public static PngImage Read(string path) => Decode(File.ReadAllBytes(path));

    /// <summary>Decodes a PNG file's bytes.</summary>
    /// <exception cref="InvalidDataException">The file is not a PNG this decoder supports.</exception>
    public static PngImage Decode(ReadOnlySpan<byte> file)
    {
        if (file.Length < Signature.Length || !file[..Signature.Length].SequenceEqual(Signature))
            throw new InvalidDataException("Not a PNG file.");

        int width = 0, height = 0, bitDepth = 8;
        PngColor color = PngColor.Rgb;
        bool header = false;
        using var idat = new MemoryStream();
        int at = Signature.Length;
        while (true)
        {
            if (at + 8 > file.Length)
                throw new InvalidDataException("The PNG file ends before its IEND chunk.");
            int length = BinaryPrimitives.ReadInt32BigEndian(file[at..]);
            string type = Encoding.ASCII.GetString(file.Slice(at + 4, 4));
            if (length < 0 || (long)at + 12 + length > file.Length)
                throw new InvalidDataException($"The PNG chunk {type} runs past the end of the file.");
            ReadOnlySpan<byte> data = file.Slice(at + 8, length);
            at += 12 + length;

            if (type == "IHDR")
            {
                (width, height, color, bitDepth) = Header(data);
                header = true;
            }
            else if (type == "IDAT")
            {
                idat.Write(data);
            }
            else if (type == "IEND")
            {
                break;
            }
        }
        if (!header)
            throw new InvalidDataException("The PNG file has no IHDR chunk.");

        int bpp = PngEncoder.Channels(color) * bitDepth / 8;
        int stride = width * bpp;
        var filtered = new byte[(long)(stride + 1) * height];
        idat.Position = 0;
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
        {
            zlib.ReadExactly(filtered);
        }
        byte[] raw = Unfilter(filtered, stride, height, bpp);
        if (bitDepth == 8)
            return new PngImage(raw, width, height, color);

        // 16 bits: big-endian samples, and the nearest 8-bit step of each for the pixels.
        var samples = new ushort[raw.Length / 2];
        var pixels = new byte[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(i * 2));
            pixels[i] = (byte)((samples[i] * 255 + 32767) / 65535);
        }
        return new PngImage(pixels, width, height, color) { BitDepth = 16, Samples16 = samples };
    }

    private static (int Width, int Height, PngColor Color, int BitDepth) Header(ReadOnlySpan<byte> ihdr)
    {
        if (ihdr.Length != 13)
            throw new InvalidDataException("Malformed PNG IHDR chunk.");
        int width = BinaryPrimitives.ReadInt32BigEndian(ihdr);
        int height = BinaryPrimitives.ReadInt32BigEndian(ihdr[4..]);
        if (width <= 0 || height <= 0)
            throw new InvalidDataException($"Unusable PNG size {width}×{height}.");
        if (ihdr[8] is not (8 or 16))
            throw new InvalidDataException($"Only 8- and 16-bit PNGs are supported, this one has {ihdr[8]} bits per sample.");
        if (ihdr[12] != 0)
            throw new InvalidDataException("Interlaced PNGs are not supported.");
        PngColor color = ihdr[9] switch
        {
            0 => PngColor.Gray,
            2 => PngColor.Rgb,
            6 => PngColor.Rgba,
            _ => throw new InvalidDataException($"PNG colour type {ihdr[9]} is not supported (grey, RGB and RGBA are)."),
        };
        return (width, height, color, ihdr[8]);
    }

    private static byte[] Unfilter(byte[] filtered, int stride, int height, int bpp)
    {
        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            int src = y * (stride + 1);
            byte filter = filtered[src++];
            int row = y * stride;
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? pixels[row + i - bpp] : 0;
                int b = y > 0 ? pixels[row - stride + i] : 0;
                int c = i >= bpp && y > 0 ? pixels[row - stride + i - bpp] : 0;
                int predictor = filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) >> 1,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException($"Unknown PNG filter type {filter} on row {y}."),
                };
                pixels[row + i] = (byte)(filtered[src + i] + predictor);
            }
        }
        return pixels;
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
}
