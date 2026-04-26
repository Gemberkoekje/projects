using SpaceTraders.Domain.Common;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Domain.Aggregates;

[Mutable]
public sealed class Agent : AggregateRoot
{
    private const long CreditsChangedThreshold = 10_000;

    public string Symbol { get; private set; }
    public long Credits { get; private set; }
    public string StartingFaction { get; private set; }
    public int ShipCount { get; private set; }
    public string? HeadquartersSymbol { get; private set; }

    private Agent(string symbol, long credits, string startingFaction, int shipCount, string? headquartersSymbol)
    {
        Symbol = symbol;
        Credits = credits;
        StartingFaction = startingFaction;
        ShipCount = shipCount;
        HeadquartersSymbol = headquartersSymbol;
    }

    public static Agent Reconstitute(string symbol, long credits, string startingFaction, int shipCount, string? headquartersSymbol)
        => new(symbol, credits, startingFaction, shipCount, headquartersSymbol);

    public void UpdateCredits(long newCredits)
    {
        var delta = Math.Abs(newCredits - Credits);
        var old = Credits;
        Credits = newCredits;

        if (delta >= CreditsChangedThreshold)
            RaiseDomainEvent(new AgentCreditsChangedEvent(old, newCredits));
    }

    public void UpdateShipCount(int shipCount) => ShipCount = shipCount;
}
