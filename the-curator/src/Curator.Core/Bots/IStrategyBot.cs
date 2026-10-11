using Curator.Core.Game;

namespace Curator.Core.Bots;

/// <summary>A strategy that plays visits through commands only (BUILD_BRIEF §8.4).</summary>
public interface IStrategyBot
{
    /// <summary>The bot's name, as the command line spells it.</summary>
    string Name { get; }

    /// <summary>Plays the visit at the counter until it is decided.</summary>
    /// <param name="session">The session; a visit is in progress.</param>
    void PlayVisit(GameSession session);
}
