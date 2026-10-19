namespace Vista.Output;

/// <summary>A line of text placed on a sheet: the glyph cell's top-left corner and the (already fitted) text.</summary>
/// <param name="X">Left edge in sheet pixels.</param>
/// <param name="Y">Top edge in sheet pixels.</param>
/// <param name="Text">What to draw.</param>
public sealed record SheetText(int X, int Y, string Text);

/// <summary>Where one tile goes on a sheet.</summary>
/// <param name="Column">Grid column, from 0.</param>
/// <param name="Row">Grid row, from 0.</param>
/// <param name="X">Left edge of the image in sheet pixels.</param>
/// <param name="Y">Top edge of the image in sheet pixels.</param>
/// <param name="Width">Image width.</param>
/// <param name="Height">Image height.</param>
/// <param name="Label">The label under the image's cell: up to <see cref="ContactSheet.MaxLabelLines"/> lines, wrapped and cut to the cell width.</param>
public sealed record SheetPlacement(int Column, int Row, int X, int Y, int Width, int Height, IReadOnlyList<SheetText> Label);

/// <summary>
/// The geometry of a contact sheet: a title block on top, then a grid of equal cells filled row by row, each an
/// image (top-left in its cell) over its label. Labels wrap; every cell's label strip is as tall as the
/// longest label needs. Every length scales with <see cref="Scale"/>, which grows with the cell width so
/// labels stay readable on large tiles.
/// </summary>
/// <param name="Width">Sheet width in pixels.</param>
/// <param name="Height">Sheet height in pixels.</param>
/// <param name="Columns">Grid columns.</param>
/// <param name="Rows">Grid rows.</param>
/// <param name="CellWidth">Width of every cell (the widest tile).</param>
/// <param name="CellHeight">Image height of every cell (the tallest tile); the label strip is below it.</param>
/// <param name="Scale">Font and spacing scale.</param>
/// <param name="Title">Title lines, wrapped to the sheet width; empty for no title.</param>
/// <param name="Cells">One placement per tile, in tile order.</param>
public sealed record SheetLayout(
    int Width,
    int Height,
    int Columns,
    int Rows,
    int CellWidth,
    int CellHeight,
    int Scale,
    IReadOnlyList<SheetText> Title,
    IReadOnlyList<SheetPlacement> Cells);

/// <summary>
/// Contact sheets (vista-plan §7.4): several images tiled into one labelled grid, so one look compares them.
/// Pure layout and compositing on 8-bit RGB; <c>SheetPipeline</c> decides what goes in the tiles.
/// </summary>
public static class ContactSheet
{
    /// <summary>Sheet background.</summary>
    public static readonly (byte R, byte G, byte B) Background = (24, 24, 24);

    /// <summary>Title text colour.</summary>
    public static readonly (byte R, byte G, byte B) TitleColor = (240, 240, 240);

    /// <summary>Label text colour.</summary>
    public static readonly (byte R, byte G, byte B) LabelColor = (200, 200, 200);

    /// <summary>Most tiles one sheet takes.</summary>
    public const int MaxTiles = 64;

    /// <summary>Most lines a label wraps onto; the last is cut with an ellipsis when more remains.</summary>
    public const int MaxLabelLines = 3;

    /// <summary>Columns when none are asked for: a row for up to three tiles, otherwise a near-square grid.</summary>
    public static int DefaultColumns(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return count <= 3 ? count : (int)Math.Ceiling(Math.Sqrt(count));
    }

    /// <summary>Font scale for cells <paramref name="cellWidth"/> pixels wide: 1 up to 639 px, then one more per 320 px, at most 4.</summary>
    public static int ScaleFor(int cellWidth) => Math.Clamp(cellWidth / 320, 1, 4);

    /// <summary>
    /// Lays out tiles of the given <paramref name="sizes"/> with their <paramref name="labels"/> in
    /// <paramref name="columns"/> columns (0 = <see cref="DefaultColumns"/>) under <paramref name="title"/>
    /// (wrapped at <c>" · "</c>; empty for none).
    /// </summary>
    public static SheetLayout Layout(IReadOnlyList<(int Width, int Height)> sizes, IReadOnlyList<string> labels, int columns, string title)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(title);
        if (sizes.Count == 0 || sizes.Count > MaxTiles)
            throw new ArgumentException($"A sheet takes 1 to {MaxTiles} tiles, got {sizes.Count}.", nameof(sizes));
        if (labels.Count != sizes.Count)
            throw new ArgumentException($"{labels.Count} labels for {sizes.Count} tiles.", nameof(labels));
        if (sizes.Any(s => s.Width <= 0 || s.Height <= 0))
            throw new ArgumentException("Every tile needs a positive size.", nameof(sizes));
        if (columns == 0)
            columns = DefaultColumns(sizes.Count);
        if (columns < 1 || columns > sizes.Count)
            throw new ArgumentOutOfRangeException(nameof(columns), columns, $"Columns must be in [1, {sizes.Count}].");

        int rows = (sizes.Count + columns - 1) / columns;
        int cellWidth = sizes.Max(s => s.Width);
        int cellHeight = sizes.Max(s => s.Height);
        int scale = ScaleFor(cellWidth);
        int margin = 6 * scale;
        int gap = 6 * scale;
        int lineHeight = BitmapFont.LineHeight * scale;
        int width = 2 * margin + columns * cellWidth + (columns - 1) * gap;
        var labelLines = labels.Select(label => LabelLines(label, cellWidth, scale)).ToList();
        int labelStrip = 2 * scale + labelLines.Max(lines => lines.Count) * lineHeight;

        IReadOnlyList<string> titleLines = title.Length == 0 ? [] : BitmapFont.Wrap(title, width - 2 * margin, scale);
        var titleTexts = titleLines.Select((line, i) => new SheetText(margin, margin + i * lineHeight, line)).ToList();
        int gridTop = margin + (titleTexts.Count > 0 ? titleTexts.Count * lineHeight + gap : 0);
        int height = gridTop + rows * (cellHeight + labelStrip) + (rows - 1) * gap + margin;

        var cells = new List<SheetPlacement>(sizes.Count);
        for (int i = 0; i < sizes.Count; i++)
        {
            int column = i % columns;
            int row = i / columns;
            int x = margin + column * (cellWidth + gap);
            int y = gridTop + row * (cellHeight + labelStrip + gap);
            var label = labelLines[i].Select((line, k) => new SheetText(x, y + cellHeight + 2 * scale + k * lineHeight, line)).ToList();
            cells.Add(new SheetPlacement(column, row, x, y, sizes[i].Width, sizes[i].Height, label));
        }
        return new SheetLayout(width, height, columns, rows, cellWidth, cellHeight, scale, titleTexts, cells);
    }

    /// <summary>Draws the sheet: background, title, every tile (8-bit RGB, one per placement) and its label.</summary>
    public static byte[] Compose(SheetLayout layout, IReadOnlyList<byte[]> tiles)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(tiles);
        if (tiles.Count != layout.Cells.Count)
            throw new ArgumentException($"{tiles.Count} tiles for {layout.Cells.Count} cells.", nameof(tiles));

        var sheet = new byte[layout.Width * layout.Height * 3];
        for (int p = 0; p < sheet.Length; p += 3)
        {
            sheet[p] = Background.R;
            sheet[p + 1] = Background.G;
            sheet[p + 2] = Background.B;
        }
        foreach (SheetText line in layout.Title)
            BitmapFont.Draw(sheet, layout.Width, line.X, line.Y, line.Text, layout.Scale, TitleColor);

        for (int i = 0; i < tiles.Count; i++)
        {
            SheetPlacement cell = layout.Cells[i];
            int rowBytes = cell.Width * 3;
            if (tiles[i].Length != rowBytes * cell.Height)
                throw new ArgumentException($"Tile {i} has {tiles[i].Length} bytes, expected {rowBytes * cell.Height}.", nameof(tiles));
            for (int row = 0; row < cell.Height; row++)
            {
                Array.Copy(tiles[i], row * rowBytes, sheet, ((cell.Y + row) * layout.Width + cell.X) * 3, rowBytes);
            }
            foreach (SheetText line in cell.Label)
                BitmapFont.Draw(sheet, layout.Width, line.X, line.Y, line.Text, layout.Scale, LabelColor);
        }
        return sheet;
    }

    private static IReadOnlyList<string> LabelLines(string label, int width, int scale)
    {
        IReadOnlyList<string> lines = BitmapFont.Wrap(label, width, scale, " ");
        if (lines.Count <= MaxLabelLines)
            return lines;
        string rest = string.Join(" ", lines.Skip(MaxLabelLines - 1));
        return [.. lines.Take(MaxLabelLines - 1), BitmapFont.Fit(rest, width, scale)];
    }
}
