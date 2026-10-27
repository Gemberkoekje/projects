namespace Vista.Rendering;

/// <summary>
/// One pass of a trace (<see cref="BatchPlanner"/>): every row of the image at the samples
/// <c>[FirstSample, FirstSample + SampleCount)</c>.
/// </summary>
/// <param name="FirstSample">The pass's first per-pixel sample index.</param>
/// <param name="SampleCount">Samples per pixel in the pass.</param>
/// <param name="PixelSamples">Pixel-samples the pass traced.</param>
/// <param name="GpuSeconds">GPU time of its dispatches (timestamps around the work alone).</param>
/// <param name="WallSeconds">Wall time from the end of the previous pass (or the first dispatch issued) to the end of this one.</param>
/// <param name="Dispatches">Planned batches in the pass.</param>
public sealed record PassTiming(int FirstSample, int SampleCount, long PixelSamples, double GpuSeconds, double WallSeconds, int Dispatches)
{
    /// <summary>GPU nanoseconds per pixel-sample over the pass.</summary>
    public double CostNs => PixelSamples > 0 ? GpuSeconds * 1e9 / PixelSamples : 0.0;
}

/// <summary>
/// What the live ETA said when a trace first reached <paramref name="Progress"/> (vista-plan §10 M1.15): the wall time so far and
/// the wall time it predicted was left. <paramref name="ActualRemainingSeconds"/> is filled in once the trace has ended.
/// </summary>
/// <param name="Progress">The share of the pixel-samples done, 0 to 1.</param>
/// <param name="ElapsedSeconds">Wall seconds since the first dispatch was issued.</param>
/// <param name="RemainingSeconds">The predicted wall seconds left; null before the clock had a cost for every row.</param>
/// <param name="ActualRemainingSeconds">The wall seconds that were in fact left (null while the trace runs).</param>
public sealed record EtaCheckpoint(double Progress, double ElapsedSeconds, double? RemainingSeconds, double? ActualRemainingSeconds = null);

/// <summary>A trace's state after one of its batches, for a progress line (vista-plan §10 M1.15).</summary>
/// <param name="Progress">The share of the pixel-samples done, 0 to 1.</param>
/// <param name="ElapsedSeconds">Wall seconds since the first dispatch was issued.</param>
/// <param name="RemainingSeconds">The predicted wall seconds left; null while some row has no measured cost yet.</param>
/// <param name="PassesDone">Passes finished.</param>
public readonly record struct TraceProgress(double Progress, double ElapsedSeconds, double? RemainingSeconds, int PassesDone);

/// <summary>
/// The wall clock of one trace (vista-plan §7.2, §10 M1.15): it watches a <see cref="BatchPlanner"/>'s batches, times them on the wall,
/// sums them by pass, and predicts the wall time left after every batch — the live ETA, which refines per dispatch.
///
/// <para><b>The prediction.</b> A pass walks the rows from top to bottom, and the rows' costs differ (a row of sky against a row of
/// near terrain), so the cost per pixel-sample so far is no guide to the rest. The clock keeps every row's last measured GPU cost per
/// pixel-sample, and the samples each row still needs: the GPU time left is their sum, and the wall time left is that times the wall
/// time per GPU second so far (the pauses of a shared GPU, <see cref="GpuShare"/>, and each submission's own overhead). Until every row
/// has a cost — during the first pass — the prediction is null: the rows still to come may cost anything.</para>
///
/// <para>Not thread-safe; the planner's events come from the thread that renders.</para>
/// </summary>
public sealed class TraceClock
{
    /// <summary>The shares of the trace at which <see cref="Checkpoints"/> records what the ETA said.</summary>
    public static IReadOnlyList<double> CheckpointShares { get; } = [0.1, 0.25, 0.5, 0.75, 0.9];

    private readonly BatchPlanner _planner;
    private readonly TimeProvider _time;
    private readonly double[] _rowCostNs;
    private readonly int[] _rowSamples;
    private readonly List<PassTiming> _passes = [];
    private readonly List<EtaCheckpoint> _checkpoints = [];
    private int _rowsCosted;
    private long _start;
    private bool _started;
    private double _lastWall;
    private double _gpu;
    private (int FirstSample, int SampleCount, long PixelSamples, double Gpu, double WallStart, int Dispatches)? _pass;

    /// <summary>A clock on <paramref name="planner"/>'s batches, reading the wall time from <paramref name="time"/> (the system's by default).</summary>
    public TraceClock(BatchPlanner planner, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(planner);
        _planner = planner;
        _time = time ?? TimeProvider.System;
        _rowCostNs = new double[planner.Height];
        _rowSamples = new int[planner.Height];
        planner.Issued += OnIssued;
        planner.Completed += OnCompleted;
    }

    /// <summary>Raised after every batch with the trace's state.</summary>
    public event Action<TraceProgress>? Progressed;

    /// <summary>The passes finished so far, in order.</summary>
    public IReadOnlyList<PassTiming> Passes => _passes;

    /// <summary>What the ETA said at each of <see cref="CheckpointShares"/> reached so far; their actual remainders once the trace ended.</summary>
    public IReadOnlyList<EtaCheckpoint> Checkpoints => _checkpoints;

    /// <summary>Wall seconds from the first dispatch issued to the last batch completed.</summary>
    public double WallSeconds => _lastWall;

    /// <summary>GPU seconds of the batches completed.</summary>
    public double GpuSeconds => _gpu;

    /// <summary>
    /// The wall seconds the trace still needs, from every row's last measured cost and the wall time per GPU second so far; 0 once it
    /// is done, null until every row has a cost.
    /// </summary>
    public double? RemainingSeconds()
    {
        if (_planner.IsComplete)
            return 0.0;
        if (_rowsCosted < _rowCostNs.Length || _gpu <= 0.0)
            return null;
        double gpuNs = 0.0;
        for (int row = 0; row < _rowCostNs.Length; row++)
            gpuNs += (double)(_planner.SamplesPerPixel - _rowSamples[row]) * _rowCostNs[row];
        return gpuNs * _planner.Width * 1e-9 * (_lastWall / _gpu);
    }

    private double Now() => _time.GetElapsedTime(_start, _time.GetTimestamp()).TotalSeconds;

    private void OnIssued(SampleBatch batch)
    {
        if (_started)
            return;
        _start = _time.GetTimestamp();
        _started = true;
    }

    private void OnCompleted(SampleBatch batch, TimeSpan gpuTime)
    {
        double wall = Now();
        double gpu = gpuTime.TotalSeconds;
        long pixelSamples = batch.PixelSamples(_planner.Width);
        _gpu += gpu;
        double cost = gpu * 1e9 / pixelSamples;
        for (int row = batch.RowStart; row < batch.RowStart + batch.RowCount; row++)
        {
            if (_rowSamples[row] == 0)
                _rowsCosted++;
            _rowSamples[row] += batch.SampleCount;
            _rowCostNs[row] = cost;
        }

        var pass = _pass ?? (batch.FirstSample, batch.SampleCount, 0L, 0.0, _lastWall, 0);
        pass = (pass.FirstSample, pass.SampleCount, pass.PixelSamples + pixelSamples, pass.Gpu + gpu, pass.WallStart, pass.Dispatches + 1);
        _lastWall = wall;
        if (batch.RowStart + batch.RowCount >= _planner.Height)
        {
            _passes.Add(new PassTiming(pass.FirstSample, pass.SampleCount, pass.PixelSamples, pass.Gpu, wall - pass.WallStart, pass.Dispatches));
            _pass = null;
        }
        else
        {
            _pass = pass;
        }

        double progress = _planner.Progress;
        double? remaining = RemainingSeconds();
        while (_checkpoints.Count < CheckpointShares.Count && progress >= CheckpointShares[_checkpoints.Count])
            _checkpoints.Add(new EtaCheckpoint(CheckpointShares[_checkpoints.Count], wall, remaining));
        if (_planner.IsComplete)
        {
            for (int i = 0; i < _checkpoints.Count; i++)
                _checkpoints[i] = _checkpoints[i] with { ActualRemainingSeconds = wall - _checkpoints[i].ElapsedSeconds };
        }
        Progressed?.Invoke(new TraceProgress(progress, wall, remaining, _passes.Count));
    }
}
