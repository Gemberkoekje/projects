using System;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.Events.Handlers;

public abstract record ChainOfCommandHandlerResult
{
    public static ChainOfCommandHandlerResult Skipped() => SkippedChainOfCommandHandlerResult.Instance;

    /// <summary>
    /// DEPRECATED: Event handlers must not emit events directly.
    /// Use commands instead, which will emit appropriate events when handled.
    /// This method is retained only for backwards compatibility and will be removed in a future version.
    /// </summary>
    [Obsolete("Event handlers must emit commands, not events. State transitions should be declared via command handlers only. Use Handled() and invoke a command via bus.InvokeAsync() instead.", error: false)]
    public static ChainOfCommandHandlerResult Handled(ChainOfCommandEvent nextEvent)
        => new HandledChainOfCommandHandlerResult(nextEvent, false, DateTimeOffset.MinValue);

    public static ChainOfCommandHandlerResult Handled()
        => HandledWithoutNextEventChainOfCommandHandlerResult.Instance;

    /// <summary>
    /// DEPRECATED: Event handlers must not emit events directly.
    /// Use commands instead, which will emit appropriate events when handled.
    /// This method is retained only for backwards compatibility and will be removed in a future version.
    /// </summary>
    [Obsolete("Event handlers must emit commands, not events. State transitions should be declared via command handlers only. Schedule a command invocation instead.", error: false)]
    public static ChainOfCommandHandlerResult Scheduled(ChainOfCommandEvent nextEvent, DateTimeOffset dueAt)
        => new HandledChainOfCommandHandlerResult(nextEvent, true, dueAt);

    public static ChainOfCommandHandlerResult Failed(string reason)
        => new FailedChainOfCommandHandlerResult(reason);
}

internal sealed record SkippedChainOfCommandHandlerResult : ChainOfCommandHandlerResult
{
    private SkippedChainOfCommandHandlerResult()
    {
    }

    public static SkippedChainOfCommandHandlerResult Instance { get; } = new();
}

internal sealed record HandledWithoutNextEventChainOfCommandHandlerResult : ChainOfCommandHandlerResult
{
    private HandledWithoutNextEventChainOfCommandHandlerResult()
    {
    }

    public static HandledWithoutNextEventChainOfCommandHandlerResult Instance { get; } = new();
}

internal sealed record HandledChainOfCommandHandlerResult(
    ChainOfCommandEvent NextEvent,
    bool IsScheduled,
    DateTimeOffset ScheduledFor) : ChainOfCommandHandlerResult;

internal sealed record FailedChainOfCommandHandlerResult(string Reason) : ChainOfCommandHandlerResult;
