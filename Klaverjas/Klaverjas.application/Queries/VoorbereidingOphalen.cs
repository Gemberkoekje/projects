namespace Klaverjas.application.Queries;

/// <summary>The "Kaartjes en tafelindeling" screen of one evening.</summary>
public sealed record VoorbereidingOphalen : IQuery<VoorbereidingFormulier>
{
    /// <summary>The evening.</summary>
    public DateOnly Datum { get; init; }
}
