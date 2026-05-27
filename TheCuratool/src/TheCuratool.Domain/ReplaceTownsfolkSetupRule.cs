namespace TheCuratool.Domain;

/// <summary>
/// A setup rule that converts one Townsfolk slot into an Outsider slot.
/// Used by characters that are secretly Outsiders, such as the Drunk.
/// </summary>
public sealed record ReplaceTownsfolkSetupRule : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        var adjusted = current with
        {
            Townsfolk = current.Townsfolk - 1,
            Outsiders = current.Outsiders + 1,
        };

        return new[] { adjusted };
    }
}
