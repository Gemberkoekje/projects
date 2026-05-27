namespace TheCuratool.Domain;

/// <summary>
/// A pre-draft session-level setup rule applied when the Marionette option is selected.
/// Adds one Townsfolk slot and removes one Minion slot to account for the hidden Marionette.
/// </summary>
public sealed record MarionetteSessionAdjustmentRule : ISetupRule
{
    public IEnumerable<SetupCounts> Apply(SetupCounts current, SetupContext context)
    {
        var adjusted = current with
        {
            Townsfolk = current.Townsfolk + 1,
            Minions = current.Minions - 1,
        };

        return new[] { adjusted };
    }
}
