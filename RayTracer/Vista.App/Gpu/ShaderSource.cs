using System.Text;
using System.Text.RegularExpressions;

namespace Vista.App.Gpu;

/// <summary>
/// Reads a Vista shader for <c>ShaderCompiler.CompileCompute</c>, which takes one source string with no include
/// handler and no defines (vista-plan §8.1: only that engine piece is reused, unchanged). Each
/// <c>#include "file"</c> line is replaced by the named file from the same folder, one level deep, wrapped in
/// <c>#line</c> directives so compiler errors still name the right file and line; the defines are prepended.
/// </summary>
internal static partial class ShaderSource
{
    /// <summary>
    /// An environment variable of extra defines for measurements (comma-separated, <c>NAME</c> or
    /// <c>NAME=VALUE</c>), e.g. <c>SINGLE_SCATTERING_ONLY</c>, which drops multiple scattering from both the LUTs
    /// and the reference so <c>--atmosphere-check</c> can test the single-scattering machinery alone.
    /// </summary>
    public const string DiagnosticDefinesVariable = "VISTA_SHADER_DEFINES";

    /// <summary>The source of <paramref name="file"/> in the app's <c>Shaders</c> folder, includes inlined, with <paramref name="defines"/> set to 1.</summary>
    public static string Load(string file, params string[] defines)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Shaders");
        string path = Path.Combine(folder, file);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Shader not found at '{path}'.");

        var text = new StringBuilder();
        string[] diagnostics = (Environment.GetEnvironmentVariable(DiagnosticDefinesVariable) ?? string.Empty)
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string define in defines.Concat(diagnostics))
        {
            int eq = define.IndexOf('=', StringComparison.Ordinal);
            text.Append("#define ").Append(eq < 0 ? define + " 1" : define[..eq] + " " + define[(eq + 1)..]).Append('\n');
        }
        string[] lines = File.ReadAllLines(path);
        text.Append("#line 1 \"").Append(file).Append("\"\n");
        for (int i = 0; i < lines.Length; i++)
        {
            Match include = IncludeLine().Match(lines[i]);
            if (!include.Success)
            {
                text.Append(lines[i]).Append('\n');
                continue;
            }
            string name = include.Groups["name"].Value;
            string included = Path.Combine(folder, name);
            if (!File.Exists(included))
                throw new FileNotFoundException($"Shader include '{name}' (from {file}) not found at '{included}'.");
            text.Append("#line 1 \"").Append(name).Append("\"\n");
            text.Append(File.ReadAllText(included)).Append('\n');
            text.Append("#line ").Append(i + 2).Append(" \"").Append(file).Append("\"\n");
        }
        return text.ToString();
    }

    [GeneratedRegex("^\\s*#include\\s+\"(?<name>[^\"]+)\"\\s*$")]
    private static partial Regex IncludeLine();
}
