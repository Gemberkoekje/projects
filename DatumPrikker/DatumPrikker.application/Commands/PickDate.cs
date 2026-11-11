namespace DatumPrikker.application.Commands;

/// <summary>Pick "this is the date" for a poll, or change the pick (D22).</summary>
public sealed record PickDate
{
    /// <summary>The code from the poll's link.</summary>
    public string ShareToken { get; init; } = string.Empty;

    /// <summary>The date option to pick; it must belong to this poll.</summary>
    public Guid OptionId { get; init; }

    /// <summary>Who is picking; only the owner may.</summary>
    public Account By { get; init; } = Account.Anonymous;
}
