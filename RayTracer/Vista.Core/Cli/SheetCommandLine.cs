using System.Globalization;
using Vista.Output;
using Vista.Recipes;

namespace Vista.Cli;

/// <summary>A parsed <c>--sheet</c> invocation: previews along one axis, tiled into one labelled image.</summary>
public sealed record SheetRequest
{
    /// <summary>
    /// The preview every cell starts from; <see cref="PreviewRequest.OutPath"/> is the sheet's PNG. When the
    /// axis supplies the recipe or the seed, that field is a placeholder until <see cref="SheetAxis.Apply"/>.
    /// </summary>
    public required PreviewRequest Base { get; init; }

    /// <summary>What varies from cell to cell.</summary>
    public required SheetAxis Axis { get; init; }

    /// <summary>Grid columns; 0 picks <see cref="ContactSheet.DefaultColumns"/>.</summary>
    public int Columns { get; init; }

    /// <summary>The sheet's title; empty builds one from what every cell shares.</summary>
    public string Title { get; init; } = string.Empty;
}

/// <summary>A parsed <c>--sheet-from</c> invocation: existing images tiled into one labelled sheet.</summary>
public sealed record SheetFromRequest
{
    /// <summary>The images to tile (PNG files), in reading order.</summary>
    public required IReadOnlyList<string> Inputs { get; init; }

    /// <summary>The sheet's PNG; its sidecar shares the base name.</summary>
    public required string OutPath { get; init; }

    /// <summary>One label per input; empty labels each cell from its sidecar.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    /// <summary>Grid columns; 0 picks <see cref="ContactSheet.DefaultColumns"/>.</summary>
    public int Columns { get; init; }

    /// <summary>The sheet's title; empty builds one from what every input shares.</summary>
    public string Title { get; init; } = string.Empty;
}

/// <summary>
/// Parses the contact-sheet CLI (vista-plan §7.4):
/// <code>
/// Vista.App --sheet --vary key=v1,v2,… --out sheet.png [--columns N] [--title text] [any --preview option]
/// Vista.App --sheet-from a.png b.png … --out sheet.png [--labels a,b,…] [--columns N] [--title text]
/// </code>
/// </summary>
public static class SheetCommandLine
{
    /// <summary>Usage text.</summary>
    public const string Usage =
        "       Vista.App --sheet --vary <key>=<v1>,<v2>,... --out <sheet.png> [--columns N] [--title <text>] [preview options]\n" +
        "                 keys: seed, recipe (files), camera (files or default), spp, bounces, camera.<field>, <recipe field>\n" +
        "                 e.g. --vary seed=1,2,3,4   --vary time=dawn,day,goldenHour,dusk   --vary camera.yawDegrees=0,90\n" +
        "                 Writes <sheet>.png, <sheet>.<debug>.png, <sheet>.json (every cell) and each cell under <sheet>.cells/.\n" +
        "       Vista.App --sheet-from <a.png> <b.png> ... --out <sheet.png> [--labels a,b,...] [--columns N] [--title <text>]\n" +
        "                 Tiles existing images, labelled from their sidecars (what differs between them) unless --labels is given.";

    private static readonly IReadOnlySet<string> SheetOptions = new HashSet<string>(StringComparer.Ordinal) { "--vary", "--columns", "--title" };

    /// <summary>Parses a <c>--sheet</c> command line. Returns false with every problem listed in <paramref name="errors"/>.</summary>
    public static bool TryParse(IReadOnlyList<string> args, out SheetRequest? request, out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(args);
        var problems = new List<string>();
        request = null;
        errors = problems;
        if (args.Count == 0 || args[0] != "--sheet")
        {
            problems.Add("The first argument must be --sheet.");
            return false;
        }

        var options = new HashSet<string>(PreviewCommandLine.ValueOptions, StringComparer.Ordinal);
        options.UnionWith(SheetOptions);
        (Dictionary<string, string> values, bool dryRun) = PreviewCommandLine.SplitOptions(args, 1, options, problems);

        SheetAxis? axis = null;
        if (!values.Remove("--vary", out string? vary))
            problems.Add("--vary is required.");
        else if (!SheetAxis.TryParse(vary, out axis, out string? axisError))
            problems.Add(axisError!);
        int columns = Columns(values, problems);
        string title = values.Remove("--title", out string? t) ? t : string.Empty;

        if (axis is not null)
        {
            if (axis.SuppliesRecipe && values.ContainsKey("--recipe"))
                problems.Add("--recipe and --vary recipe=… both choose the recipe; give one.");
            if (axis.SuppliesSeed && values.ContainsKey("--seed"))
                problems.Add("--seed and --vary seed=… both choose the seed; give one.");
            if (axis.SuppliesCamera && values.ContainsKey("--camera"))
                problems.Add("--camera and --vary camera=… both choose the camera; give one.");
            if (columns > axis.Values.Count)
                problems.Add($"--columns must be at most the number of cells ({axis.Values.Count}).");
        }
        PreviewRequest? baseRequest = PreviewCommandLine.Build(
            values, dryRun, recipeRequired: axis?.SuppliesRecipe != true, seedRequired: axis?.SuppliesSeed != true, problems);

        if (problems.Count > 0)
            return false;
        request = new SheetRequest { Base = baseRequest!, Axis = axis!, Columns = columns, Title = title };
        return true;
    }

    /// <summary>Parses a <c>--sheet-from</c> command line. Returns false with every problem listed in <paramref name="errors"/>.</summary>
    public static bool TryParseFrom(IReadOnlyList<string> args, out SheetFromRequest? request, out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(args);
        var problems = new List<string>();
        request = null;
        errors = problems;
        if (args.Count == 0 || args[0] != "--sheet-from")
        {
            problems.Add("The first argument must be --sheet-from.");
            return false;
        }

        int k = 1;
        var inputs = new List<string>();
        while (k < args.Count && !args[k].StartsWith("--", StringComparison.Ordinal))
            inputs.Add(args[k++]);
        if (inputs.Count == 0)
            problems.Add("--sheet-from needs at least one image.");
        else if (inputs.Count > ContactSheet.MaxTiles)
            problems.Add($"--sheet-from takes at most {ContactSheet.MaxTiles} images, got {inputs.Count}.");
        foreach (string input in inputs.Where(i => !i.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
            problems.Add($"'{input}' is not a .png file.");

        var options = new HashSet<string>(StringComparer.Ordinal) { "--out", "--labels", "--columns", "--title" };
        (Dictionary<string, string> values, bool dryRun) = PreviewCommandLine.SplitOptions(args, k, options, problems);
        if (dryRun)
            problems.Add("--dry-run does not apply to --sheet-from.");
        if (!values.TryGetValue("--out", out string? outPath))
            problems.Add("--out is required.");
        else if (!outPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            problems.Add("--out must name a .png file.");
        IReadOnlyList<string> labels = [];
        if (values.TryGetValue("--labels", out string? labelText))
        {
            try
            {
                labels = JsonOverrides.SplitList(labelText);
                if (labels.Count != inputs.Count)
                    problems.Add($"--labels has {labels.Count} labels for {inputs.Count} images.");
            }
            catch (FormatException e)
            {
                problems.Add($"--labels: {e.Message}");
            }
        }
        int columns = Columns(values, problems);
        if (inputs.Count > 0 && columns > inputs.Count)
            problems.Add($"--columns must be at most the number of images ({inputs.Count}).");

        if (problems.Count > 0)
            return false;
        request = new SheetFromRequest
        {
            Inputs = inputs,
            OutPath = outPath!,
            Labels = labels,
            Columns = columns,
            Title = values.GetValueOrDefault("--title", string.Empty),
        };
        return true;
    }

    private static int Columns(Dictionary<string, string> values, List<string> problems)
    {
        if (!values.Remove("--columns", out string? text))
            return 0;
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int columns) && columns >= 1)
            return columns;
        problems.Add($"--columns must be a positive integer, got '{text}'.");
        return 0;
    }
}
