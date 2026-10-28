namespace Vista.Rendering;

/// <summary>
/// What a render tells whoever waits on it while it traces (vista-plan §10 M1.16, the window's progressive preview), carried by its
/// <see cref="BatchPlanner"/>: the planner refuses its next batch once <see cref="Cancellation"/> is cancelled, so the render stops
/// between dispatches; and after a pass over the whole frame the renderer hands <see cref="Pass"/> the image so far, at most every
/// <see cref="IntervalSeconds"/> of the trace and never after the last pass (that image is the render's own). A pass gives every pixel
/// the same samples, so the image so far is the render at fewer samples; the feed reads it and never changes it.
/// </summary>
public sealed class TraceFeed
{
    /// <summary>Stops the render before its next batch; the render then throws <see cref="OperationCanceledException"/>.</summary>
    public CancellationToken Cancellation { get; init; }

    /// <summary>Told the image so far after a pass, from the render's thread; null for none (the render reads nothing back early).</summary>
    public Action<PassImage>? Pass { get; init; }

    /// <summary>The least wall time between two images, from the trace's start, s: a quick render shows only its own.</summary>
    public double IntervalSeconds { get; init; } = 0.25;
}

/// <summary>The image of a render after a pass over the whole frame (<see cref="TraceFeed.Pass"/>).</summary>
/// <param name="Width">The rendered window's width.</param>
/// <param name="Height">The rendered window's height.</param>
/// <param name="Samples">The samples every pixel has so far.</param>
/// <param name="SamplesPerPixel">The samples every pixel will have.</param>
/// <param name="Xyz">CIE XYZ, the mean over <paramref name="Samples"/>, 3 floats a pixel, row-major.</param>
public sealed record PassImage(int Width, int Height, int Samples, int SamplesPerPixel, float[] Xyz);
