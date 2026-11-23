namespace Klaverjas.application.Queries;

/// <summary>The score cards of one evening, ready to print (D15).</summary>
public sealed record KaartjesOphalen : IQuery<Kaartjes>
{
    /// <summary>The evening.</summary>
    public DateOnly Datum { get; init; }
}
