namespace Curator.Core.Projections;

/// <summary>A book in the returns tray.</summary>
/// <param name="LoanId">The loan.</param>
/// <param name="BookId">The book.</param>
/// <param name="Title">Its title.</param>
/// <param name="PatronName">Who returned it.</param>
/// <param name="CanNoteReturn">Whether its return can be written into the notebook.</param>
/// <param name="Noted">Whether it already was.</param>
/// <param name="RemovedPageIds">Pages torn out of it on its return.</param>
public sealed record ReturnedBookView(
    string LoanId,
    string BookId,
    string Title,
    string PatronName,
    bool CanNoteReturn,
    bool Noted,
    IReadOnlyList<string> RemovedPageIds);
