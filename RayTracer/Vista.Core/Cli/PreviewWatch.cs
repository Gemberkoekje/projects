using System.Text.Json;
using System.Text.Json.Nodes;
using Vista.Output;

namespace Vista.Cli;

/// <summary>What one <see cref="PreviewWatch.Poll"/> came to.</summary>
public enum WatchEventKind
{
    /// <summary>Not set.</summary>
    None,

    /// <summary>Nothing to do: no input changed, or a change is still settling.</summary>
    Idle,

    /// <summary>The inputs changed (or this was the first poll) and the preview was rendered.</summary>
    Rendered,

    /// <summary>The inputs changed but do not make a preview (a file does not parse, an option is wrong); the session keeps watching.</summary>
    Failed,

    /// <summary>An input file has been missing or locked for a while; the session waits for it.</summary>
    Waiting,
}

/// <summary>One step of a watch session.</summary>
/// <param name="Kind">What happened.</param>
public sealed record WatchEvent(WatchEventKind Kind)
{
    /// <summary>The sidecar of a <see cref="WatchEventKind.Rendered"/> event.</summary>
    public Sidecar? Sidecar { get; init; }

    /// <summary>Why a <see cref="WatchEventKind.Failed"/> or <see cref="WatchEventKind.Waiting"/> event happened.</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Watch mode (vista-plan §7.4, §10 M1.8): a <see cref="PreviewSession"/> that renders again whenever the recipe, the slider file
/// (since M1.11), the camera or the options file (<see cref="WatchCommandLine"/>) changes. The session keeps the device, the world, the mesh, the acceleration
/// structure, the atmosphere LUTs and the last render alive between renders and rebuilds only what a change touched; each render's
/// sidecar records which stages were built and which reused, and what changed (<see cref="WatchInfo"/>).
///
/// <para>The files are polled every <see cref="PollInterval"/> and compared by content, so saving a file unchanged renders nothing.
/// A change is acted on once the files have read the same for <see cref="Settle"/>, so an editor's save in several writes is one
/// change, and several files saved together are one render. A file that is missing or locked (mid-save) is waited for; one that does
/// not parse, or options that do not make a preview, are reported once and the session keeps watching until they are fixed. The M1.16
/// window drives the same session.</para>
///
/// <para>Not thread-safe: one poll at a time.</para>
/// </summary>
public sealed class PreviewWatch
{
    /// <summary>How often the files are read.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>How long a change must read the same before it is rendered.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(100);

    /// <summary>How long a file may stay missing or locked before the session says it is waiting for it.</summary>
    public static readonly TimeSpan MissingGrace = TimeSpan.FromSeconds(1);

    private readonly WatchRequest _request;
    private readonly PreviewSession _session;
    private readonly TimeProvider _time;
    private Snapshot? _attempted;
    private Snapshot? _rendered;
    private Snapshot? _pending;
    private DateTimeOffset _pendingSince;
    private string _missing = string.Empty;
    private DateTimeOffset _missingSince;
    private bool _missingReported;

    /// <summary>A watch of <paramref name="request"/>'s files, rendering through <paramref name="session"/>.</summary>
    public PreviewWatch(WatchRequest request, PreviewSession session, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        if (!request.DryRun && !session.CanRender)
            throw new ArgumentException("A watch that renders needs a session with a renderer.", nameof(session));
        _request = request;
        _session = session;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Renders so far.</summary>
    public int Renders { get; private set; }

    /// <summary>The files the last poll read: the options file, the recipe, the slider file and the camera (when given).</summary>
    public IReadOnlyList<string> Watched { get; private set; } = [];

    /// <summary>
    /// Polls until <paramref name="stop"/> is cancelled, handing every event but <see cref="WatchEventKind.Idle"/> to
    /// <paramref name="report"/>. A render in progress finishes first. Errors that are not about the inputs (a lost GPU) end it.
    /// </summary>
    public void Run(Action<WatchEvent> report, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(report);
        while (!stop.IsCancellationRequested)
        {
            WatchEvent step = Poll();
            if (step.Kind == WatchEventKind.Idle)
                stop.WaitHandle.WaitOne(PollInterval);
            else
                report(step);
        }
    }

    /// <summary>Reads the files once and renders when they changed and settled (the first poll renders at once).</summary>
    public WatchEvent Poll()
    {
        DateTimeOffset now = _time.GetUtcNow();
        Snapshot snapshot = Read();
        Watched = snapshot.Paths;
        if (snapshot.Missing.Length > 0)
        {
            _pending = null;
            if (_missing != snapshot.Missing)
            {
                (_missing, _missingSince, _missingReported) = (snapshot.Missing, now, false);
            }
            else if (!_missingReported && now - _missingSince >= MissingGrace)
            {
                _missingReported = true;
                return new WatchEvent(WatchEventKind.Waiting) { Message = $"Waiting for '{snapshot.Missing}': it is missing or cannot be read." };
            }
            return new WatchEvent(WatchEventKind.Idle);
        }
        (_missing, _missingReported) = (string.Empty, false);

        double waited = 0.0;
        if (_attempted is not null)
        {
            if (snapshot.SameInputs(_attempted))
            {
                _pending = null;
                return new WatchEvent(WatchEventKind.Idle);
            }
            if (_pending is null || !snapshot.SameInputs(_pending))
            {
                (_pending, _pendingSince) = (snapshot, now);
                return new WatchEvent(WatchEventKind.Idle);
            }
            if (now - _pendingSince < Settle)
                return new WatchEvent(WatchEventKind.Idle);
            waited = (now - _pendingSince).TotalSeconds;
        }

        // Act on this snapshot once, whatever comes of it: a broken file is reported once, not on every poll.
        _pending = null;
        _attempted = snapshot;
        if (snapshot.Request is null)
            return new WatchEvent(WatchEventKind.Failed) { Message = snapshot.Problem };
        IReadOnlyList<string> changes = _rendered is null ? [] : Changes(_rendered, snapshot);
        try
        {
            Sidecar sidecar = _session.Run(snapshot.Request, new WatchInfo(Renders + 1, changes, waited));
            Renders++;
            _rendered = snapshot;
            return new WatchEvent(WatchEventKind.Rendered) { Sidecar = sidecar };
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or IOException or UnauthorizedAccessException or ArgumentException
            or InsufficientMemoryException)
        {
            // A mesh too fine for the GPU (M1.9) is reported like a bad file: the next change may make one that fits.
            return new WatchEvent(WatchEventKind.Failed) { Message = e.Message };
        }
    }

    /// <summary>The options file, the request it makes with the command line, and the files that request reads, as they are now.</summary>
    private Snapshot Read()
    {
        var paths = new List<string>();
        string options = string.Empty;
        if (_request.OptionsPath.Length > 0)
        {
            paths.Add(_request.OptionsPath);
            if (!TryRead(_request.OptionsPath, out options))
                return new Snapshot(options, null, string.Empty, [], _request.OptionsPath, paths, new(), new());
        }

        var problems = new List<string>();
        PreviewRequest? request = WatchCommandLine.Merge(_request, options, problems, out Dictionary<string, string> values, out HashSet<string> flags);
        if (request is null)
            return new Snapshot(options, null, string.Join(" ", problems), [], string.Empty, paths, values, flags);

        var files = new List<InputFile>();
        foreach ((string role, string? path) in new[] { ("recipe", request.RecipePath), ("sliders", request.SlidersPath), ("camera", request.CameraPath) })
        {
            if (path is null)
                continue;
            paths.Add(path);
            if (!TryRead(path, out string text))
                return new Snapshot(options, null, string.Empty, [], path, paths, values, flags);
            files.Add(new InputFile(role, path, text));
        }
        return new Snapshot(options, request, string.Empty, files, string.Empty, paths, values, flags);
    }

    private static bool TryRead(string path, out string text)
    {
        text = string.Empty;
        try
        {
            if (!File.Exists(path))
                return false;
            text = File.ReadAllText(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// What changed from <paramref name="before"/> to <paramref name="after"/>: each option whose value changed (<c>--seed: 42 → 7</c>),
    /// and each field of a JSON file that did (<c>camera.exposureEv: 0 → 1</c>).
    /// </summary>
    internal static IReadOnlyList<string> Changes(Snapshot before, Snapshot after)
    {
        var changes = new List<string>();
        foreach (string option in before.Values.Keys.Union(after.Values.Keys).Order(StringComparer.Ordinal))
        {
            string was = before.Values.GetValueOrDefault(option, "(none)");
            string now = after.Values.GetValueOrDefault(option, "(none)");
            if (was != now)
                changes.Add($"{option}: {was} → {now}");
        }
        changes.AddRange(before.Flags.Union(after.Flags).Order(StringComparer.Ordinal)
            .Where(flag => before.Flags.Contains(flag) != after.Flags.Contains(flag))
            .Select(flag => $"{flag}: {(before.Flags.Contains(flag) ? "on → off" : "off → on")}"));
        foreach (InputFile file in after.Files)
        {
            // A file that is another file now shows as its option's change above.
            if (before.Files.FirstOrDefault(f => f.Role == file.Role) is { } old && old.Path == file.Path && old.Text != file.Text)
                changes.AddRange(JsonChanges(file.Role, old.Text, file.Text));
        }
        return changes;
    }

    /// <summary>The fields of two JSON documents that differ, as <c>role.path: before → after</c>.</summary>
    internal static IReadOnlyList<string> JsonChanges(string role, string before, string after)
    {
        JsonNode? a = TryParse(before);
        JsonNode? b = TryParse(after);
        if (a is null || b is null)
            return [$"{role}: {(b is null ? "not valid JSON" : "valid JSON again")}"];
        var changes = new List<string>();
        Compare(role, a, b, changes);
        return changes.Count > 0 ? changes : [$"{role}: reformatted, no field changed"];
    }

    private static void Compare(string path, JsonNode? a, JsonNode? b, List<string> changes)
    {
        if (a is JsonObject objectA && b is JsonObject objectB)
        {
            foreach (string key in objectA.Select(p => p.Key).Union(objectB.Select(p => p.Key)))
                Compare(path + "." + key, objectA[key], objectB[key], changes);
            return;
        }
        if (!JsonNode.DeepEquals(a, b))
            changes.Add($"{path}: {Show(a)} → {Show(b)}");
    }

    private static string Show(JsonNode? node) => node switch
    {
        null => "(none)",
        JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
        _ => node.ToJsonString(),
    };

    private static JsonNode? TryParse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>One input file as read.</summary>
    internal readonly record struct InputFile(string Role, string Path, string Text);

    /// <summary>
    /// The inputs at one poll: the options file's text, the request it makes (null with the <paramref name="Problem"/> when it makes
    /// none), the files that request reads, and a file that could not be read (<paramref name="Missing"/>, then nothing else is known).
    /// </summary>
    internal sealed record Snapshot(
        string Options,
        PreviewRequest? Request,
        string Problem,
        IReadOnlyList<InputFile> Files,
        string Missing,
        IReadOnlyList<string> Paths,
        Dictionary<string, string> Values,
        HashSet<string> Flags)
    {
        /// <summary>True when both read the same options and the same files with the same contents.</summary>
        public bool SameInputs(Snapshot other) => Options == other.Options && Files.SequenceEqual(other.Files) && Missing == other.Missing
            && Paths.SequenceEqual(other.Paths, StringComparer.Ordinal);
    }
}
