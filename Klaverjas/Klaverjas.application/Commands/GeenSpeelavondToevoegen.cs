namespace Klaverjas.application.Commands;

/// <summary>Adds an evening without play, such as Easter Monday (R13).</summary>
public sealed record GeenSpeelavondToevoegen : ICommand
{
    /// <summary>The date.</summary>
    public DateOnly Datum { get; init; }

    /// <summary>What the standings show in that week's column.</summary>
    public string Reden { get; init; } = string.Empty;
}
