using Curator.Core.Events;

namespace Curator.Core.Commands;

/// <summary>What came of a command: the events it produced, or why it was refused.</summary>
public sealed record CommandResult
{
    private CommandResult(RejectionReason rejection, IReadOnlyList<GameEvent> events)
    {
        Rejection = rejection;
        Events = events;
    }

    /// <summary>Why the command was refused, or <see cref="RejectionReason.None"/>.</summary>
    public RejectionReason Rejection { get; }

    /// <summary>The events appended; empty when refused.</summary>
    public IReadOnlyList<GameEvent> Events { get; }

    /// <summary>Whether the command was carried out.</summary>
    public bool Accepted => Rejection == RejectionReason.None;

    /// <summary>A carried-out command.</summary>
    /// <param name="events">The events it appended.</param>
    /// <returns>The result.</returns>
    public static CommandResult Done(IReadOnlyList<GameEvent> events) => new(RejectionReason.None, events);

    /// <summary>A refused command.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>The result.</returns>
    public static CommandResult Refused(RejectionReason reason) => new(reason, []);
}
