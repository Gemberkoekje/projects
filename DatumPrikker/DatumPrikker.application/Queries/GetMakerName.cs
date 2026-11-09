namespace DatumPrikker.application.Queries;

/// <summary>The name to put in the "Jouw naam" field of a new poll.</summary>
public sealed record GetMakerName
{
    /// <summary>The signed-in maker.</summary>
    public Account Account { get; init; } = Account.Anonymous;
}
