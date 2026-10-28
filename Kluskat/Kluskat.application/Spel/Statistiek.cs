namespace Kluskat.application.Spel;

/// <summary>Everything the achievement rules look at, computed once from the completion log.</summary>
internal sealed record Statistiek
{
    public int Totaal { get; init; }

    public int Klein { get; init; }

    public int Groot { get; init; }

    public bool Dapper { get; init; }

    public int MeesteKeerOvergeslagen { get; init; }

    public bool Comeback { get; init; }

    public bool OveralGeweest { get; init; }

    public int MeesteOpEenDag { get; init; }

    public bool HeelWeekend { get; init; }

    public bool VroegeVogel { get; init; }

    public bool Nachtbraker { get; init; }

    public bool Jaarklus { get; init; }

    public int VoorDeKatten { get; init; }

    public int Planten { get; init; }

    public int LangsteReeks { get; init; }
}
