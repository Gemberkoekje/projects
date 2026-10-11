namespace Curator.Core.Projections;

/// <summary>A story patron's trust at the week's end.</summary>
/// <param name="PatronId">The patron.</param>
/// <param name="Name">Their name.</param>
/// <param name="Met">Whether they ever came.</param>
/// <param name="Trust">Their trust, 0-3.</param>
public sealed record PatronTrustView(
    string PatronId,
    string Name,
    bool Met,
    int Trust);
