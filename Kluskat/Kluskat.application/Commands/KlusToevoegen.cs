namespace Kluskat.application.Commands;

/// <summary>A new klus from the beheer page.</summary>
public sealed record KlusToevoegen : ICommand
{
    /// <summary>What, where, how often and how big.</summary>
    public Klus.Data Data { get; init; } = new Klus.Data();

    /// <summary>When it was last done; <see langword="default"/> makes it due straight away.</summary>
    public DateOnly LaatstGedaanOp { get; init; }
}
