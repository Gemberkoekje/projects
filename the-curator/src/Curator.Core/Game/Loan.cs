namespace Curator.Core.Game;

/// <summary>An in-game loan: the true history the library card shows.</summary>
public sealed class Loan
{
    internal Loan(string loanId, string bookId, string patronId, string visitId)
    {
        LoanId = loanId;
        BookId = bookId;
        PatronId = patronId;
        VisitId = visitId;
    }

    public string LoanId { get; }

    public string BookId { get; }

    public string PatronId { get; }

    public string VisitId { get; }

    public int BorrowedDay { get; internal set; }

    public int DueDay { get; internal set; }

    /// <summary>False when the book never comes back.</summary>
    public bool Returns { get; internal set; }

    public int Fee { get; internal set; }

    public bool AsAlternative { get; internal set; }

    public bool Returned { get; internal set; }

    public int ReturnedDay { get; internal set; }

    /// <summary>Whether the curator wrote it into the notebook.</summary>
    public bool LedgerNoted { get; internal set; }

    /// <summary>Whether the curator wrote its return into the notebook.</summary>
    public bool ReturnNoted { get; internal set; }
}
