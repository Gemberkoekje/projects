using Curator.Core.Content;

namespace Curator.Core.Projections;

/// <summary>A resolved outcome, for the debug overlay.</summary>
/// <param name="ResolutionId">The resolution.</param>
/// <param name="PatronId">The patron.</param>
/// <param name="BookId">The book, or empty.</param>
/// <param name="Category">Its category.</param>
/// <param name="Cause">Its cause.</param>
/// <param name="Channel">How it surfaces.</param>
/// <param name="SurfaceDay">When.</param>
/// <param name="Surfaced">Whether it has.</param>
/// <param name="Key">Which outcome.</param>
public sealed record PendingOutcomeView(
    string ResolutionId,
    string PatronId,
    string BookId,
    OutcomeCategory Category,
    Cause Cause,
    OutcomeChannel Channel,
    int SurfaceDay,
    bool Surfaced,
    string Key);
