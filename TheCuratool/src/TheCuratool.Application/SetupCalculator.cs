using TheCuratool.Domain;

namespace TheCuratool.Application;

/// <summary>
/// Computes the valid character-type distributions for a game based on player count,
/// active characters, Loric modifiers, hidden flags, and session options.
/// </summary>
public sealed class SetupCalculator
{
    private readonly IReadOnlyDictionary<int, SetupCounts> _baseDistributions = new Dictionary<int, SetupCounts>
    {
        [5] = new SetupCounts(3, 0, 1, 1),
        [6] = new SetupCounts(3, 1, 1, 1),
        [7] = new SetupCounts(5, 0, 1, 1),
        [8] = new SetupCounts(5, 1, 1, 1),
        [9] = new SetupCounts(5, 2, 1, 1),
        [10] = new SetupCounts(7, 0, 2, 1),
        [11] = new SetupCounts(7, 1, 2, 1),
        [12] = new SetupCounts(7, 2, 2, 1),
        [13] = new SetupCounts(9, 0, 3, 1),
        [14] = new SetupCounts(9, 1, 3, 1),
        [15] = new SetupCounts(9, 2, 3, 1),
    };

    /// <summary>
    /// Calculates the <see cref="SetupCalculationResult"/> for the given session state.
    /// </summary>
    /// <param name="script">The script in play.</param>
    /// <param name="playerCount">Number of players (5–15).</param>
    /// <param name="chosenCharacterIds">Character IDs confirmed chosen so far.</param>
    /// <param name="activeLoricIds">Loric IDs currently active for this session.</param>
    /// <param name="hiddenFlagsByCharacterId">Storyteller-only hidden flags keyed by character ID.</param>
    /// <param name="options">Pre-draft session options (e.g. Marionette).</param>
    /// <param name="characterDatabase">The character database for rule resolution.</param>
    /// <param name="loricDatabase">The Loric database for rule resolution.</param>
    public SetupCalculationResult Calculate(
        Script script,
        int playerCount,
        IReadOnlyList<string> chosenCharacterIds,
        IReadOnlyList<string> activeLoricIds,
        IReadOnlyDictionary<string, HiddenFlags> hiddenFlagsByCharacterId,
        SessionSetupOptions options,
        CharacterDatabase characterDatabase,
        LoricDatabase loricDatabase)
    {
        if (!_baseDistributions.TryGetValue(playerCount, out var baseDistribution))
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount), "Player count must be between 5 and 15.");
        }

        var setupContext = new SetupContext(playerCount, chosenCharacterIds, activeLoricIds);
        var activeRules = ResolveRules(script, chosenCharacterIds, activeLoricIds, hiddenFlagsByCharacterId, options, characterDatabase, loricDatabase);

        var outcomes = new HashSet<SetupCounts> { baseDistribution };
        foreach (var rule in activeRules)
        {
            var nextOutcomes = new HashSet<SetupCounts>();
            foreach (var outcome in outcomes)
            {
                foreach (var updated in rule.Apply(outcome, setupContext))
                {
                    if (updated.Townsfolk < 0 || updated.Outsiders < 0 || updated.Minions < 0 || updated.Demons < 0)
                    {
                        continue;
                    }

                    nextOutcomes.Add(updated);
                }
            }

            outcomes = nextOutcomes;
        }

        var validTargetCounts = outcomes.OrderBy(c => c.Townsfolk)
            .ThenBy(c => c.Outsiders)
            .ThenBy(c => c.Minions)
            .ThenBy(c => c.Demons)
            .ToList()
            .AsReadOnly();

        return new SetupCalculationResult(baseDistribution, validTargetCounts);
    }

    private static IReadOnlyList<ISetupRule> ResolveRules(
        Script script,
        IReadOnlyList<string> chosenCharacterIds,
        IReadOnlyList<string> activeLoricIds,
        IReadOnlyDictionary<string, HiddenFlags> hiddenFlagsByCharacterId,
        SessionSetupOptions options,
        CharacterDatabase characterDatabase,
        LoricDatabase loricDatabase)
    {
        var rules = new List<ISetupRule>();

        foreach (var id in chosenCharacterIds)
        {
            var hiddenFlags = hiddenFlagsByCharacterId.TryGetValue(id, out var flags)
                ? flags
                : new HiddenFlags(false, false);

            if (hiddenFlags.IsDrunk || hiddenFlags.IsLunatic)
            {
                continue;
            }

            var character = script.Characters.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? characterDatabase.Resolve(id);

            rules.AddRange(character.SetupRules);
        }

        foreach (var loricId in activeLoricIds)
        {
            var loric = loricDatabase.GetAll().FirstOrDefault(l => string.Equals(l.Id, loricId, StringComparison.OrdinalIgnoreCase));
            if (loric is not null)
            {
                rules.AddRange(loric.SetupRules);
            }
        }

        if (options.UseMarionette)
        {
            rules.Add(new MarionetteSessionAdjustmentRule());
        }

        return rules.AsReadOnly();
    }
}
