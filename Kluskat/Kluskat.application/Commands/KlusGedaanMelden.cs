namespace Kluskat.application.Commands;

/// <summary>"Gedaan!" on the klus screen.</summary>
public sealed record KlusGedaanMelden : ICommand
{
    /// <summary>The klus that was done.</summary>
    public KlusId KlusId { get; init; }
}
