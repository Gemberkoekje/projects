using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vista.Cli;

/// <summary>One golden: a preview at a fixed seed whose PNG and EXR are committed beside the suite (vista-plan §10 M1.17).</summary>
/// <param name="Name">The goldens' file name: <c>&lt;name&gt;.png</c> and <c>&lt;name&gt;.exr</c> in the suite's folder.</param>
/// <param name="Checks">Which M1 check the case keeps, in words.</param>
/// <param name="Arguments">
/// The preview's own options, without <c>--preview</c> and <c>--out</c>: a recipe, camera or slider file is named relative to the suite's
/// folder.
/// </param>
public sealed record GoldenCase(string Name, string Checks, IReadOnlyList<string> Arguments);

/// <summary>
/// How far a golden may move and still pass (vista-plan §10 M1.17): a driver or compiler change can change a few operations' rounding,
/// and a path that takes another turn re-rolls its pixel's noise, so the image is compared as its mean and its blocks' means, where
/// the noise averages out, not pixel by pixel.
/// </summary>
/// <param name="MeanRelative">The most the EXR's mean luminance may move, as a share of the golden's.</param>
/// <param name="BlockRelative">The most the EXR's blocks' luminance may move, root mean square, as a share of the golden's mean luminance.</param>
/// <param name="BlockPixels">The blocks' side, in pixels.</param>
/// <param name="PngMeanLevels">The most any of the PNG's channel means may move, in 8-bit levels.</param>
public sealed record GoldenTolerance(double MeanRelative, double BlockRelative, int BlockPixels, double PngMeanLevels)
{
    /// <summary>The tolerance unless the suite gives one.</summary>
    public static GoldenTolerance Default { get; } = new(0.01, 0.03, 16, 1.0);
}

/// <summary>The Vista golden suite (vista-plan §10 M1.17): the cases and their tolerance, read from <c>vista-views/goldens/suite.json</c>.</summary>
public sealed record GoldenSuite
{
    /// <summary>The suite file's format version this build reads.</summary>
    public const int CurrentSchema = 1;

    /// <summary>The suite file's format version.</summary>
    public int Schema { get; init; } = CurrentSchema;

    /// <summary>What a golden may move by and still pass.</summary>
    public GoldenTolerance Tolerance { get; init; } = GoldenTolerance.Default;

    /// <summary>The cases, in the order they run.</summary>
    public IReadOnlyList<GoldenCase> Cases { get; init; } = [];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Parses a suite file's text.</summary>
    /// <exception cref="InvalidDataException">The suite is not one this build reads: its schema, a case without a name, a name twice.</exception>
    public static GoldenSuite Parse(string text)
    {
        GoldenSuite suite = JsonSerializer.Deserialize<GoldenSuite>(text, Json) ?? throw new InvalidDataException("The golden suite is empty.");
        var problems = new List<string>();
        if (suite.Schema != CurrentSchema)
            problems.Add($"Unsupported golden suite schema {suite.Schema}; this build reads {CurrentSchema}.");
        if (suite.Cases.Count == 0)
            problems.Add("The golden suite has no cases.");
        foreach (GoldenCase c in suite.Cases)
        {
            if (string.IsNullOrWhiteSpace(c.Name) || c.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                problems.Add($"A golden's name must be a file name, got '{c.Name}'.");
            if (c.Arguments.Any(a => a is "--preview" or "--out"))
                problems.Add($"{c.Name}: a golden's arguments leave out --preview and --out.");
        }
        foreach (string twice in suite.Cases.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key))
            problems.Add($"The golden '{twice}' is named twice.");
        if (problems.Count > 0)
            throw new InvalidDataException(string.Join(" ", problems));
        return suite;
    }

    /// <summary>The options whose value is a file, named relative to the suite's folder.</summary>
    private static readonly HashSet<string> FileOptions = new(StringComparer.Ordinal) { "--recipe", "--camera", "--sliders" };

    /// <summary>
    /// The preview command line of <paramref name="golden"/>: <c>--preview</c>, its arguments with every file resolved against
    /// <paramref name="suiteFolder"/>, and <c>--out</c> <paramref name="outPng"/>.
    /// </summary>
    public static IReadOnlyList<string> CommandLine(GoldenCase golden, string suiteFolder, string outPng)
    {
        ArgumentNullException.ThrowIfNull(golden);
        var args = new List<string> { "--preview" };
        for (int i = 0; i < golden.Arguments.Count; i++)
        {
            string a = golden.Arguments[i];
            args.Add(a);
            if (FileOptions.Contains(a) && i + 1 < golden.Arguments.Count)
                args.Add(Path.GetFullPath(Path.Combine(suiteFolder, golden.Arguments[++i])));
        }
        args.Add("--out");
        args.Add(outPng);
        return args;
    }
}
