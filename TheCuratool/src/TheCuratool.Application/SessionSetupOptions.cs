namespace TheCuratool.Application;

/// <summary>
/// Pre-draft options that affect the setup calculation and draft session initialisation.
/// </summary>
/// <param name="UseMarionette">When <see langword="true"/>, the Marionette adjustment is applied (+1 Townsfolk / −1 Minion).</param>
/// <param name="UseAtheist">When <see langword="true"/>, setup uses canonical Atheist distribution semantics.</param>
/// <param name="IsLegionGame">When <see langword="true"/>, setup uses Legion-mode distribution math.</param>
/// <param name="LegionCount">Requested Legion-slot count when Legion mode is enabled; 0 uses the default formula.</param>
public sealed record SessionSetupOptions(
    bool UseMarionette,
    bool UseAtheist = false,
    bool IsLegionGame = false,
    int LegionCount = 0)
{
    public SessionSetupOptions(bool useMarionette, bool isLegionGame, int legionCount)
        : this(useMarionette, false, isLegionGame, legionCount)
    {
    }
}
