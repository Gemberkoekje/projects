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
}
