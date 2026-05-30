namespace TheCuratool.Domain;

/// <summary>
/// A setup rule that converts one Demon slot into an Outsider slot.
/// Used by characters that are secretly Outsiders masquerading as Demons, such as the Lunatic.
/// When no Demon slots remain but Outsider slots exist, the rule instead converts one Outsider
/// slot into a Demon display slot, because the character is shown as a Demon by definition.
/// </summary>
public sealed record ReplaceDemonOutsiderDisplayRule : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        if (current.Demons == 0 && current.Outsiders > 0)
        {
            // No demon slots remain, but outsider slots exist: the Lunatic is shown as a
            // demon by definition, so one outsider slot becomes a demon display slot.
            return new[]
            {
                current with
                {
                    Demons = current.Demons + 1,
                    Outsiders = current.Outsiders - 1,
                },
            };
        }

        return new[]
        {
            current with
            {
                Demons = current.Demons - 1,
                Outsiders = current.Outsiders + 1,
            },
        };
    }
}
