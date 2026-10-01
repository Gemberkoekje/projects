namespace SpaceTraders.Application.Health;

/// <summary>A subject that breaks a health rule, and what is wrong with it.</summary>
public sealed record HealthViolation
{
    /// <summary>Creates a violation.</summary>
    /// <param name="Subject">What the anomaly is about: a ship, a contract, the agent, the API or a log statement.</param>
    /// <param name="Details">What is wrong, as the end of a journal line, without a closing full stop.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public HealthViolation(string Subject, string Details)
    {
        this.Subject = Subject;
        this.Details = Details;
    }

    /// <summary>What the anomaly is about: its <c>subject</c> label and the journal's <c>Subject</c>.</summary>
    public required string Subject { get; init; }

    /// <summary>
    /// What is wrong, as the journal line ends: <c>AnomalyRaised: ShipStuck on SHIP-1: {Details}.</c>
    /// </summary>
    public required string Details { get; init; }
}
