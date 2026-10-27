namespace Vista.Rendering;

/// <summary>
/// Tuning for <see cref="BatchPlanner"/>. The defaults were measured on the RTX 3070 with integrator v0 and
/// kept (vista-plan §10, "M0 batch defaults, measured"): a 250 ms dispatch is far below Windows' ~2 s TDR (the
/// longest of a 4K render was 246 ms), and the initial cost of 2 µs per pixel-sample — about 1,400× the M0
/// terrain's measured worst — keeps the very first dispatch small on any scene the later milestones bring,
/// at the price of a ramp of about ten small dispatches.
/// </summary>
public sealed record BatchPlannerOptions
{
    /// <summary>Hard ceiling for <see cref="DispatchBudgetMs"/>: half the default TDR timeout.</summary>
    public const double MaxDispatchBudgetMs = 1000.0;

    /// <summary>Target GPU time per dispatch in milliseconds; at most <see cref="MaxDispatchBudgetMs"/>.</summary>
    public double DispatchBudgetMs { get; init; } = 250.0;

    /// <summary>Cost in nanoseconds per pixel-sample assumed before the first measurement.</summary>
    public double InitialCostNs { get; init; } = 2000.0;

    /// <summary>Upper bound on samples per pixel in one dispatch (keeps progress and ETA updates flowing).</summary>
    public int MaxSamplesPerDispatch { get; init; } = 64;

    /// <summary>
    /// A probe's stop (vista-plan §10 M1.15): the planner issues nothing more once a pass ends with at least this much GPU time measured,
    /// at least two passes done (the first carries the ramp), and the pass that ended as large as the passes will settle at (the dispatch
    /// budget over the cost that pass measured, as the estimate approaches it): a full-frame pass grows as the cost estimate falls (one
    /// sample, one, two, three, four...), slowly near the end, and a sample costs more in a larger one. So a render traces the first
    /// passes of a final exactly as the final will, up to the pass size the rest of it keeps, and no more; at ten times this GPU time it
    /// stops anyway. Infinite (the default) traces every sample.
    /// </summary>
    public double StopAfterGpuSeconds { get; init; } = double.PositiveInfinity;

    /// <summary>Problems with these options (empty when valid).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (double.IsNaN(DispatchBudgetMs) || DispatchBudgetMs <= 0.0 || DispatchBudgetMs > MaxDispatchBudgetMs)
            errors.Add($"Dispatch budget must be in (0, {MaxDispatchBudgetMs}] ms.");
        if (!double.IsFinite(InitialCostNs) || InitialCostNs <= 0.0)
            errors.Add("Initial cost must be positive.");
        if (MaxSamplesPerDispatch < 1)
            errors.Add("At least one sample per dispatch.");
        if (double.IsNaN(StopAfterGpuSeconds) || StopAfterGpuSeconds < 0.0)
            errors.Add("A probe stops after a non-negative GPU time.");
        return errors;
    }
}

/// <summary>
/// One GPU dispatch: rows <c>[RowStart, RowStart + RowCount)</c> of the full image width, sample indices
/// <c>[FirstSample, FirstSample + SampleCount)</c> per pixel.
/// </summary>
/// <param name="Index">Issue order, from 0.</param>
/// <param name="RowStart">First image row.</param>
/// <param name="RowCount">Number of rows.</param>
/// <param name="FirstSample">First per-pixel sample index.</param>
/// <param name="SampleCount">Samples per pixel in this dispatch.</param>
public readonly record struct SampleBatch(int Index, int RowStart, int RowCount, int FirstSample, int SampleCount)
{
    /// <summary>Pixel-samples this batch traces for an image <paramref name="width"/> pixels wide.</summary>
    public long PixelSamples(int width) => (long)width * RowCount * SampleCount;
}

/// <summary>
/// Splits a render into TDR-safe GPU dispatches (vista-plan §5.1 / §7.3) and keeps a live cost estimate
/// for the ETA. The image is covered in <i>passes</i>: a pass fixes a sample range and walks the rows in
/// bands sized to the dispatch budget; when one sample over the whole frame fits the budget, a pass is a
/// single full-frame dispatch of several samples. Every <c>(pixel, sample index)</c> is issued exactly once.
///
/// <para><b>Contract with the integrator:</b> a pixel's sample stream must be keyed by
/// <c>(pixel, sample index)</c> alone — never by dispatch — so the partition this planner picks (and with it
/// the measured timings) cannot change the image. That is a GPU check: render the same frame under two
/// different budgets and compare bit-for-bit on the 3070.</para>
///
/// <para>Cost model: <c>ns per pixel-sample</c>. A slower measurement replaces the estimate at once (a
/// heavy region must not trip the TDR on the next dispatch); a faster one only pulls it halfway down.</para>
///
/// <para>A probe (<see cref="BatchPlannerOptions.StopAfterGpuSeconds"/>, vista-plan §10 M1.15) stops at a pass's end: the image then
/// holds <see cref="SamplesCompleted"/> samples a pixel, not <see cref="SamplesPerPixel"/>, and a renderer's means over the samples asked
/// for are that share too dark.</para>
/// </summary>
public sealed class BatchPlanner
{
    private readonly BatchPlannerOptions _options;
    private readonly double _budgetNs;
    private int _rowCursor;
    private int _passFirstSample;
    private int _passSampleCount;
    private readonly Dictionary<int, SampleBatch> _outstanding = [];
    private int _issued;
    private int _passesDone;
    private bool _stopped;
    private double _passGpuNs;
    private long _passPixelSamples;

    /// <summary>Creates a planner for a <paramref name="width"/>×<paramref name="height"/> image at <paramref name="samplesPerPixel"/>.</summary>
    public BatchPlanner(int width, int height, int samplesPerPixel, BatchPlannerOptions? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samplesPerPixel);
        _options = options ?? new BatchPlannerOptions();
        IReadOnlyList<string> errors = _options.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(options));
        Width = width;
        Height = height;
        SamplesPerPixel = samplesPerPixel;
        _budgetNs = _options.DispatchBudgetMs * 1e6;
        CostNsPerPixelSample = _options.InitialCostNs;
    }

    /// <summary>Image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Image height in pixels.</summary>
    public int Height { get; }

    /// <summary>Samples every pixel receives.</summary>
    public int SamplesPerPixel { get; }

    /// <summary>Current cost estimate in nanoseconds per pixel-sample.</summary>
    public double CostNsPerPixelSample { get; private set; }

    /// <summary>True while there is work left to issue (and a probe has not stopped).</summary>
    public bool HasNext => _passFirstSample < SamplesPerPixel && !_stopped;

    /// <summary>True when a probe stopped before its last sample (<see cref="BatchPlannerOptions.StopAfterGpuSeconds"/>).</summary>
    public bool Stopped => _stopped;

    /// <summary>The samples every pixel has once the issued passes are complete: all of them, unless a probe stopped.</summary>
    public int SamplesCompleted => _passFirstSample;

    /// <summary>True once every batch has been issued and reported complete.</summary>
    public bool IsComplete => !HasNext && _outstanding.Count == 0;

    /// <summary>Batches issued so far.</summary>
    public int Dispatches => _issued;

    /// <summary>Pixel-samples reported complete so far.</summary>
    public long PixelSamplesCompleted { get; private set; }

    /// <summary>Total pixel-samples of the render.</summary>
    public long PixelSamplesTotal => (long)Width * Height * SamplesPerPixel;

    /// <summary>Completed fraction in [0, 1].</summary>
    public double Progress => (double)PixelSamplesCompleted / PixelSamplesTotal;

    /// <summary>GPU time measured so far.</summary>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>Remaining time at the current cost estimate.</summary>
    public TimeSpan EstimatedRemaining =>
        TimeSpan.FromMilliseconds((PixelSamplesTotal - PixelSamplesCompleted) * CostNsPerPixelSample / 1e6);

    /// <summary>Raised by <see cref="Next"/> with the batch it planned (a <see cref="TraceClock"/> starts its wall clock on the first).</summary>
    public event Action<SampleBatch>? Issued;

    /// <summary>Raised by <see cref="Complete"/> with the batch and its GPU time, once the planner has taken it in (vista-plan §10 M1.15's live ETA).</summary>
    public event Action<SampleBatch, TimeSpan>? Completed;

    /// <summary>Plans the next dispatch. Throws when <see cref="HasNext"/> is false.</summary>
    public SampleBatch Next()
    {
        if (!HasNext)
            throw new InvalidOperationException("Every batch has already been issued.");

        double budgetPixelSamples = _budgetNs / CostNsPerPixelSample;
        int rows;
        if (_rowCursor == 0)
        {
            int remaining = SamplesPerPixel - _passFirstSample;
            double frame = (double)Width * Height;
            if (budgetPixelSamples >= frame)
            {
                _passSampleCount = (int)Math.Clamp(Math.Floor(budgetPixelSamples / frame), 1.0, Math.Min(remaining, _options.MaxSamplesPerDispatch));
                rows = Height;
            }
            else
            {
                _passSampleCount = 1;
                rows = RowsFor(budgetPixelSamples, 1);
            }
        }
        else
        {
            rows = RowsFor(budgetPixelSamples, _passSampleCount);
        }

        var batch = new SampleBatch(_issued++, _rowCursor, rows, _passFirstSample, _passSampleCount);
        _outstanding.Add(batch.Index, batch);
        _rowCursor += rows;
        if (_rowCursor >= Height)
        {
            _rowCursor = 0;
            _passFirstSample += _passSampleCount;
        }
        Issued?.Invoke(batch);
        return batch;
    }

    /// <summary>Reports that <paramref name="batch"/> finished after <paramref name="gpuTime"/> of GPU time.</summary>
    public void Complete(SampleBatch batch, TimeSpan gpuTime)
    {
        if (!_outstanding.TryGetValue(batch.Index, out SampleBatch issued) || issued != batch)
            throw new ArgumentException("That batch was not issued by this planner, or was already completed.", nameof(batch));
        _outstanding.Remove(batch.Index);
        long pixelSamples = batch.PixelSamples(Width);
        PixelSamplesCompleted += pixelSamples;
        Elapsed += gpuTime;

        double observed = gpuTime.Ticks * 100.0 / pixelSamples; // 1 tick = 100 ns
        if (double.IsFinite(observed) && observed > 0.0)
            CostNsPerPixelSample = observed > CostNsPerPixelSample ? observed : 0.5 * (CostNsPerPixelSample + observed);
        _passGpuNs += gpuTime.Ticks * 100.0;
        _passPixelSamples += pixelSamples;
        if (batch.RowStart + batch.RowCount >= Height)
        {
            double passCostNs = _passGpuNs / _passPixelSamples;
            (_passGpuNs, _passPixelSamples) = (0.0, 0L);
            double seconds = Elapsed.TotalSeconds;
            if (++_passesDone >= 2 && seconds >= _options.StopAfterGpuSeconds && _outstanding.Count == 0 && _rowCursor == 0
                && (_passSampleCount >= SettledPassSamples(passCostNs) || seconds >= 10.0 * _options.StopAfterGpuSeconds))
                _stopped = true;
        }
        Completed?.Invoke(batch, gpuTime);
    }

    /// <summary>
    /// The samples a pixel the passes settle at once the cost estimate reaches <paramref name="costNs"/>, as <see cref="Next"/> plans a pass:
    /// several samples while one over the frame fits the budget, else one in row bands.
    /// </summary>
    private int SettledPassSamples(double costNs)
    {
        double budgetPixelSamples = _budgetNs / costNs;
        double frame = (double)Width * Height;
        int remaining = Math.Max(1, SamplesPerPixel - _passFirstSample);
        return budgetPixelSamples >= frame
            ? (int)Math.Clamp(Math.Floor(budgetPixelSamples / frame), 1.0, Math.Min(remaining, _options.MaxSamplesPerDispatch))
            : 1;
    }

    private int RowsFor(double budgetPixelSamples, int samples) =>
        (int)Math.Clamp(Math.Floor(budgetPixelSamples / ((double)Width * samples)), 1.0, Height - _rowCursor);
}
