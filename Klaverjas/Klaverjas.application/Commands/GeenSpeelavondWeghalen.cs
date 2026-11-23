namespace Klaverjas.application.Commands;

/// <summary>Takes an evening without play off the list again.</summary>
public sealed record GeenSpeelavondWeghalen : ICommand
{
    /// <summary>The date.</summary>
    public DateOnly Datum { get; init; }
}
