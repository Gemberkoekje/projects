namespace Kluskat.application.Commands;

/// <summary>Seeds the list on first start, from the chore list agreed in DESIGN.md.</summary>
public sealed record StartvullingToepassen : ICommand
{
    /// <summary>The klussen, in seed order (which decides the stagger within an interval).</summary>
    public IReadOnlyList<Klus.Data> Klussen { get; init; } = [];
}
