namespace TheCuratool.Domain;

/// <summary>
/// A setup rule that converts one Townsfolk slot into an Outsider slot.
/// Used by characters that are secretly Outsiders, such as the Drunk.
/// </summary>
public sealed record ReplaceTownsfolkSetupRule : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        if (current.Townsfolk == 0 && current.Outsiders > 0)
        {
            // No townsfolk slots remain, but outsider slots exist: the Drunk is shown as a
            // townsfolk by definition, so one outsider slot becomes a townsfolk display slot.
            return new[]
            {
                current with
                {
                    Townsfolk = current.Townsfolk + 1,
                    Outsiders = current.Outsiders - 1,
                },
            };
        }

        return new[]
        {
            current with
            {
                Townsfolk = current.Townsfolk - 1,
                Outsiders = current.Outsiders + 1,
            },
        };
    }
}
