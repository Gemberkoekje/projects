using System.Text;

namespace Vista.Cli;

/// <summary>A parsed <c>--watch</c> invocation: the preview options of the command line and the options file that adds to them.</summary>
public sealed record WatchRequest
{
    /// <summary>The command line's <c>--option value</c> pairs (every <c>--preview</c> option but the flags).</summary>
    public required IReadOnlyDictionary<string, string> Values { get; init; }

    /// <summary>The command line's flags (<c>--dry-run</c>, <c>--denoise</c>).</summary>
    public required IReadOnlySet<string> Flags { get; init; }

    /// <summary>The options file, read again whenever it changes; empty for none.</summary>
    public string OptionsPath { get; init; } = string.Empty;

    /// <summary>True when the session renders nothing (<c>--dry-run</c>): it needs no GPU, and its options file cannot change that.</summary>
    public bool DryRun => Flags.Contains("--dry-run");
}

/// <summary>
/// Parses the watch CLI (vista-plan §7.4, §10 M1.8):
/// <code>
/// Vista.App --watch --recipe r.json --seed N [--camera c.json] --out p.png [any --preview option] [--options o.txt]
/// </code>
/// The options file holds more <c>--preview</c> options, as on a command line (<c>#</c> starts a comment, double quotes keep a
/// value with spaces together); they override the command line's and are read again whenever the file changes. So everything
/// that is not in the recipe or the camera — the seed above all — can change while the session runs, by editing a file, which a
/// script or an agent can do as easily as a person. The options file may supply the required options itself.
/// </summary>
public static class WatchCommandLine
{
    /// <summary>The option naming the options file.</summary>
    public const string OptionsOption = "--options";

    /// <summary>Usage text.</summary>
    public const string Usage =
        "Usage: Vista.App --watch --recipe <recipe.json> --seed <n> [--camera <camera.json>] --out <preview.png> [any --preview option]\n" +
        "                 [--options <options.txt>]\n" +
        "Renders once, then again whenever the recipe, the slider file, the camera or the options file changes, rebuilding only\n" +
        "the stages the change touches (world, mesh, sky, render, exposure, denoised image) and rewriting <preview>.* and its\n" +
        "sidecar, which lists the stages built and reused. The options file holds more --preview options (# starts a comment);\n" +
        "they override the command line's and are read again whenever it changes: put --seed, --time or --width there to change\n" +
        "them. A file that does not parse is reported and the session keeps watching. Type quit (or press Ctrl+C) to stop.";

    /// <summary>The options a watch takes on its command line: every <c>--preview</c> option and the options file.</summary>
    private static IReadOnlySet<string> ValueOptions { get; } = new HashSet<string>(PreviewCommandLine.ValueOptions, StringComparer.Ordinal) { OptionsOption };

    /// <summary>Parses <paramref name="args"/>. Returns false with every problem listed in <paramref name="errors"/>.</summary>
    public static bool TryParse(IReadOnlyList<string> args, out WatchRequest? request, out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(args);
        var problems = new List<string>();
        request = null;
        errors = problems;
        if (args.Count == 0 || args[0] != "--watch")
        {
            problems.Add("The first argument must be --watch.");
            return false;
        }

        (Dictionary<string, string> values, IReadOnlySet<string> flags) = PreviewCommandLine.SplitOptions(args, 1, ValueOptions, problems);
        string optionsPath = values.Remove(OptionsOption, out string? path) ? path : string.Empty;
        if (optionsPath.Length == 0)
        {
            // Without an options file the command line is all there is, so it must make a request now.
            PreviewCommandLine.Build(values, flags, recipeRequired: true, seedRequired: true, problems);
        }
        else if (!File.Exists(optionsPath))
        {
            problems.Add($"No options file at '{optionsPath}'.");
        }
        if (problems.Count > 0)
            return false;
        request = new WatchRequest { Values = values, Flags = flags, OptionsPath = optionsPath };
        return true;
    }

    /// <summary>
    /// The request of <paramref name="watch"/>'s command line with the options file's text <paramref name="options"/> on top: its
    /// values replace the command line's and its flags add to them. Null, with every problem in <paramref name="problems"/>, when the
    /// two do not make a request. The file may not name another options file or change <c>--dry-run</c>.
    /// </summary>
    public static PreviewRequest? Merge(WatchRequest watch, string options, List<string> problems,
        out Dictionary<string, string> values, out HashSet<string> flags)
    {
        ArgumentNullException.ThrowIfNull(watch);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(problems);
        values = new Dictionary<string, string>(watch.Values, StringComparer.Ordinal);
        flags = new HashSet<string>(watch.Flags, StringComparer.Ordinal);
        int before = problems.Count;
        IReadOnlyList<string> tokens = Tokenize(options, problems);
        (Dictionary<string, string> fileValues, IReadOnlySet<string> fileFlags) = PreviewCommandLine.SplitOptions(tokens, 0, ValueOptions, problems);
        if (fileValues.ContainsKey(OptionsOption))
            problems.Add($"The options file cannot name another ({OptionsOption}).");
        if (fileFlags.Contains("--dry-run"))
            problems.Add("--dry-run cannot change while a watch runs; give it on the command line.");
        foreach ((string option, string value) in fileValues)
            values[option] = value;
        flags.UnionWith(fileFlags);
        if (problems.Count > before)
            return null;
        PreviewRequest? request = PreviewCommandLine.Build(values, flags, recipeRequired: true, seedRequired: true, problems);
        return problems.Count > before ? null : request;
    }

    /// <summary>
    /// Splits an options file into arguments: whitespace separates them, <c>#</c> starts a comment that runs to the end of the
    /// line, and double quotes keep a value with spaces together (<c>--out "my renders/p.png"</c>).
    /// </summary>
    internal static IReadOnlyList<string> Tokenize(string text, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = new List<string>();
        var token = new StringBuilder();
        bool inToken = false;
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    quoted = false;
                    continue;
                }
                if (c is not ('\n' or '\r'))
                {
                    token.Append(c);
                    continue;
                }
                // The line break ends the quote and the token: a value never spans lines.
                problems.Add("A quoted value in the options file runs past the end of its line.");
                quoted = false;
            }
            if (c == '"')
            {
                quoted = true;
                inToken = true;
            }
            else if (c == '#' && !inToken)
            {
                while (i + 1 < text.Length && text[i + 1] is not ('\n' or '\r'))
                    i++;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inToken)
                    tokens.Add(token.ToString());
                token.Clear();
                inToken = false;
            }
            else
            {
                token.Append(c);
                inToken = true;
            }
        }
        if (quoted)
            problems.Add("A quoted value in the options file is not closed.");
        if (inToken)
            tokens.Add(token.ToString());
        return tokens;
    }
}
