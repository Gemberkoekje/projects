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
    /// <param name="borrowedAbilityCharacterIds">IDs of abilities borrowed by dynamic-setup characters (Alchemist / Boffin). Their setup rules are included unconditionally.</param>
    /// <param name="feasibility">
    /// Draft-state reachability filter. When omitted, defaults to <see cref="SetupFeasibilityContext.Unconstrained"/>,
    /// which keeps every enumerated distribution (pure enumeration, no pruning).
    /// </param>
    public SetupCalculationResult Calculate(
        Script script,
        int playerCount,
        IReadOnlyList<string> chosenCharacterIds,
        IReadOnlyList<string> activeLoricIds,
        IReadOnlyDictionary<string, HiddenFlags> hiddenFlagsByCharacterId,
        SessionSetupOptions options,
        CharacterDatabase characterDatabase,
        LoricDatabase loricDatabase,
        IReadOnlyList<string>? borrowedAbilityCharacterIds = null,
        SetupFeasibilityContext? feasibility = null)
    {
        var feasibilityContext = feasibility ?? SetupFeasibilityContext.Unconstrained;
        if (!_baseDistributions.TryGetValue(playerCount, out var baseDistribution))
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount), "Player count must be between 5 and 15.");
        }

        if (options.IsLegionGame)
        {
            var legionCount = options.LegionCount == 0
                ? GetDefaultLegionCount(playerCount)
                : options.LegionCount;

            if (legionCount < 0 || legionCount > playerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(options.LegionCount), "Legion count must be between 0 and player count.");
            }

            var goodSlots = playerCount - legionCount;
            var legionOutsiderCharacterCount = script.EffectiveCharacters.Count(character => character.Type == CharacterType.Outsider);
            var outsiderCap = Math.Min(goodSlots, legionOutsiderCharacterCount);

            var legionDistributions = new List<SetupCounts>();
            for (var outsiderCount = 0; outsiderCount <= outsiderCap; outsiderCount++)
            {
                legionDistributions.Add(new SetupCounts(
                    goodSlots - outsiderCount,
                    outsiderCount,
                    0,
                    legionCount));
            }

            var baseLegionDistribution = legionDistributions[0];
            return new SetupCalculationResult(
                baseLegionDistribution,
                legionDistributions.AsReadOnly());
        }

        if (options.UseAtheist)
        {
            var atheistDistribution = new SetupCounts(
                baseDistribution.Townsfolk + baseDistribution.Minions + baseDistribution.Demons,
                baseDistribution.Outsiders,
                0,
                0);

            return new SetupCalculationResult(
                baseDistribution,
                new[] { atheistDistribution }.ToList().AsReadOnly());
        }

        var outsiderCharacterCount = script.EffectiveCharacters.Count(character => character.Type == CharacterType.Outsider);
        var hasAnyDemonChosen = chosenCharacterIds.Any(characterId =>
        {
            var character = script.EffectiveCharacters.FirstOrDefault(candidate => string.Equals(candidate.Id, characterId, StringComparison.OrdinalIgnoreCase));
            if (character is null || character.Type != CharacterType.Demon)
            {
                return false;
            }

            // A Drunk/Lunatic-flagged Demon does not occupy the Demon slot (it counts as an Outsider),
            // so it must not suppress the other Demon alternatives — a real Demon is still needed.
            if (hiddenFlagsByCharacterId.TryGetValue(characterId, out var flags) && (flags.IsDrunk || flags.IsLunatic))
            {
                return false;
            }

            return true;
        });

        var setupContext = new SetupContext(playerCount, chosenCharacterIds, activeLoricIds, outsiderCharacterCount, hasAnyDemonChosen);

        // Mandatory sources: rules that are already locked in (chosen characters, the chosen Demon,
        // borrowed abilities, active Lorics, and the Marionette option). These always apply.
        var mandatoryRules = ResolveRules(script, chosenCharacterIds, activeLoricIds, hiddenFlagsByCharacterId, options, characterDatabase, loricDatabase, borrowedAbilityCharacterIds);

        // Optional sources: still-draftable, not-yet-chosen characters on the script whose setup rules
        // could broaden the valid-target list. Demon sources are mutually exclusive alternatives;
        // non-demon sources stack via apply-or-skip.
        var (optionalDemonSources, optionalNonDemonSources, hasPlainDraftableDemon) = ResolveOptionalSources(
            script, chosenCharacterIds, borrowedAbilityCharacterIds, hasAnyDemonChosen);

        // Apply the mandatory rules first to produce the set of base outcomes shared by every branch.
        var mandatoryOutcomes = ApplyRules(new HashSet<SetupCounts> { baseDistribution }, mandatoryRules, setupContext);

        var outcomes = new HashSet<SetupCounts>();

        // Demon alternatives are mutually exclusive: enumerate one branch per still-draftable Demon
        // source (each contributing its rules under HasAnyDemonChosen=true so a Demon remains in play),
        // plus a no-rule branch representing a plain Demon (e.g. Imp) being drafted unchanged. The
        // no-rule branch is suppressed only when every Demon path on the script carries a setup rule
        // (so the unchanged base is genuinely unreachable); when no Demon sources exist at all the base
        // remains the natural fallback.
        var includeNoRuleBranch = hasPlainDraftableDemon
            || hasAnyDemonChosen
            || optionalDemonSources.Count == 0;
        var demonBranches = BuildDemonBranches(optionalDemonSources, includeNoRuleBranch);
        foreach (var demonBranchRules in demonBranches)
        {
            var demonContext = demonBranchRules.Count == 0
                ? setupContext
                : setupContext with { HasAnyDemonChosen = true };

            var branchOutcomes = ApplyRules(mandatoryOutcomes, demonBranchRules, demonContext);

            // Non-demon optional sources stack independently: each may apply or be skipped.
            foreach (var nonDemonRules in EnumerateNonDemonSubsets(optionalNonDemonSources))
            {
                foreach (var outcome in ApplyRules(branchOutcomes, nonDemonRules, demonContext))
                {
                    outcomes.Add(outcome);
                }
            }
        }

        var validTargetCounts = outcomes
            .Where(feasibilityContext.IsReachable)
            .OrderBy(c => c.Townsfolk)
            .ThenBy(c => c.Outsiders)
            .ThenBy(c => c.Minions)
            .ThenBy(c => c.Demons)
            .ToList()
            .AsReadOnly();

        return new SetupCalculationResult(baseDistribution, validTargetCounts);
    }

    private static HashSet<SetupCounts> ApplyRules(
        IReadOnlyCollection<SetupCounts> seedOutcomes,
        IReadOnlyList<ISetupRule> rules,
        SetupContext context)
    {
        var outcomes = new HashSet<SetupCounts>(seedOutcomes);

        foreach (var rule in rules)
        {
            var nextOutcomes = new HashSet<SetupCounts>();
            foreach (var outcome in outcomes)
            {
                foreach (var updated in rule.Apply(outcome, context))
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

        return outcomes;
    }

    /// <summary>
    /// Builds the mutually-exclusive Demon-source branches. Each still-draftable Demon source
    /// contributes one branch consisting of its setup rules. When a plain Demon (no count-affecting
    /// rules, e.g. Imp) is still draftable — or a Demon has already been chosen — a final empty branch
    /// represents the distribution being left unchanged.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<ISetupRule>> BuildDemonBranches(
        IReadOnlyList<IReadOnlyList<ISetupRule>> optionalDemonSources,
        bool includeNoRuleBranch)
    {
        var branches = new List<IReadOnlyList<ISetupRule>>();

        foreach (var demonRules in optionalDemonSources)
        {
            branches.Add(demonRules);
        }

        // No-rule branch: only legal when a plain demon can still be drafted as-is (or one is already
        // in play). Otherwise every Demon in play carries a setup rule and the unchanged base is unreachable.
        if (includeNoRuleBranch)
        {
            branches.Add(Array.Empty<ISetupRule>());
        }

        return branches;
    }

    /// <summary>
    /// Enumerates the apply-or-skip power set over the optional non-demon sources, returning the
    /// flattened rule list for each subset.
    /// </summary>
    private static IEnumerable<IReadOnlyList<ISetupRule>> EnumerateNonDemonSubsets(
        IReadOnlyList<IReadOnlyList<ISetupRule>> optionalNonDemonSources)
    {
        var count = optionalNonDemonSources.Count;
        var total = 1 << count;

        for (var mask = 0; mask < total; mask++)
        {
            var combined = new List<ISetupRule>();
            for (var index = 0; index < count; index++)
            {
                if ((mask & (1 << index)) != 0)
                {
                    combined.AddRange(optionalNonDemonSources[index]);
                }
            }

            yield return combined;
        }
    }

    /// <summary>
    /// Partitions still-draftable, not-yet-chosen on-script characters into optional Demon sources
    /// (mutually exclusive alternatives) and optional non-demon sources (stackable). Characters with
    /// no count-affecting setup rules contribute nothing and are skipped.
    /// </summary>
    private static (IReadOnlyList<IReadOnlyList<ISetupRule>> DemonSources, IReadOnlyList<IReadOnlyList<ISetupRule>> NonDemonSources, bool HasPlainDraftableDemon) ResolveOptionalSources(
        Script script,
        IReadOnlyList<string> chosenCharacterIds,
        IReadOnlyList<string>? borrowedAbilityCharacterIds,
        bool hasAnyDemonChosen)
    {
        var chosenSet = new HashSet<string>(chosenCharacterIds, StringComparer.OrdinalIgnoreCase);
        var borrowedSet = borrowedAbilityCharacterIds is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(borrowedAbilityCharacterIds, StringComparer.OrdinalIgnoreCase);

        var hasAnyMinionChosen = chosenCharacterIds.Any(id =>
        {
            var character = script.EffectiveCharacters.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            return character is not null && character.Type == CharacterType.Minion;
        });

        var availabilityContext = new AvailabilityContext(
            chosenCharacterIds,
            hasAnyMinionChosen,
            hasAnyDemonChosen,
            chosenCharacterIds.Count,
            false,
            false);

        var demonSources = new List<IReadOnlyList<ISetupRule>>();
        var nonDemonSources = new List<IReadOnlyList<ISetupRule>>();
        var hasPlainDraftableDemon = false;

        foreach (var character in script.EffectiveCharacters)
        {
            if (character.IsDraftExcluded)
            {
                continue;
            }

            if (chosenSet.Contains(character.Id) || borrowedSet.Contains(character.Id))
            {
                continue;
            }

            // A chosen Demon makes all other Demon alternatives impossible (only one Demon in play).
            if (character.Type == CharacterType.Demon && hasAnyDemonChosen)
            {
                continue;
            }

            // Respect availability constraints (e.g. Kazali/Summoner blocked once a Demon/Minion exists).
            if (character.AvailabilityConstraints.Any(constraint => !constraint.IsAvailable(availabilityContext)))
            {
                continue;
            }

            var countAffectingRules = character.SetupRules
                .Where(rule => rule is not RequiresCharacterSetupRule)
                .ToList()
                .AsReadOnly();

            if (countAffectingRules.Count == 0)
            {
                // A still-draftable plain Demon (no count-affecting rules, e.g. Imp) legitimises the
                // no-rule Demon branch: the base distribution stays reachable by drafting it as-is.
                if (character.Type == CharacterType.Demon)
                {
                    hasPlainDraftableDemon = true;
                }

                continue;
            }

            if (character.Type == CharacterType.Demon)
            {
                demonSources.Add(countAffectingRules);
            }
            else if (character.Type != CharacterType.Outsider)
            {
                // Unchosen Outsiders (e.g. Drunk on Trouble Brewing) must not broaden setup counts.
                // Their setup adjustments only apply once that Outsider is actually in play.
                nonDemonSources.Add(countAffectingRules);
            }
        }

        return (demonSources, nonDemonSources, hasPlainDraftableDemon);
    }

    public int GetDefaultLegionCount(int playerCount)
    {
        if (!_baseDistributions.TryGetValue(playerCount, out var baseDistribution))
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount), "Player count must be between 5 and 15.");
        }

        return baseDistribution.Townsfolk + baseDistribution.Outsiders;
    }

    private int GetOutsiderCountForGoodSlots(int goodSlots)
    {
        if (goodSlots <= 0)
        {
            return 0;
        }

        if (_baseDistributions.TryGetValue(goodSlots, out var baseline))
        {
            return baseline.Outsiders;
        }

        return 0;
    }

    private static IReadOnlyList<ISetupRule> ResolveRules(
        Script script,
        IReadOnlyList<string> chosenCharacterIds,
        IReadOnlyList<string> activeLoricIds,
        IReadOnlyDictionary<string, HiddenFlags> hiddenFlagsByCharacterId,
        SessionSetupOptions options,
        CharacterDatabase characterDatabase,
        LoricDatabase loricDatabase,
        IReadOnlyList<string>? borrowedAbilityCharacterIds = null)
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

        // Include borrowed ability rules unconditionally (not subject to hidden flags)
        if (borrowedAbilityCharacterIds is not null)
        {
            foreach (var borrowedId in borrowedAbilityCharacterIds)
            {
                var borrowedCharacter = script.Characters.FirstOrDefault(c => string.Equals(c.Id, borrowedId, StringComparison.OrdinalIgnoreCase))
                    ?? characterDatabase.Resolve(borrowedId);

                rules.AddRange(borrowedCharacter.SetupRules);
            }
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
