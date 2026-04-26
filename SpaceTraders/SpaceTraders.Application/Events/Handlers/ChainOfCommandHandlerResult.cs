using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.Events.Handlers;

public abstract record ChainOfCommandHandlerResult
{
    public static ChainOfCommandHandlerResult Skipped() => SkippedChainOfCommandHandlerResult.Instance;

    public static ChainOfCommandHandlerResult Handled(ChainOfCommandEvent nextEvent)
        => new HandledChainOfCommandHandlerResult(nextEvent, false, DateTimeOffset.MinValue);

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

internal sealed record HandledChainOfCommandHandlerResult(
    ChainOfCommandEvent NextEvent,
    bool IsScheduled,
    DateTimeOffset ScheduledFor) : ChainOfCommandHandlerResult;

internal sealed record FailedChainOfCommandHandlerResult(string Reason) : ChainOfCommandHandlerResult;
