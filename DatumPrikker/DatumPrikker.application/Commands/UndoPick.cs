namespace DatumPrikker.application.Commands;

/// <summary>Undo a poll's pick; the poll stays closed (D22).</summary>
public sealed record UndoPick
{
    /// <summary>The code from the poll's link.</summary>
    public string ShareToken { get; init; } = string.Empty;

    /// <summary>Who is undoing it; only the owner may.</summary>
    public Account By { get; init; } = Account.Anonymous;
}
