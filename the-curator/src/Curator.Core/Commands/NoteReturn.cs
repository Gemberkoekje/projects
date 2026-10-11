namespace Curator.Core.Commands;

/// <summary>Write a loan that came back today into the notebook, with its return date.</summary>
/// <param name="LoanId">The loan.</param>
public sealed record NoteReturn(string LoanId) : Command;
