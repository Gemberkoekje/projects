namespace Kluskat.application.Commands;

/// <summary>Takes a klus off the list, from the beheer page.</summary>
public sealed record KlusSchrappen : ICommand
{
    /// <summary>The klus.</summary>
    public KlusId KlusId { get; init; }
}
