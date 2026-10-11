namespace Curator.Core.Bots;

/// <summary>One balance guardrail, checked across every seed.</summary>
/// <param name="Name">What it asks.</param>
/// <param name="Holds">Whether it holds in every seed.</param>
/// <param name="Detail">The numbers behind it.</param>
public sealed record GuardrailResult(string Name, bool Holds, string Detail);
