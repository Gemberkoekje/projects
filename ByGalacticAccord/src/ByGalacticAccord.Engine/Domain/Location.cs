using System.Collections.Generic;

namespace ByGalacticAccord.Engine.Domain;

/// <summary>
/// The reach of the Galactic Accord (§2.3) made concrete. This single axis explains the
/// whole enforcement-context table (§4.1.3): escrow and arbitration are geography.
/// </summary>
public enum AccordReach
{
    /// <summary>Core system: automatic escrow + Accord arbitration.</summary>
    DeepAccord,

    /// <summary>Mid-tier colony: escrow only, no arbitration.</summary>
    AccordEdge,

    /// <summary>Frontier outpost: reputation only, no escrow — unless bonded (§4.4).</summary>
    OutsideAccord,

    /// <summary>Black market: no formal enforcement at all.</summary>
    BlackMarket,
}

/// <summary>Location archetypes (§4.7.1).</summary>
public enum LocationType
{
    CoreWorld,
    IndustrialWorld,
    AgriculturalWorld,
    MiningColony,
    FrontierOutpost,
    RefineryStation,
}

/// <summary>
/// A node in the route graph with resident actors. Production and consumption here are
/// decoupled (§4.7.1): a farming world that receives luxuries does not grow more food.
/// </summary>
public sealed class Location
{
    private readonly List<ActorId> _residents = new();

    public Location(LocationId id, string name, LocationType type, string region, AccordReach reach)
    {
        Id = id;
        Name = name;
        Type = type;
        Region = region;
        Reach = reach;
    }

    public LocationId Id { get; }

    public string Name { get; }

    public LocationType Type { get; }

    /// <summary>Reputation context key for "the region" (§4.3).</summary>
    public string Region { get; }

    public AccordReach Reach { get; }

    public IReadOnlyList<ActorId> Residents => _residents;

    public void AddResident(ActorId actor)
    {
        if (!_residents.Contains(actor))
        {
            _residents.Add(actor);
        }
    }

    public bool HasEscrow => Reach is AccordReach.DeepAccord or AccordReach.AccordEdge;

    public bool HasArbitration => Reach is AccordReach.DeepAccord;
}
