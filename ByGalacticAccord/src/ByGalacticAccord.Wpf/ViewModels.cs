using System.Windows.Media;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Wpf;

/// <summary>A row on the contract board (the bound list item).</summary>
internal sealed class BoardRow
{
    public required ContractId Id { get; init; }

    public required Brush Danger { get; init; }

    public required string Good { get; init; }

    public required string Route { get; init; }

    public required string Law { get; init; }

    public required string Fee { get; init; }

    public required string Deadline { get; init; }

    public override string ToString() => $"{Good}  {Route}  {Fee}";
}

/// <summary>A row in the player's active-hauls list, with a 0..1 delivery progress.</summary>
internal sealed class HaulRow
{
    public required string Cargo { get; init; }

    public required string Route { get; init; }

    public required string Detail { get; init; }

    public required double Progress { get; init; }
}

/// <summary>A coloured line in the event ticker.</summary>
internal sealed class LogRow
{
    public required Brush Color { get; init; }

    public required string Text { get; init; }
}
