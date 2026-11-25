namespace Klaverjas.application.Commands;

/// <summary>"Weghalen" next to a substitute on the Spelers screen.</summary>
public sealed record SpelerWeghalen : ICommand
{
    /// <summary>Who.</summary>
    public Guid Id { get; init; }
}
