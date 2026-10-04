namespace SpaceTraders.Application.Interfaces;

/// <summary>
/// The server reset the active agent was registered under (slice 2.13, D70), such as <c>2026-10-04</c>: what keeps one run
/// apart from the next, as the bot registers the same symbol after every reset.
/// </summary>
public interface IActiveReset
{
    /// <summary>The reset date; empty until agent bootstrap has picked the agent.</summary>
    string ResetDate { get; }
}
