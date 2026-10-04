using Serilog.Core;
using Serilog.Events;
using SpaceTraders.Infrastructure.Persistence.Scoping;

namespace SpaceTraders.API.Services;

/// <summary>
/// What keeps one run's data apart from the next in Grafana (slice 2.13, D70): the server reset the agent was registered
/// under, such as <c>2026-10-04</c>. The bot registers the same symbol after every reset, so the agent's symbol and its
/// ships' symbols repeat; the reset date doesn't. Every <c>spacetraders_*</c> series carries it as the label
/// <see cref="Name"/>, and every log line as the property <see cref="LogProperty"/>.
/// </summary>
/// <remarks>
/// It comes from the active agent's id (<c>SYMBOL@resetDate</c>, <see cref="AgentIdentity"/>), which is empty until agent
/// bootstrap has run. A reset ends the process (<c>ServerResetMonitor</c>), so one process has one value once bootstrap
/// has set it. What is written before that, such as bootstrap's own API calls, carries an empty value, which Prometheus
/// stores as no label at all.
/// </remarks>
public static class ResetDateLabel
{
    /// <summary>The label's name on the metrics.</summary>
    public const string Name = "reset_date";

    /// <summary>The property's name on the log lines.</summary>
    public const string LogProperty = "ResetDate";

    /// <summary>The reset date in an agent id: <c>2026-10-04</c> in <c>SPECTER@2026-10-04</c>; empty without one.</summary>
    /// <param name="agentId">The agent id, or empty before agent bootstrap.</param>
    /// <returns>The reset date, or empty.</returns>
    public static string Of(string agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        var at = agentId.LastIndexOf('@');
        return at < 0 ? string.Empty : agentId[(at + 1)..];
    }
}

/// <summary>
/// Puts the active agent's reset date on every log line, as <see cref="ResetDateLabel.LogProperty"/>, once agent bootstrap
/// has run (slice 2.13, D70). Loki's queries filter on it, as the dashboards' queries filter the metrics on
/// <see cref="ResetDateLabel.Name"/>.
/// </summary>
/// <param name="agent">The active agent, read on every line.</param>
public sealed class ResetDateEnricher(IAgentDataScope agent) : ILogEventEnricher
{
    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        var resetDate = ResetDateLabel.Of(agent.AgentId);
        if (resetDate.Length > 0)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(ResetDateLabel.LogProperty, resetDate));
        }
    }
}
