namespace Kluskat.application.ReadModels;

/// <summary>What the win screen shows after a "Gedaan".</summary>
public sealed record Overwinning
{
    /// <summary>The klus that was just done.</summary>
    public KlusOverzicht Klus { get; init; } = KlusOverzicht.Geen;

    /// <summary>How many klussen were done this week (Monday to Sunday), this one included.</summary>
    public int AantalDezeWeek { get; init; }
}
