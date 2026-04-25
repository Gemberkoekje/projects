namespace SpaceTraders.Domain.ValueObjects;

public sealed record Fuel
{
    public int Current { get; }
    public int Capacity { get; }

    public Fuel(int current, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(current);
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        Current = current;
        Capacity = capacity;
    }

    public double Percentage => Capacity == 0 ? 0 : (double)Current / Capacity;
}
