using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.World;

namespace Vista.Cli;

/// <summary>
/// Contact sheets end to end (vista-plan §7.4). <see cref="Render"/> renders one preview per value of the
/// sheet's axis — each a complete preview with its own files under <c>&lt;sheet&gt;.cells/</c> — and tiles
/// them; <see cref="FromFiles"/> tiles images that already exist (a render on <c>main</c> and one on a branch
/// make a PR's before/after). Either writes the sheet PNG and one sidecar listing every cell.
/// </summary>
public static class SheetPipeline
{
    /// <summary>
    /// Renders every cell of <paramref name="request"/> with <paramref name="renderer"/> (null for a dry run,
    /// which tiles the cells' height maps) and writes the sheet. Every cell is checked before the first
    /// renders, so a misspelt field or a missing file fails before any GPU time is spent. A request that denoises needs a
    /// <paramref name="denoiser"/>, and writes a second sheet of the denoised cells when every cell has one.
    /// </summary>
    public static SheetSidecar Render(SheetRequest request, IVistaRenderer? renderer, TimeProvider? time = null, IDenoiser? denoiser = null,
        ITerrainEroder? eroder = null, ErosionCache? erosionCache = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (renderer is null && !request.Base.DryRun)
            throw new InvalidOperationException("A renderer is required unless the request is a dry run.");
        time ??= TimeProvider.System;

        (string directory, string baseName) = Split(request.Base.OutPath);
        string cellDirectory = Path.Combine(directory, baseName + ".cells");
        int count = request.Axis.Values.Count;
        var cells = new List<PreviewRequest>(count);
        for (int i = 0; i < count; i++)
        {
            PreviewRequest cell = request.Axis.Apply(request.Base, i) with
            {
                OutPath = Path.Combine(cellDirectory, string.Create(CultureInfo.InvariantCulture, $"cell{i + 1:00}.png")),
            };
            Preflight(cell, request.Axis.Label(i));
            cells.Add(cell);
        }

        Directory.CreateDirectory(cellDirectory);
        var worlds = new WorldCache();
        var sidecars = new List<Sidecar>(count);
        foreach (PreviewRequest cell in cells)
            sidecars.Add(PreviewPipeline.Run(cell, renderer, time, worlds, denoiser, eroder, erosionCache));

        string beautyRole = renderer is null ? "heightMap" : "beauty";
        var sources = cells.Select((c, i) => Path.Combine(cellDirectory, sidecars[i].Outputs[beautyRole])).ToList();
        var identities = sidecars
            .Select(s => (IReadOnlyDictionary<string, string>)SheetLabels.Identity(JsonSerializer.SerializeToNode(s, VistaJson.Options)))
            .ToList();
        string title = request.Title.Length > 0 ? request.Title : SheetLabels.Title(identities);
        var labels = Enumerable.Range(0, count).Select(request.Axis.Label).ToList();

        var outputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        SheetLayout layout = WriteSheet(request.Base.OutPath, sources, labels, request.Columns, title);
        outputs["sheet"] = Path.GetFileName(request.Base.OutPath);
        if (renderer is not null)
        {
            foreach (DebugOutput debug in request.Base.Debug.OrderBy(d => d))
            {
                string role = debug.ToString().ToLowerInvariant();
                string file = $"{baseName}.{role}.png";
                WriteSheet(Path.Combine(directory, file), cells.Select((c, i) => Path.Combine(cellDirectory, sidecars[i].Outputs[role])).ToList(),
                    labels, request.Columns, title);
                outputs[role] = file;
            }
            if (sidecars.All(s => s.Outputs.ContainsKey("denoised")))
            {
                string file = baseName + ".denoised.png";
                WriteSheet(Path.Combine(directory, file), cells.Select((c, i) => Path.Combine(cellDirectory, sidecars[i].Outputs["denoised"])).ToList(),
                    labels, request.Columns, title.Length > 0 ? title + " · denoised" : "denoised");
                outputs["denoised"] = file;
            }
        }
        outputs["sidecar"] = baseName + ".json";

        var sidecar = new SheetSidecar
        {
            Kind = "sheet",
            CreatedUtc = time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            Engine = EngineInfo.Current(renderer?.DeviceName),
            Axis = request.Axis.Key,
            Title = title,
            Width = layout.Width,
            Height = layout.Height,
            Columns = layout.Columns,
            Rows = layout.Rows,
            Cells = Cells(layout, labels, sources, directory, sidecars.Select(s => JsonSerializer.SerializeToNode(s, VistaJson.Options)).ToList()),
            Outputs = outputs,
            Warnings = [.. sidecars.SelectMany((s, i) => s.Warnings.Select(w => $"{labels[i]}: {w}")).Distinct()],
        };
        File.WriteAllText(Path.Combine(directory, baseName + ".json"), VistaJson.Serialize(sidecar));
        return sidecar;
    }

    /// <summary>
    /// Tiles the images of <paramref name="request"/>, each labelled by <see cref="SheetFromRequest.Labels"/> or
    /// else by what its sidecar says that the others' do not (the file name when nothing differs or there is
    /// no sidecar), and writes the sheet.
    /// </summary>
    public static SheetSidecar FromFiles(SheetFromRequest request, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        time ??= TimeProvider.System;
        (string directory, string baseName) = Split(request.OutPath);

        var warnings = new List<string>();
        var sidecars = new List<JsonNode?>(request.Inputs.Count);
        foreach (string input in request.Inputs)
        {
            if (!File.Exists(input))
                throw new FileNotFoundException($"No image at '{input}'.", input);
            string? path = SheetLabels.SidecarPathFor(input);
            JsonNode? node = null;
            if (path is null)
            {
                warnings.Add($"{input}: no sidecar, labelled by its file name.");
            }
            else
            {
                try
                {
                    node = JsonNode.Parse(File.ReadAllText(path));
                }
                catch (JsonException e)
                {
                    warnings.Add($"{path}: not readable JSON ({e.Message}), labelled by its file name.");
                }
            }
            sidecars.Add(node);
        }

        var identities = sidecars.Select((s, i) => (IReadOnlyDictionary<string, string>)SheetLabels.Identity(s, request.Inputs[i])).ToList();
        IReadOnlyList<string> labels = request.Labels.Count > 0
            ? request.Labels
            : [.. SheetLabels.Differences(identities).Select((label, i) =>
                sidecars[i] is null || label.Length == 0 ? Path.GetFileName(request.Inputs[i]) : label)];
        string title = request.Title.Length > 0 ? request.Title : SheetLabels.Title(identities);

        SheetLayout layout = WriteSheet(request.OutPath, request.Inputs, labels, request.Columns, title);
        var outputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["sheet"] = Path.GetFileName(request.OutPath),
            ["sidecar"] = baseName + ".json",
        };
        var sidecar = new SheetSidecar
        {
            Kind = "sheetFrom",
            CreatedUtc = time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            Engine = EngineInfo.Current(device: null),
            Title = title,
            Width = layout.Width,
            Height = layout.Height,
            Columns = layout.Columns,
            Rows = layout.Rows,
            Cells = Cells(layout, labels, request.Inputs, directory, sidecars),
            Outputs = outputs,
            Warnings = warnings,
        };
        File.WriteAllText(Path.Combine(directory, baseName + ".json"), VistaJson.Serialize(sidecar));
        return sidecar;
    }

    /// <summary>
    /// Checks a cell before anything renders: its recipe (with overrides) and camera (file and overrides)
    /// must parse in the strict dialect. The default camera's overrides are checked against a stand-in pose.
    /// </summary>
    private static void Preflight(PreviewRequest cell, string label)
    {
        try
        {
            VistaJson.ParseRecipe(JsonOverrides.Apply(File.ReadAllText(cell.RecipePath), cell.RecipeOverrides));
            string camera = cell.CameraPath is null
                ? VistaJson.Serialize(new CameraSpec { Position = new WorldPoint(0.0, 0.0, 0.0) })
                : File.ReadAllText(cell.CameraPath);
            VistaJson.ParseCamera(JsonOverrides.Apply(camera, cell.CameraOverrides));
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Sheet cell {label}: {e.Message}", e);
        }
    }

    private static SheetLayout WriteSheet(string outPath, IReadOnlyList<string> sources, IReadOnlyList<string> labels, int columns, string title)
    {
        var tiles = sources.Select(PngDecoder.Read).ToList();
        SheetLayout layout = ContactSheet.Layout([.. tiles.Select(t => (t.Width, t.Height))], labels, columns, title);
        byte[] rgb = ContactSheet.Compose(layout, [.. tiles.Select(t => t.ToRgb())]);
        PngEncoder.Write(outPath, rgb, layout.Width, layout.Height, PngColor.Rgb);
        return layout;
    }

    private static List<SheetCellInfo> Cells(
        SheetLayout layout, IReadOnlyList<string> labels, IReadOnlyList<string> sources, string directory, IReadOnlyList<JsonNode?> sidecars) =>
        [.. layout.Cells.Select((c, i) => new SheetCellInfo(
            i + 1, labels[i], c.Column, c.Row, c.X, c.Y, c.Width, c.Height,
            Path.GetRelativePath(directory, Path.GetFullPath(sources[i])).Replace('\\', '/'),
            sidecars[i]))];

    private static (string Directory, string BaseName) Split(string outPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(outPath))!;
        Directory.CreateDirectory(directory);
        return (directory, Path.GetFileNameWithoutExtension(outPath));
    }
}
