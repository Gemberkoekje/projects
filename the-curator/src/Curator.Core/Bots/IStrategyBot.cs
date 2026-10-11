using Curator.Core.Commands;
using Curator.Core.Game;

namespace Curator.Core.Bots;

/// <summary>A strategy that plays visits through commands only (BUILD_BRIEF §8.4).</summary>
public interface IStrategyBot
{
    /// <summary>The bot's name, as the command line spells it.</summary>
    string Name { get; }

    /// <summary>Plays the visit at the counter until it is decided.</summary>
    /// <param name="session">The session to read; a visit is in progress.</param>
    /// <param name="sender">Where to send commands: the session, or Godot's controller.</param>
    void PlayVisit(GameSession session, ICommandSender sender);
}
