namespace Klaverjas.application.Queries;

/// <summary>The entry screen of one evening, new or to change.</summary>
public sealed record UitslagOphalen : IQuery<UitslagFormulier>
{
    /// <summary>The evening.</summary>
    public DateOnly Datum { get; init; }
}
