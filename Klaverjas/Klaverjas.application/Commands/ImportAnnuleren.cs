namespace Klaverjas.application.Commands;

/// <summary>"Annuleren" on the import screen: forgets the plan, writes nothing.</summary>
public sealed record ImportAnnuleren : ICommand
{
    /// <summary>The plan.</summary>
    public Guid Id { get; init; }
}
