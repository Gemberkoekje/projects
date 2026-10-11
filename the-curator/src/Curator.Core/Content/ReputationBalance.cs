namespace Curator.Core.Content;

/// <summary>Reputation numbers (BUILD_BRIEF §5.6).</summary>
public sealed record ReputationBalance
{
    public required int Start { get; init; }

    public required int GoodPublic { get; init; }

    public required int HarmPublic { get; init; }

    public required int UnexplainedDecline { get; init; }

    public required int WalkedOut { get; init; }

    public required int WarmThreshold { get; init; }

    public required int WaryThreshold { get; init; }

    public required int WarmStrangerTrust { get; init; }
}
