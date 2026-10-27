using Vista.Scene;

namespace Vista.Composition;

/// <summary>
/// How the auto camera chose the camera (sidecar schema 24, vista-plan §10 M1.14): the plant, the search's settings, weights and
/// limits, how many candidates it scored and how many kept the limits, the pick and the finalists with their scores, the best
/// candidates by their buffers, and what each stage cost.
/// </summary>
public sealed record AutoCameraInfo
{
    /// <summary>The shot-first plant the candidates stood around.</summary>
    public required PlantedShot Planted { get; init; }

    /// <summary>How the search ran.</summary>
    public required AutoCameraSettings Settings { get; init; }

    /// <summary>How the scores were weighed.</summary>
    public required CompositionWeights Weights { get; init; }

    /// <summary>The limits a pick keeps (the near field's until M5 among them).</summary>
    public required CompositionLimits Limits { get; init; }

    /// <summary>The candidates scored by their buffers.</summary>
    public required int Candidates { get; init; }

    /// <summary>Of those, the ones whose buffers kept the limits.</summary>
    public required int Feasible { get; init; }

    /// <summary>The places on shoulders above a pool among the candidates' places.</summary>
    public required int Shoulders { get; init; }

    /// <summary>The pick: the best finalist that keeps every limit (the best candidate by its buffer when none was rendered).</summary>
    public required AutoCameraPick Pick { get; init; }

    /// <summary>The finalists as rendered and scored, best first (empty when none was rendered).</summary>
    public required IReadOnlyList<AutoCameraPick> Finalists { get; init; }

    /// <summary>The best candidates by their buffers alone, best first.</summary>
    public required IReadOnlyList<AutoCameraPick> Best { get; init; }

    /// <summary>What each stage cost, s: <c>plant</c>, <c>buffers</c>, <c>finalists</c> (their renders), <c>meshes</c> (their 4K meshes' triangles).</summary>
    public required SortedDictionary<string, double> Seconds { get; init; }

    /// <summary>Why the finalists were not rendered, when they were not (a dry run, or none asked for).</summary>
    public string? Note { get; init; }
}

/// <summary>
/// The auto camera's detail budget over a preview (sidecar schema 25, vista-plan §10, M1.14's follow-up; <c>--debug detail</c>): the
/// share of the frame lit bare ground covers that the final frame magnifies past the terrain's detail, whether that breaks the limit,
/// and, to choose the limit by eye, the shares at other thresholds and by the ground's class.
/// </summary>
public sealed record DetailInfo
{
    /// <summary>The thresholds the shares are read at, final-frame pixels per detail cell.</summary>
    public static IReadOnlyList<double> Thresholds { get; } = [2.0, 3.0, 4.0, 6.0, 8.0, 12.0, 16.0];

    /// <summary>The final frame's width the budget is measured at, pixels.</summary>
    public required int FinalFrameWidth { get; init; }

    /// <summary>The final frame's height, pixels.</summary>
    public required int FinalFrameHeight { get; init; }

    /// <summary>The limit's threshold (<see cref="CompositionLimits.DetailCellPixels"/>).</summary>
    public required double CellPixels { get; init; }

    /// <summary>The limit's share (<see cref="CompositionLimits.DetailMaxShare"/>).</summary>
    public required double MaxShare { get; init; }

    /// <summary>The share of the frame that counts against the budget.</summary>
    public required double Share { get; init; }

    /// <summary>True when <see cref="Share"/> passes <see cref="MaxShare"/>: the auto camera would rank this frame down.</summary>
    public required bool Breaks { get; init; }

    /// <summary>The share of counted ground at each of <see cref="Thresholds"/> (keyed by it, two digits).</summary>
    public required SortedDictionary<string, double> ShareByCellPixels { get; init; }

    /// <summary>The share of each class's lit ground past <see cref="CellPixels"/>, counted or not.</summary>
    public required SortedDictionary<string, double> ShareByClass { get; init; }
}

/// <summary>One scored framing, as the sidecar records it.</summary>
/// <param name="Index">Its index among the candidates.</param>
/// <param name="Origin">How its place was found (<see cref="CameraCandidate.Origin"/>).</param>
/// <param name="Camera">The camera.</param>
/// <param name="Scores">The scores.</param>
/// <param name="Total">The weighted total.</param>
/// <param name="Breaks">The limit it breaks, or null.</param>
public sealed record AutoCameraPick(int Index, string Origin, CameraSpec Camera, CompositionScores Scores, double Total, string? Breaks)
{
    /// <summary>How high above the ground at its place the camera stands, m (<see cref="CameraCandidate.AboveGroundMetres"/>).</summary>
    public double AboveGroundMetres { get; init; }

    /// <summary>The pick of <paramref name="scored"/>.</summary>
    public static AutoCameraPick Of(ScoredCandidate scored)
    {
        ArgumentNullException.ThrowIfNull(scored);
        return new AutoCameraPick(scored.Candidate.Index, scored.Candidate.Origin, scored.Candidate.Camera, scored.Scores, scored.Total, scored.Breaks)
        {
            AboveGroundMetres = scored.Candidate.AboveGroundMetres,
        };
    }
}
