namespace Curator.Core.Game;

/// <summary>
/// The curator's free text: notebook notes per patron and the ledger's note column per loan.
/// Not game state — the game never reads it — so edits aren't commands; the save stores it
/// beside the log (BUILD_BRIEF §4.2).
/// </summary>
public sealed class Notes
{
    private readonly Dictionary<string, string> free = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> ledger = new(StringComparer.Ordinal);

    /// <summary>Free notes by patron id.</summary>
    public IReadOnlyDictionary<string, string> Free => free;

    /// <summary>Ledger notes by loan id.</summary>
    public IReadOnlyDictionary<string, string> Ledger => ledger;

    /// <summary>A patron's free notes.</summary>
    /// <param name="patronId">The patron.</param>
    /// <returns>The text, or empty.</returns>
    public string FreeNotes(string patronId) => free.TryGetValue(patronId, out var text) ? text : "";

    /// <summary>Replaces a patron's free notes.</summary>
    /// <param name="patronId">The patron.</param>
    /// <param name="text">The text.</param>
    public void SetFreeNotes(string patronId, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        free[patronId] = text;
    }

    /// <summary>A watched loan's note.</summary>
    /// <param name="loanId">The loan.</param>
    /// <returns>The note, or empty.</returns>
    public string LedgerNote(string loanId) => ledger.TryGetValue(loanId, out var text) ? text : "";

    /// <summary>Replaces a watched loan's note.</summary>
    /// <param name="loanId">The loan.</param>
    /// <param name="text">The note.</param>
    public void SetLedgerNote(string loanId, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ledger[loanId] = text;
    }
}
