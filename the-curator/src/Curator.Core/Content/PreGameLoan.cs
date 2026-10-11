namespace Curator.Core.Content;

/// <summary>A loan on a patron's card from before day 1 (days 0 or earlier).</summary>
public sealed record PreGameLoan
{
    public required string BookId { get; init; }

    public required int BorrowedDay { get; init; }

    public required int ReturnedDay { get; init; }
}
