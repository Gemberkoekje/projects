namespace Vista.Cli;

/// <summary>
/// What a <see cref="PreviewSession"/> run tells the window that started it (vista-plan §10 M1.16): it stops at the next stage or the
/// next dispatch once <see cref="Cancellation"/> is cancelled, throwing <see cref="OperationCanceledException"/> and keeping every
/// stage it finished; and while the image traces, <see cref="Frame"/> gets it after each pass over the whole frame, exposed and
/// display-encoded as the PNG will be, so the preview refines on screen. The run's files are the same with a feed and without one.
/// </summary>
public sealed class PreviewFeed
{
    /// <summary>Stops the run at its next stage or dispatch.</summary>
    public CancellationToken Cancellation { get; init; }

    /// <summary>Told the image so far after each pass, from the run's thread; null for none.</summary>
    public Action<PreviewFrame>? Frame { get; init; }

    /// <summary>The least wall time between two frames, s (<see cref="Rendering.TraceFeed.IntervalSeconds"/>).</summary>
    public double IntervalSeconds { get; init; } = 0.25;
}

/// <summary>The image so far of a run's render, as the window shows it (<see cref="PreviewFeed.Frame"/>).</summary>
/// <param name="Width">The image's width.</param>
/// <param name="Height">The image's height.</param>
/// <param name="Samples">The samples every pixel has so far.</param>
/// <param name="SamplesPerPixel">The samples every pixel will have.</param>
/// <param name="Rgb">sRGB, 8 bits a channel, 3 bytes a pixel, row-major: exposed and display-encoded as the PNG, without its dither.</param>
public sealed record PreviewFrame(int Width, int Height, int Samples, int SamplesPerPixel, byte[] Rgb);
