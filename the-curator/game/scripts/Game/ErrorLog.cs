using System.Threading;
using Godot;

namespace Curator.Presentation.Game;

/// <summary>
/// Counts errors logged by the engine or by scripts, so <c>--autoplay</c> can exit with 0 only when
/// none were (BUILD_BRIEF §7.11). Warnings aren't counted.
/// </summary>
public partial class ErrorLog : Logger
{
    private static int errors;

    /// <summary>Errors logged since the game started.</summary>
    public static int Errors => Volatile.Read(ref errors);

    /// <summary>Registers the logger with the engine.</summary>
    public static void Install() => OS.AddLogger(new ErrorLog());

    /// <inheritdoc />
    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        if (errorType != (int)ErrorType.Warning)
        {
            Interlocked.Increment(ref errors);
        }
    }
}
