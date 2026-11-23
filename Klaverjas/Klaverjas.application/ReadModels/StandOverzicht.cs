namespace Klaverjas.application.ReadModels;

/// <summary>The standings plus the settings the page needs to draw them (colours, evenings without play).</summary>
public sealed record StandOverzicht
{
    /// <summary>The computed standings.</summary>
    public Stand Stand { get; init; } = Stand.Leeg(0);

    /// <summary>The settings.</summary>
    public Instellingen Instellingen { get; init; } = new Instellingen();
}
