namespace Curator.Core.Content;

/// <summary>How a visit ended.</summary>
public enum DecisionKind
{
    None = 0,
    Lent,
    Alternative,
    Declined,
    WalkedOut,
}
