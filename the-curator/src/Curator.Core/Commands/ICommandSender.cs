namespace Curator.Core.Commands;

/// <summary>
/// Carries commands to the game: the session itself, or Godot's controller, which also redraws the
/// views after each one. Bots send through this, so Godot's autoplay drives them through the
/// presentation layer (BUILD_BRIEF §7.11).
/// </summary>
public interface ICommandSender
{
    /// <summary>Sends a command.</summary>
    /// <param name="command">The command.</param>
    /// <returns>What happened.</returns>
    CommandResult Send(Command command);
}
