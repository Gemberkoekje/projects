namespace SpaceTraders.Domain.ValueObjects;

public sealed record WaypointSymbol
{
    public string Value { get; }

    public WaypointSymbol(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.ToUpperInvariant();
    }

    public override string ToString() => Value;
}
