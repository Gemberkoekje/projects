using System.Globalization;

namespace Vista.Rendering;

/// <summary>
/// How much of the GPU a Vista run takes (the dev box is the user's own PC, 2026-10-26: "my PC is barely responding while you run
/// these renders"). Every GPU submission is followed by a pause, so the GPU works at most <see cref="Share"/> of the wall time, and
/// the submissions are kept short (<see cref="MaxDispatchMs"/>), so the desktop and other programs get the GPU between them within a
/// frame or two. Set by the environment variable <see cref="Variable"/> (a share from 0.05 to 1; <c>1</c> runs at full speed, as
/// before 2026-10-26); <see cref="DefaultShare"/> without it. Only wall time moves: the images (a render's split never changes them)
/// and the GPU seconds the sidecars and the cost ledger record (timestamps around the work alone) are the same.
/// </summary>
public static class GpuShare
{
    /// <summary>The environment variable that sets the share.</summary>
    public const string Variable = "VISTA_GPU_SHARE";

    /// <summary>The share without the variable: about half the GPU, the rest for the desktop.</summary>
    public const double DefaultShare = 0.5;

    /// <summary>The longest a submission runs when the share is below 1, ms: about two frames at 60 Hz.</summary>
    public const double CappedDispatchMs = 30.0;

    /// <summary>The share of the wall time the GPU may work, 0.05 to 1.</summary>
    public static double Share { get; } = Read();

    /// <summary>True when the GPU is shared (<see cref="Share"/> below 1).</summary>
    public static bool Capped => Share < 1.0;

    /// <summary>The longest a submission may run, ms: <see cref="CappedDispatchMs"/> when capped, unbounded otherwise.</summary>
    public static double MaxDispatchMs => Capped ? CappedDispatchMs : double.PositiveInfinity;

    /// <summary>A dispatch budget of <paramref name="ms"/>, held to <see cref="MaxDispatchMs"/>.</summary>
    public static double Budget(double ms) => Math.Min(ms, MaxDispatchMs);

    /// <summary>Waits after a submission that kept the GPU busy <paramref name="gpuSeconds"/>, so it works at most <see cref="Share"/> of the time.</summary>
    public static void Pause(double gpuSeconds)
    {
        if (!Capped || !(gpuSeconds > 0.0))
            return;
        double seconds = Math.Min(gpuSeconds * (1.0 / Share - 1.0), 2.0);
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
    }

    private static double Read()
    {
        string? text = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(text))
            return DefaultShare;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double share) && share > 0.0
            ? Math.Clamp(share, 0.05, 1.0)
            : DefaultShare;
    }
}
