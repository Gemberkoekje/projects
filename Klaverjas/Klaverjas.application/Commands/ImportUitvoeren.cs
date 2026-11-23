namespace Klaverjas.application.Commands;

/// <summary>"Inlezen": writes the planned import.</summary>
public sealed record ImportUitvoeren : ICommand<ImportUitgevoerd>
{
    /// <summary>The plan.</summary>
    public Guid Id { get; init; }
}
