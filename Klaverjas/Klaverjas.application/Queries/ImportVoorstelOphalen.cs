namespace Klaverjas.application.Queries;

/// <summary>A planned import, for the check screen.</summary>
public sealed record ImportVoorstelOphalen : IQuery<ImportVoorstel>
{
    /// <summary>The plan.</summary>
    public Guid Id { get; init; }
}
