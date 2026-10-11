namespace Curator.Core.Content;

/// <summary>One way a patron can greet the curator, used when its condition holds.</summary>
public sealed record GreetingVariant
{
    public Condition When { get; init; } = Condition.Always;

    public required string Text { get; init; }
}
