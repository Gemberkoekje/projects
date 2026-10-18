namespace Vista.Recipes;

/// <summary>
/// The set-piece catalogue (vista-plan §3.2): one open-ended list of things a wallpaper can contain. A recipe
/// holds the subset currently chosen — add a piece to get it, remove it to drop it ("snow-capped mountains +
/// forest + lake"); either starts a new scene. The catalogue is expected to grow long; JSON stores the camelCase names, so moving it from
/// an enum to a registry later does not change recipe files. Only the terrain-shaping pieces affect M0's
/// world; the rest are recorded and arrive with their milestones.
/// </summary>
public enum SetPiece
{
    /// <summary>High, glaciated peaks with snow down their faces.</summary>
    SnowCappedMountains,
    /// <summary>A jagged alpine range.</summary>
    AlpineRange,
    /// <summary>Low, soft hills.</summary>
    RollingHills,
    /// <summary>A coastline: cliffs and beaches (M9).</summary>
    Coast,
    /// <summary>Forest (vegetation arrives in M5).</summary>
    Forest,
    /// <summary>A lake in a basin in front of the viewpoint (M3).</summary>
    Lake,
    /// <summary>A frozen lake: ice with snow on top (M7).</summary>
    FrozenLake,
    /// <summary>A river from the erosion flow map (M3).</summary>
    River,
    /// <summary>A waterfall in a planted river step (M10).</summary>
    Waterfall,
    /// <summary>Open sea (M9).</summary>
    Sea,
}

/// <summary>
/// Set pieces that cannot share a recipe (vista-plan §3.2), e.g. a lake and a frozen lake. The relation is
/// symmetric and grows with the catalogue. <see cref="Recipe.With"/> swaps a conflicting piece out;
/// <see cref="Recipe.Validate"/> rejects a recipe file that lists both.
/// </summary>
public static class SetPieceConflicts
{
    private static readonly (SetPiece A, SetPiece B)[] Pairs =
    [
        (SetPiece.Lake, SetPiece.FrozenLake),
    ];

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> cannot be in one recipe.</summary>
    public static bool Between(SetPiece a, SetPiece b) =>
        Pairs.Any(p => (p.A == a && p.B == b) || (p.A == b && p.B == a));

    /// <summary>Every piece that conflicts with <paramref name="piece"/>.</summary>
    public static IEnumerable<SetPiece> Of(SetPiece piece) =>
        Pairs.Where(p => p.A == piece || p.B == piece).Select(p => p.A == piece ? p.B : p.A);
}

/// <summary>Time of day (vista-plan §3.2): one value per recipe. Real astronomy replaces the M0 sun placeholder in M1.</summary>
public enum TimeOfDay
{
    /// <summary>Around sunrise.</summary>
    Dawn,
    /// <summary>Sun high in the sky.</summary>
    Day,
    /// <summary>Low, warm evening sun.</summary>
    GoldenHour,
    /// <summary>Around sunset.</summary>
    Dusk,
    /// <summary>Sun below the horizon.</summary>
    Night,
}

/// <summary>Season (vista-plan §3.2 / §3.5): one value per recipe. The finer early / peak / late control comes later.</summary>
public enum Season
{
    /// <summary>Spring.</summary>
    Spring,
    /// <summary>Summer.</summary>
    Summer,
    /// <summary>Autumn.</summary>
    Autumn,
    /// <summary>Winter.</summary>
    Winter,
}
