namespace TheCuratool.Domain;

/// <summary>
/// A setup rule that atomically converts one Outsider slot into one Townsfolk slot.
/// </summary>
public sealed record SwapOutsiderForTownsfolkSetupRule : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        var adjusted = current with
        {
            Outsiders = current.Outsiders - 1,
            Townsfolk = current.Townsfolk + 1,
        };

        return new[] { adjusted };
    }
}
