using System.Diagnostics;
using Vista.Cli;
using Vista.Output;
using Vista.Rendering;
using Vista.World;

namespace Vista.Tweak;

/// <summary>One run the window asked for: what it runs, of which scene, from which files, writing where, and the CLI's own arguments for it.</summary>
/// <param name="Id">The job's number, from 1, in the order asked for.</param>
/// <param name="Kind">What it runs.</param>
/// <param name="Scene">The scene, as it was asked for.</param>
/// <param name="Paths">The files the run reads, written from <paramref name="Scene"/> just before it starts.</param>
/// <param name="OutPath">The PNG it writes; its siblings share the name.</param>
/// <param name="Arguments">The CLI's arguments for it (<see cref="TweakFiles.Arguments"/>).</param>
public sealed record TweakJob(int Id, TweakRun Kind, TweakScene Scene, TweakPaths Paths, string OutPath, IReadOnlyList<string> Arguments)
{
    /// <summary>The CLI command line that makes the same run (<see cref="TweakFiles.CommandLine"/>).</summary>
    public string CommandLine => TweakFiles.CommandLine(Arguments);
}

/// <summary>How a job ended: its sidecar, or why it failed, or that it was stopped; its wall time; the world it ran on; the image to show.</summary>
/// <param name="Job">The job.</param>
/// <param name="Sidecar">The sidecar it wrote; null when it failed or was stopped.</param>
/// <param name="Problem">Why it failed; null when it did not.</param>
/// <param name="Cancelled">True when it was stopped, or dropped before it started.</param>
/// <param name="Seconds">Its wall time, from writing the files to the sidecar, s; 0 for a job dropped before it started.</param>
/// <param name="World">The world it ran on (the session's last); null when it stopped before one.</param>
public sealed record TweakResult(TweakJob Job, Sidecar? Sidecar, string? Problem, bool Cancelled, double Seconds, TerrainWorld? World)
{
    /// <summary>True when the job ran to its sidecar.</summary>
    public bool Succeeded => Sidecar is not null;

    /// <summary>
    /// A preview's image as the window shows it: the file of its view (<see cref="TweakFiles.OutputRole"/>, the beauty when the view's
    /// file was not written), read back as soon as it was written; null for other jobs.
    /// </summary>
    public PreviewFrame? Image { get; init; }

    /// <summary>The sidecar's output role of <see cref="Image"/>.</summary>
    public string? ImageRole { get; init; }
}

/// <summary>What a <see cref="TweakRunner"/> runs its jobs on: a session, and what to dispose with it (its renderer, its denoiser).</summary>
/// <param name="Session">The session, made to keep the final's stages (<see cref="PreviewSession.KeepsFinalStages"/>).</param>
/// <param name="Owned">What the runner disposes when it ends.</param>
public sealed record TweakEngine(PreviewSession Session, IReadOnlyList<IDisposable> Owned);

/// <summary>
/// The window's worker (vista-plan §10 M1.16): one thread that owns the <see cref="PreviewSession"/> (one run at a time, as the session
/// requires) and runs the window's jobs on it, keeping every stage between them as a watch does. Previews are coalesced: only the newest
/// one asked for runs, and asking for anything newer stops a preview that is running (between stages or dispatches), so the preview
/// restarts after each change. The explicit jobs — the auto camera, the estimate, the final — run in the order asked for, before any
/// waiting preview, and only <see cref="Cancel"/> stops them. Before each job its scene's files are written, and its arguments parsed as
/// the CLI parses them. Every job ends with exactly one <see cref="Finished"/>: done, failed, stopped, or dropped unstarted.
///
/// <para>Its events are raised on the worker's thread, but for a job dropped before it started, whose <see cref="Finished"/> comes on
/// the thread that dropped it: a window marshals them all to its own.</para>
/// </summary>
public sealed class TweakRunner : IDisposable
{
    private readonly Func<Action<TraceProgress>, TweakEngine> _engine;
    private readonly Thread _thread;
    private readonly object _gate = new();
    private readonly List<TweakJob> _explicit = [];
    private TweakJob? _preview;
    private TweakJob? _running;
    private CancellationTokenSource? _stop;
    private bool _ending;
    private string? _broken;
    private int _jobs;

    /// <summary>
    /// A runner whose thread makes its engine with <paramref name="engine"/> when the first job comes (given the progress callback the
    /// session reports a final's trace to) and runs jobs until disposed.
    /// </summary>
    public TweakRunner(Func<Action<TraceProgress>, TweakEngine> engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        _thread = new Thread(Work) { IsBackground = true, Name = "Vista window runner" };
        _thread.Start();
    }

    /// <summary>Applied to each request before it runs (the app's measurement overrides); none by default.</summary>
    public Func<PreviewRequest, PreviewRequest>? Adjust { get; init; }

    /// <summary>The least wall time between two preview frames, s (<see cref="PreviewFeed.IntervalSeconds"/>).</summary>
    public double FrameIntervalSeconds { get; init; } = 0.2;

    /// <summary>Raised when a job starts.</summary>
    public event Action<TweakJob>? Started;

    /// <summary>Raised with a preview's image after each pass (<see cref="PreviewFeed.Frame"/>).</summary>
    public event Action<TweakJob, PreviewFrame>? Frame;

    /// <summary>Raised with a final's trace progress and live ETA after each dispatch.</summary>
    public event Action<TweakJob, TraceProgress>? Progress;

    /// <summary>Raised once for every job: done, failed, stopped, or dropped before it started.</summary>
    public event Action<TweakResult>? Finished;

    /// <summary>Raised when the engine could not be made (no DXR GPU, say): the runner then runs nothing.</summary>
    public event Action<string>? Broken;

    /// <summary>True while a job runs or waits.</summary>
    public bool Busy
    {
        get
        {
            lock (_gate)
                return IsBusy;
        }
    }

    private bool IsBusy => _running is not null || _preview is not null || _explicit.Count > 0;

    /// <summary>
    /// Asks for a <paramref name="kind"/> run of <paramref name="scene"/> from the files at <paramref name="paths"/>, writing
    /// <paramref name="outPath"/>; returns the job. A preview replaces a preview that waits (which ends, dropped) and stops one that runs;
    /// any other job queues, and stops a preview that runs too: it is stale once anything newer is asked for.
    /// </summary>
    public TweakJob Submit(TweakRun kind, TweakScene scene, TweakPaths paths, string outPath)
    {
        IReadOnlyList<string> args = TweakFiles.Arguments(scene, paths, kind, outPath);
        TweakJob? dropped = null;
        TweakJob job;
        string? broken;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_ending && _broken is null, this);
            job = new TweakJob(++_jobs, kind, scene, paths, outPath, args);
            broken = _broken;
            if (broken is null)
            {
                if (kind == TweakRun.Preview)
                    (dropped, _preview) = (_preview, job);
                else
                    _explicit.Add(job);
                if (_running?.Kind == TweakRun.Preview)
                    _stop?.Cancel();
                Monitor.PulseAll(_gate);
            }
        }
        if (dropped is not null)
            Finished?.Invoke(new TweakResult(dropped, null, null, true, 0.0, null));
        if (broken is not null)
            Finished?.Invoke(new TweakResult(job, null, broken, false, 0.0, null));
        return job;
    }

    /// <summary>
    /// Stops <paramref name="job"/>: between its stages or dispatches when it runs, at once when it waits (it ends, dropped). Nothing
    /// when it has ended.
    /// </summary>
    public void Cancel(TweakJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        bool dropped = false;
        lock (_gate)
        {
            if (ReferenceEquals(_running, job))
                _stop?.Cancel();
            else if (ReferenceEquals(_preview, job))
                (_preview, dropped) = (null, true);
            else
                dropped = _explicit.Remove(job);
            Monitor.PulseAll(_gate);
        }
        if (dropped)
            Finished?.Invoke(new TweakResult(job, null, null, true, 0.0, null));
    }

    /// <summary>Waits until no job runs or waits, or <paramref name="timeout"/> passes; true when idle.</summary>
    public bool WaitIdle(TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        lock (_gate)
        {
            while (IsBusy)
            {
                TimeSpan left = timeout - clock.Elapsed;
                if (left <= TimeSpan.Zero || _ending)
                    return false;
                Monitor.Wait(_gate, left);
            }
            return true;
        }
    }

    /// <summary>Stops the running job, drops the waiting ones, and ends the thread, which disposes the engine.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_ending)
                return;
            _ending = true;
            _preview = null;
            _explicit.Clear();
            _stop?.Cancel();
            Monitor.PulseAll(_gate);
        }
        _thread.Join(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// The worker: it makes the engine when the first job comes (so whoever asked for it hears when there is none), then runs every job
    /// until the runner ends, and disposes the engine.
    /// </summary>
    private void Work()
    {
        TweakEngine? engine = null;
        try
        {
            while (Next() is { } job)
            {
                if (engine is null && !TryMakeEngine(job, out engine))
                    return;
                RunJob(engine!.Session, job);
            }
        }
        finally
        {
            foreach (IDisposable owned in engine?.Owned ?? [])
                owned.Dispose();
        }
    }

    /// <summary>
    /// Makes the engine for <paramref name="first"/>, the first job. Without one (no DXR GPU, a driver that refuses) every job, this one
    /// and those after it, ends with the reason, and <see cref="Broken"/> says it once.
    /// </summary>
    private bool TryMakeEngine(TweakJob first, out TweakEngine? engine)
    {
        try
        {
            engine = _engine(OnProgress);
            return true;
        }
#pragma warning disable CA1031 // No engine: the window says why and stays open.
        catch (Exception e)
#pragma warning restore CA1031
        {
            engine = null;
            List<TweakJob> waiting = [first];
            lock (_gate)
            {
                // From here a job asked for ends at once with the reason; those waiting end below, before the runner is idle.
                _broken = e.Message;
                waiting.AddRange(_explicit);
                if (_preview is not null)
                    waiting.Add(_preview);
                _preview = null;
                _explicit.Clear();
            }
            Broken?.Invoke(e.Message);
            foreach (TweakJob job in waiting)
                Finished?.Invoke(new TweakResult(job, null, e.Message, false, 0.0, null));
            lock (_gate)
            {
                _ending = true;
                _running = null;
                _stop?.Dispose();
                _stop = null;
                Monitor.PulseAll(_gate);
            }
            return false;
        }
    }

    /// <summary>The next job, the explicit ones first; null once the runner ends.</summary>
    private TweakJob? Next()
    {
        lock (_gate)
        {
            while (true)
            {
                if (_ending)
                    return null;
                TweakJob? job = null;
                if (_explicit.Count > 0)
                {
                    job = _explicit[0];
                    _explicit.RemoveAt(0);
                }
                else if (_preview is not null)
                {
                    (job, _preview) = (_preview, null);
                }
                if (job is not null)
                {
                    _running = job;
                    _stop = new CancellationTokenSource();
                    return job;
                }
                Monitor.Wait(_gate);
            }
        }
    }

    private void RunJob(PreviewSession session, TweakJob job)
    {
        CancellationToken stop;
        lock (_gate)
            stop = _stop!.Token;
        Started?.Invoke(job);
        var clock = Stopwatch.StartNew();
        TweakResult result;
        try
        {
            stop.ThrowIfCancellationRequested();
            TweakFiles.Write(job.Scene, job.Paths);
            PreviewRequest request = TweakFiles.Request(job.Arguments);
            if (Adjust is { } adjust)
                request = adjust(request);
            var feed = new PreviewFeed
            {
                Cancellation = stop,
                Frame = job.Kind == TweakRun.Preview ? frame => Frame?.Invoke(job, frame) : null,
                IntervalSeconds = FrameIntervalSeconds,
            };
            Sidecar sidecar = session.Run(request, feed: feed);
            result = new TweakResult(job, sidecar, null, false, clock.Elapsed.TotalSeconds, session.LastWorld);
            if (job.Kind is TweakRun.Preview or TweakRun.AutoCamera)
                result = WithImage(result, sidecar);
        }
        catch (OperationCanceledException)
        {
            result = new TweakResult(job, null, null, true, clock.Elapsed.TotalSeconds, session.LastWorld);
        }
#pragma warning disable CA1031 // A run's failure of any kind is the window's to show, never the end of its worker.
        catch (Exception e)
#pragma warning restore CA1031
        {
            result = new TweakResult(job, null, e.Message, false, clock.Elapsed.TotalSeconds, session.LastWorld);
        }
        // Reported before the job is let go, so a runner found idle has reported every job it ran.
        Finished?.Invoke(result);
        lock (_gate)
        {
            _running = null;
            _stop?.Dispose();
            _stop = null;
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary><paramref name="result"/> with the image its view shows, read from the file the run just wrote.</summary>
    private static TweakResult WithImage(TweakResult result, Sidecar sidecar)
    {
        string role = TweakFiles.OutputRole(result.Job.Scene.Preview.View);
        if (!sidecar.Outputs.ContainsKey(role))
            role = "beauty";
        if (!sidecar.Outputs.TryGetValue(role, out string? file))
            return result;
        PngImage png = PngDecoder.Read(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(result.Job.OutPath))!, file));
        int samples = sidecar.Image.SamplesPerPixel;
        return result with { Image = new PreviewFrame(png.Width, png.Height, samples, samples, png.ToRgb()), ImageRole = role };
    }

    private void OnProgress(TraceProgress progress)
    {
        TweakJob? job;
        lock (_gate)
            job = _running;
        if (job is { Kind: TweakRun.Final })
            Progress?.Invoke(job, progress);
    }
}
