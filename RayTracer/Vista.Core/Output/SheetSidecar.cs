using System.Text.Json.Nodes;

namespace Vista.Output;

/// <summary>
/// The JSON file written next to a contact sheet (vista-plan §7.4): the sheet's geometry and, for every cell,
/// its label, its place, the image it shows and that image's own sidecar, so one file lists every cell.
/// </summary>
public sealed record SheetSidecar
{
    /// <summary>The sheet sidecar format version this build writes.</summary>
    public const int CurrentSchema = 1;

    /// <summary>Format version.</summary>
    public int Schema { get; init; } = CurrentSchema;

    /// <summary>"sheet" for variants rendered along one axis (<c>--sheet</c>), "sheetFrom" for existing images (<c>--sheet-from</c>).</summary>
    public required string Kind { get; init; }

    /// <summary>When the file was written (UTC, ISO 8601).</summary>
    public required string CreatedUtc { get; init; }

    /// <summary>The build that made the sheet (and, for <c>--sheet</c>, the device that rendered the cells).</summary>
    public required EngineInfo Engine { get; init; }

    /// <summary>The axis the cells vary along (<c>--vary</c>'s key, e.g. <c>seed</c>); null for <c>--sheet-from</c>.</summary>
    public string? Axis { get; init; }

    /// <summary>The title drawn on the sheet (empty for none).</summary>
    public required string Title { get; init; }

    /// <summary>Sheet width in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>Sheet height in pixels.</summary>
    public required int Height { get; init; }

    /// <summary>Grid columns.</summary>
    public required int Columns { get; init; }

    /// <summary>Grid rows.</summary>
    public required int Rows { get; init; }

    /// <summary>The cells in reading order.</summary>
    public required IReadOnlyList<SheetCellInfo> Cells { get; init; }

    /// <summary>Output role → file name (relative to this file): the sheet, one sheet per debug output, this sidecar.</summary>
    public required SortedDictionary<string, string> Outputs { get; init; }

    /// <summary>Non-fatal problems, e.g. an input without a sidecar.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>One cell of a contact sheet.</summary>
/// <param name="Index">Position in reading order, from 1.</param>
/// <param name="Label">The full label (the sheet may show it cut to the cell width).</param>
/// <param name="Column">Grid column, from 0.</param>
/// <param name="Row">Grid row, from 0.</param>
/// <param name="X">Left edge of the image on the sheet.</param>
/// <param name="Y">Top edge of the image on the sheet.</param>
/// <param name="Width">Image width.</param>
/// <param name="Height">Image height.</param>
/// <param name="Source">The image shown, relative to the sheet sidecar (forward slashes).</param>
/// <param name="Sidecar">That image's sidecar as written; null when it has none.</param>
public sealed record SheetCellInfo(
    int Index,
    string Label,
    int Column,
    int Row,
    int X,
    int Y,
    int Width,
    int Height,
    string Source,
    JsonNode? Sidecar);
