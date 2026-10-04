namespace SpaceTraders.Infrastructure.Persistence.Scoping;

/// <summary>
/// The short id that every agent's rows are keyed on: the agent's symbol and the server's reset
/// date, such as <c>GEMBER@2026-09-27</c>. A symbol is unique within one reset, so the agent that
/// takes the same symbol after the next reset gets a new id. The token itself is stored only in
/// <c>stored_credentials</c>.
/// </summary>
public static class AgentIdentity
{
    public const int MaxLength = 64;

    public static string For(string agentSymbol, string resetDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentSymbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(resetDate);
        return $"{agentSymbol.ToUpperInvariant()}@{resetDate}";
    }

    /// <summary>The reset date in an agent id: <c>2026-10-04</c> in <c>SPECTER@2026-10-04</c>; empty without one.</summary>
    /// <param name="agentId">The agent id, or empty before agent bootstrap.</param>
    /// <returns>The reset date, or empty.</returns>
    public static string ResetDateOf(string agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        var at = agentId.LastIndexOf('@');
        return at < 0 ? string.Empty : agentId[(at + 1)..];
    }
}
