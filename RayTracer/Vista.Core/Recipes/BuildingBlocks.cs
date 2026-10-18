namespace Vista.Recipes;

/// <summary>Terrain building blocks (vista-plan §3.2). A recipe may combine several.</summary>
public enum TerrainBlock
{
    /// <summary>High, glaciated peaks with snow down their faces.</summary>
    SnowCappedMountains,
    /// <summary>A jagged alpine range.</summary>
    AlpineRange,
    /// <summary>Low, soft hills.</summary>
    RollingHills,
    /// <summary>A coastline (cliffs, beaches — M9).</summary>
    Coast,
    /// <summary>Forested ground (vegetation itself arrives in M5).</summary>
    Forest,
}

/// <summary>Water building blocks (vista-plan §3.2). An empty list means "none".</summary>
public enum WaterBlock
{
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

/// <summary>Time-of-day building block (vista-plan §3.2). Real astronomy replaces the M0 sun placeholder in M1.</summary>
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

/// <summary>Season building block (vista-plan §3.2 / §3.5). The finer early / peak / late control comes later.</summary>
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
