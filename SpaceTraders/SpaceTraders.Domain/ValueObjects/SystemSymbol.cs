namespace SpaceTraders.Domain.ValueObjects;

public sealed record SystemSymbol
{
    public string Value { get; }

    public SystemSymbol(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.ToUpperInvariant();
    }

    public override string ToString() => Value;
}
