namespace Curator.Core.Projections;

/// <summary>A Read Thoughts fragment copied into the notebook.</summary>
/// <param name="Date">When.</param>
/// <param name="Fragment">What surfaced.</param>
public sealed record NotedThoughtView(
    string Date,
    string Fragment);
