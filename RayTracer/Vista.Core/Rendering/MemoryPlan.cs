using System.Globalization;

namespace Vista.Rendering;

/// <summary>The stages of a render in the order its video memory is taken (vista-plan §7.1, §10 M1.15).</summary>
public enum MemoryStage
{
    /// <summary>Not a stage.</summary>
    None,

    /// <summary>The terrain mesh, its normal maps and its materials uploaded.</summary>
    Mesh,

    /// <summary>The acceleration structures built: the build's scratch lives only here.</summary>
    Acceleration,

    /// <summary>The air's tables and the valleys' pools built: the scattering orders' scratch lives only here.</summary>
    Air,

    /// <summary>The image buffers and the sky's distribution in place, and the trace.</summary>
    Trace,
}

/// <summary>
/// One buffer of a render's plan: <paramref name="Bytes"/> of the GPU's own memory, taken at <paramref name="From"/> and held through
/// <paramref name="Until"/>.
/// </summary>
/// <param name="Name">What it holds (camelCase, as the sidecar records it).</param>
/// <param name="Bytes">Its size, rounded up to the allocation granularity (<see cref="MemoryPlan.Page"/>).</param>
/// <param name="From">The stage it is allocated in.</param>
/// <param name="Until">The last stage it is held through.</param>
public sealed record MemoryItem(string Name, long Bytes, MemoryStage From, MemoryStage Until = MemoryStage.Trace)
{
    /// <summary>True when the buffer is held during <paramref name="stage"/>.</summary>
    public bool HeldIn(MemoryStage stage) => stage >= From && stage <= Until;
}

/// <summary>
/// A render's video memory, planned before anything of it is allocated (vista-plan §7.1, §7.2, §10 M1.15): what the process holds
/// before its first render (<paramref name="BaseBytes"/>: the device, its pipelines and fixed tables, as measured when the renderer
/// was made), and every buffer the render will add, by the stages it is held through. The peak is the fullest stage; it is checked
/// against the budget Windows gives the process (DXGI's <c>Budget</c>), which already leaves out what other programs hold.
///
/// <para>The plan is for this render in a fresh process (a final run): buffers a session kept from an earlier render are planned again.
/// A render over the budget does not fit (the renderer refuses it before uploading anything); one over <see cref="TightShare"/> of it
/// fits only while other programs leave the GPU's memory alone, so it is warned about.</para>
/// </summary>
/// <param name="BaseBytes">The process's video memory before its first render.</param>
/// <param name="BudgetBytes">DXGI's budget for the process now; 0 when unknown (no GPU).</param>
/// <param name="Items">The render's buffers.</param>
public sealed record MemoryPlan(long BaseBytes, long BudgetBytes, IReadOnlyList<MemoryItem> Items)
{
    /// <summary>The share of the budget above which a render is warned about: the plan's "never crash at 80%" (§7.2).</summary>
    public const double TightShare = 0.8;

    /// <summary>The granularity of a committed buffer on D3D12 (64 KiB): every buffer takes a whole number of these.</summary>
    public const long PageBytes = 65536;

    /// <summary>
    /// The margin the plan states against the peak a render measures, as a share of the plan (vista-plan §10 M1.15: on its test views the
    /// plan met the measured peak to the MiB once its base held the driver's own build memory).
    /// </summary>
    public const double Margin = 0.03;

    /// <summary>The empty plan: nothing known.</summary>
    public static MemoryPlan None { get; } = new(0, 0, []);

    /// <summary><paramref name="bytes"/> rounded up to whole <see cref="PageBytes"/>; 0 stays 0.</summary>
    public static long Page(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        return (bytes + PageBytes - 1) / PageBytes * PageBytes;
    }

    /// <summary>The bytes held during <paramref name="stage"/>, the base included.</summary>
    public long HeldIn(MemoryStage stage) => BaseBytes + Items.Where(i => i.HeldIn(stage)).Sum(i => i.Bytes);

    /// <summary>The fullest stage.</summary>
    public MemoryStage PeakStage => Enum.GetValues<MemoryStage>().Where(s => s != MemoryStage.None).MaxBy(HeldIn);

    /// <summary>The bytes held at the fullest stage, the base included.</summary>
    public long PeakBytes => HeldIn(PeakStage);

    /// <summary>True when the peak is within the budget (or the budget is unknown).</summary>
    public bool Fits => BudgetBytes <= 0 || PeakBytes <= BudgetBytes;

    /// <summary>True when the peak passes <see cref="TightShare"/> of the budget.</summary>
    public bool Tight => BudgetBytes > 0 && PeakBytes > TightShare * BudgetBytes;

    /// <summary>The plan's sum for one item name (0 when absent).</summary>
    public long BytesOf(string name) => Items.Where(i => i.Name == name).Sum(i => i.Bytes);

    /// <summary>A warning when the render does not fit or is tight; null when it fits with room.</summary>
    public string? Warning()
    {
        if (BudgetBytes <= 0 || !Tight)
            return null;
        string numbers = string.Create(CultureInfo.InvariantCulture,
            $"{PeakBytes / 1048576.0:N0} MiB of video memory at its peak ({PeakStage.ToString().ToLowerInvariant()}) against a budget of {BudgetBytes / 1048576.0:N0} MiB");
        return Fits
            ? $"Tight: the render needs {numbers}, over {TightShare:P0}; other programs taking video memory could push it out. Raise --mesh-error or free the GPU."
            : $"Does not fit: the render needs {numbers}. Raise --mesh-error, lower the size, or free the GPU.";
    }
}
