namespace Curator.Core.Content;

/// <summary>Loan numbers.</summary>
public sealed record LoansBalance
{
    public required int DefaultDays { get; init; }
}
