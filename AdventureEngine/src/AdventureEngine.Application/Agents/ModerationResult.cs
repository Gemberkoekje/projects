namespace AdventureEngine.Application.Agents;

public sealed record ModerationResult(bool IsSafe, string? Reason)
{
    public static ModerationResult Safe { get; } = new(true, null);
}
