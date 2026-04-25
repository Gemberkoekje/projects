namespace SpaceTraders.Domain.ValueObjects;

public sealed record CargoItem
{
    public TradeSymbol Symbol { get; }
    public int Units { get; }

    public CargoItem(TradeSymbol symbol, int units)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(units);
        Symbol = symbol;
        Units = units;
    }
}

public sealed record Cargo
{
    public int Units { get; }
    public int Capacity { get; }
    public IReadOnlyList<CargoItem> Items { get; }

    public Cargo(int units, int capacity, IReadOnlyList<CargoItem> items)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(units);
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        Units = units;
        Capacity = capacity;
        Items = items;
    }
}
