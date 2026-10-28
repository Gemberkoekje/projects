namespace Kluskat.application.Commands;

/// <summary>An edit from the beheer page.</summary>
public sealed record KlusAanpassen : ICommand
{
    /// <summary>The klus.</summary>
    public KlusId KlusId { get; init; }

    /// <summary>The new values.</summary>
    public Klus.Data Data { get; init; } = new Klus.Data();
}
