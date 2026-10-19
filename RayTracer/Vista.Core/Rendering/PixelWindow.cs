using System.Globalization;
using System.Text.Json.Serialization;

namespace Vista.Rendering;

/// <summary>
/// A rectangle of pixels in a frame's own coordinates (column <see cref="X"/>, row <see cref="Y"/>, row 0 at
/// the top). The loupe (vista-plan §7.4) renders one of these out of the full frame; the default value is
/// <see cref="IsEmpty"/> and means "no window", i.e. the whole frame.
/// </summary>
/// <param name="X">First column.</param>
/// <param name="Y">First row.</param>
/// <param name="Width">Columns.</param>
/// <param name="Height">Rows.</param>
public readonly record struct PixelWindow(int X, int Y, int Width, int Height)
{
    /// <summary>True for the default value (no window).</summary>
    [JsonIgnore]
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>Pixels in the window.</summary>
    [JsonIgnore]
    public int Pixels => Width * Height;

    /// <summary>The whole <paramref name="width"/>×<paramref name="height"/> frame.</summary>
    public static PixelWindow Full(int width, int height) => new(0, 0, width, height);

    /// <summary>True when the window is non-empty and lies inside a <paramref name="width"/>×<paramref name="height"/> frame.</summary>
    public bool FitsIn(int width, int height) =>
        X >= 0 && Y >= 0 && Width > 0 && Height > 0 && (long)X + Width <= width && (long)Y + Height <= height;

    /// <summary>True when the window covers the whole <paramref name="width"/>×<paramref name="height"/> frame.</summary>
    public bool IsFull(int width, int height) => X == 0 && Y == 0 && Width == width && Height == height;

    /// <summary>
    /// Cuts this window out of a top-row-first <paramref name="frame"/> buffer <paramref name="frameWidth"/>
    /// pixels wide with <paramref name="channels"/> floats per pixel.
    /// </summary>
    public float[] Crop(ReadOnlySpan<float> frame, int frameWidth, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        int frameHeight = frame.Length / (frameWidth * channels);
        if (frame.Length != frameWidth * frameHeight * channels || !FitsIn(frameWidth, frameHeight))
            throw new ArgumentException($"Window {this} does not fit a {frameWidth}-pixel-wide buffer of {frame.Length} floats.", nameof(frame));
        var crop = new float[Pixels * channels];
        int rowFloats = Width * channels;
        for (int row = 0; row < Height; row++)
            frame.Slice(((Y + row) * frameWidth + X) * channels, rowFloats).CopyTo(crop.AsSpan(row * rowFloats, rowFloats));
        return crop;
    }

    /// <summary>Parses <c>x,y,width,height</c> (non-negative integers, positive size).</summary>
    public static bool TryParse(string? text, out PixelWindow window)
    {
        window = default;
        string[] parts = (text ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
            return false;
        var values = new int[4];
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]))
                return false;
        }
        if (values[2] == 0 || values[3] == 0)
            return false;
        window = new PixelWindow(values[0], values[1], values[2], values[3]);
        return true;
    }

    /// <summary><c>x,y,width,height</c>, the form <see cref="TryParse"/> reads.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{X},{Y},{Width},{Height}");
}
