using SpaceTraders.Application.Events.Handlers;

namespace SpaceTraders.Application.Events.Dispatching;

public sealed record ChainOfCommandDispatchResult(
    string HandlerName,
    string Outcome,
    string NextEventType,
    bool IsScheduled);
