namespace Kluskat.application.Commands;

/// <summary>"Gedaan op …" from the beheer page: for a klus done without the app.</summary>
public sealed record KlusGedaanOp : ICommand
{
    /// <summary>The klus.</summary>
    public KlusId KlusId { get; init; }

    /// <summary>The local date it was done.</summary>
    public DateOnly Datum { get; init; }
}
