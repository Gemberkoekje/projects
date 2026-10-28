namespace Kluskat.application.Commands;

/// <summary>"Geen zin" or "Geen tijd" on the klus screen.</summary>
public sealed record KlusOverslaan : ICommand
{
    /// <summary>The klus that was on screen.</summary>
    public KlusId KlusId { get; init; }

    /// <summary>Which button.</summary>
    public OverslaanReden Reden { get; init; }
}
