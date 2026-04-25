namespace SpaceTraders.Domain.ValueObjects;

public sealed record TradeSymbol
{
    public string Value { get; }

    public TradeSymbol(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.ToUpperInvariant();
    }

    public override string ToString() => Value;
}
