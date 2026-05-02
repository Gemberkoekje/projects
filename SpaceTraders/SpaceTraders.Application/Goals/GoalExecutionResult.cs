namespace SpaceTraders.Application.Goals;

/// <summary>
/// Outcome of a single <see cref="IShipGoalExecutor"/> step.
/// </summary>
/// <remarks>Phase 10: introduced as part of the goal-driven architecture migration.</remarks>
public enum GoalExecutionOutcome
{
    /// <summary>One step was executed; a follow-up <c>ShipAutomationTickEvent</c> will be published by the service.</summary>
    Progressing = 0,

    /// <summary>Ship is in transit; no command issued. The <c>ShipInTransitEventHandler</c> will trigger the next tick at arrival.</summary>
    WaitingForArrival = 1,

    /// <summary>Ship is on cooldown; no command issued. Service should schedule a tick at <see cref="GoalExecutionResult.CooldownExpiresAt"/>.</summary>
    WaitingForCooldown = 2,

    /// <summary>Goal objective achieved; ship is available for a new goal.</summary>
    Completed = 3,

    /// <summary>Cannot progress (missing equipment, no route); goal should be abandoned.</summary>
    Blocked = 4,
}

/// <summary>
/// Result of a single executor step. Carries the outcome and, when blocked, the reason.
/// </summary>
/// <remarks>Phase 10: replaces <c>ShipPlannerDecision</c> for the goal layer.</remarks>
public sealed record GoalExecutionResult
{
    public required GoalExecutionOutcome Outcome { get; init; }

    public string Reason { get; init; } = string.Empty;

    /// <summary>When <see cref="Outcome"/> is <see cref="GoalExecutionOutcome.WaitingForCooldown"/>, the time at which the cooldown expires.</summary>
    public DateTimeOffset? CooldownExpiresAt { get; init; }

    public static GoalExecutionResult Progressing(string reason) =>
        new() { Outcome = GoalExecutionOutcome.Progressing, Reason = reason };

    public static GoalExecutionResult WaitingForArrival(string reason) =>
        new() { Outcome = GoalExecutionOutcome.WaitingForArrival, Reason = reason };

    public static GoalExecutionResult WaitingForCooldown(string reason, DateTimeOffset? expiresAt = null) =>
        new() { Outcome = GoalExecutionOutcome.WaitingForCooldown, Reason = reason, CooldownExpiresAt = expiresAt };

    public static GoalExecutionResult Completed(string reason) =>
        new() { Outcome = GoalExecutionOutcome.Completed, Reason = reason };

    public static GoalExecutionResult Blocked(string reason) =>
        new() { Outcome = GoalExecutionOutcome.Blocked, Reason = reason };
}
