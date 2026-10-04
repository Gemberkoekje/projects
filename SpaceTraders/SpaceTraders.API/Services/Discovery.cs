using System.Diagnostics.CodeAnalysis;

namespace SpaceTraders.API.Services;

/// <summary>
/// A ship type or good no snapshot of the run held yet (<see cref="KnownTypes.ShipTypeKind"/> or
/// <see cref="KnownTypes.GoodKind"/>), with the waypoints that list it.
/// </summary>
public sealed record Discovery
{
    /// <summary>A ship type or a good: <see cref="KnownTypes.ShipTypeKind"/> or <see cref="KnownTypes.GoodKind"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>Its symbol, such as <c>SHIP_LIGHT_HAULER</c> or <c>FAB_MATS</c>.</summary>
    public required string Symbol { get; init; }

    /// <summary>The shipyards or markets that list it.</summary>
    public required IReadOnlyList<string> Waypoints { get; init; }

    /// <summary>Initializes a new instance of the <see cref="Discovery"/> class.</summary>
    /// <param name="kind">A ship type or a good.</param>
    /// <param name="symbol">Its symbol.</param>
    /// <param name="waypoints">The shipyards or markets that list it.</param>
    [SetsRequiredMembers]
    public Discovery(string kind, string symbol, IReadOnlyList<string> waypoints)
    {
        Kind = kind;
        Symbol = symbol;
        Waypoints = waypoints;
    }
}
