using System.Globalization;

namespace ByGalacticAccord.Engine.Domain;

// Typed identifiers. The GDD calls for Qowaiv-style typed IDs; we roll lightweight
// long-backed structs so the slice runs without external packages. Longs (not Guids)
// keep IDs deterministic across a seeded replay — they come from a monotonic counter
// in the world, never from a non-deterministic source.

/// <summary>Identifies an <see cref="Actors.ActorState"/>.</summary>
public readonly record struct ActorId(long Value)
{
    public override string ToString() => "A" + Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Identifies a <see cref="Ship"/>.</summary>
public readonly record struct ShipId(long Value)
{
    public override string ToString() => "S" + Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Identifies a <see cref="Location"/>.</summary>
public readonly record struct LocationId(long Value)
{
    public override string ToString() => "L" + Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Identifies a <see cref="Contract"/>.</summary>
public readonly record struct ContractId(long Value)
{
    public override string ToString() => "C" + Value.ToString(CultureInfo.InvariantCulture);
}
