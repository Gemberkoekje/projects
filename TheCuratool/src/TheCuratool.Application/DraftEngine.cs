using TheCuratool.Domain;

namespace TheCuratool.Application;

/// <summary>
/// Orchestrates the Curator draft: creates sessions, tracks state, generates curated offers,
/// validates character selections, and advances players through the draft order.
/// </summary>
public sealed class DraftEngine
{
    private const string LegionCharacterId = "legion";
    private const string EvilSentinelCharacterId = "evil";
    private static readonly string[] StAssignedMinionCharacterIds = ["kazali", "lord_of_typhon"];

    private readonly CharacterDatabase _characterDatabase;
    private readonly LoricDatabase _loricDatabase;
    private readonly SetupCalculator _setupCalculator;
    private readonly Dictionary<Guid, GameSession> _sessions = new();

    public DraftEngine(CharacterDatabase characterDatabase, LoricDatabase loricDatabase, SetupCalculator setupCalculator)
    {
        _characterDatabase = characterDatabase;
        _loricDatabase = loricDatabase;
        _setupCalculator = setupCalculator;
    }

    /// <summary>
    /// Creates a new <see cref="GameSession"/> for the given script and player count.
    /// Players are randomly assigned a draft order.
    /// </summary>
    /// <param name="script">The script to use for character selection.</param>
    /// <param name="playerCount">Number of players (5–15).</param>
    /// <param name="activeLoricIds">IDs of Lorics that are active for this session.</param>
    /// <param name="useMarionette">When <see langword="true"/>, applies the Marionette pre-draft adjustment.</param>
    public GameSession StartSession(
        Script script,
        int playerCount,
        IReadOnlyList<string> activeLoricIds,
        bool useMarionette = false,
        bool isLegionGame = false,
        int legionCount = 0)
    {
        if (playerCount < 5)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount), "Player count must be at least 5.");
        }

        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(activeLoricIds);

        var random = Random.Shared;
        var players = Enumerable.Range(0, playerCount)
            .Select(index => new PlayerSlot(index + 1, Guid.NewGuid(), PlayerChoice.UnchosenChoice.Empty))
            .OrderBy(_ => random.Next())
            .ToList()
            .AsReadOnly();

        var session = new GameSession(
            Guid.NewGuid(),
            script,
            playerCount,
            players,
            GameStatus.Drafting,
            activeLoricIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly(),
            useMarionette,
            isLegionGame,
            legionCount);

        _sessions[session.Id] = session;
        return session;
    }

    public void TrackSession(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _sessions[session.Id] = session;
    }

    public IReadOnlyList<CharacterDefinition> GetRemainingValidCharacters(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var current = ResolveSession(session);
        var state = DraftStateSnapshot.FromSession(current);
        var currentCounts = DraftMath.ComputeCurrentCounts(current, _characterDatabase);
        var chosenSet = new HashSet<string>(state.ChosenCharacterIds, StringComparer.OrdinalIgnoreCase);
        var remainingSeats = GetRemainingSeats(current);
        var setupResult = _setupCalculator.Calculate(
            current.Script,
            current.PlayerCount,
            state.ChosenCharacterIds,
            current.ActiveLoricIds,
            state.HiddenFlagsByCharacterId,
            new SessionSetupOptions(current.UseMarionette, current.IsLegionGame, current.LegionCount),
            _characterDatabase,
            _loricDatabase);

        if (current.IsLegionGame)
        {
            var legionTarget = setupResult.ValidTargetCounts[0].Demons;
            if (currentCounts.Demons < legionTarget)
            {
                return new[]
                {
                    new CharacterDefinition(EvilSentinelCharacterId, "Evil", CharacterType.Demon, Array.Empty<ISetupRule>(), Array.Empty<IAvailabilityConstraint>(), false, false, false),
                }.ToList().AsReadOnly();
            }
        }

        var valid = new List<CharacterDefinition>();

        foreach (var character in current.Script.Characters)
        {
            if (character.IsDraftExcluded)
            {
                continue;
            }

            if (chosenSet.Contains(character.Id))
            {
                continue;
            }

            if (IsSpecialHiddenCharacter(character.Id))
            {
                continue;
            }

            if (!current.IsLegionGame
                && string.Equals(character.Id, LegionCharacterId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (current.IsLegionGame && (character.Type == CharacterType.Minion || character.Type == CharacterType.Demon))
            {
                // Legion mode drafts only good roles directly; evil seats are handled by the "evil" sentinel branch above.
                continue;
            }

            if (!IsAvailableByConstraints(current, character, state))
            {
                continue;
            }

            var projectedChosenIds = state.ChosenCharacterIds.Concat(new[] { character.Id }).ToList().AsReadOnly();
            var projectedHiddenFlags = new Dictionary<string, HiddenFlags>(state.HiddenFlagsByCharacterId, StringComparer.OrdinalIgnoreCase)
            {
                [character.Id] = new HiddenFlags(false, false),
            };

            var projectedSetup = _setupCalculator.Calculate(
                current.Script,
                current.PlayerCount,
                projectedChosenIds,
                current.ActiveLoricIds,
                projectedHiddenFlags,
                new SessionSetupOptions(current.UseMarionette, current.IsLegionGame, current.LegionCount),
                _characterDatabase,
                _loricDatabase);

            if (!IsHardFeasible(character, current.Script, currentCounts, projectedSetup.ValidTargetCounts, state, remainingSeats, chosenSet))
            {
                continue;
            }

            if (!SatisfiesPairFeasibility(character, current.Script, chosenSet, currentCounts, projectedSetup.ValidTargetCounts, remainingSeats))
            {
                continue;
            }

            valid.Add(character);
        }

        return valid
            .OrderBy(c => c.Type)
            .ThenBy(c => c.DisplayName, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<CharacterDefinition> SuggestThree(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var current = ResolveSession(session);
        var valid = GetRemainingValidCharacters(current);
        if (valid.Count <= 3)
        {
            return valid;
        }

        var selected = new List<CharacterDefinition>();
        var orderedTypes = new[] { CharacterType.Townsfolk, CharacterType.Outsider, CharacterType.Minion, CharacterType.Demon };

        foreach (var type in orderedTypes)
        {
            var pick = valid.FirstOrDefault(c => c.Type == type && selected.All(s => !string.Equals(s.Id, c.Id, StringComparison.OrdinalIgnoreCase)));
            if (pick is null)
            {
                continue;
            }

            selected.Add(pick);
            if (selected.Count == 3)
            {
                return selected.AsReadOnly();
            }
        }

        foreach (var character in valid)
        {
            if (character.IsDraftExcluded)
            {
                continue;
            }

            if (selected.Any(s => string.Equals(s.Id, character.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            selected.Add(character);
            if (selected.Count == 3)
            {
                break;
            }
        }

        return selected.AsReadOnly();
    }

    public GameSession CreateCuratedOffer(Guid sessionId, int playerSlotIndex, IReadOnlyList<string> offeredIds)
    {
        ArgumentNullException.ThrowIfNull(offeredIds);

        var session = GetSession(sessionId);
        var currentSlot = GetCurrentSlot(session);
        if (currentSlot.DraftOrder != playerSlotIndex)
        {
            throw new InvalidOperationException("Curated offers are only valid for the current draft slot.");
        }

        var offeredSet = NormalizeOfferedIds(offeredIds);
        if (offeredSet.Count < 1 || offeredSet.Count > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(offeredIds), "Curated offers must include 1 to 3 unique character ids.");
        }

        var validIds = new HashSet<string>(GetRemainingValidCharacters(session).Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var offeredId in offeredSet)
        {
            if (!validIds.Contains(offeredId) && !string.Equals(offeredId, EvilSentinelCharacterId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Curated offer contains invalid id '{offeredId}'.");
            }
        }

        var unchosen = currentSlot.Choice as PlayerChoice.UnchosenChoice ?? PlayerChoice.UnchosenChoice.Empty;
        var updatedSlot = currentSlot with { Choice = new PlayerChoice.UnchosenChoice(offeredSet, unchosen.IsAtheistCommitmentConfirmed) };

        var updated = ReplaceSlot(session, updatedSlot);
        _sessions[updated.Id] = updated;
        return updated;
    }

    public GameSession ConfirmAtheistCommitment(Guid sessionId, int playerSlotIndex)
    {
        var session = GetSession(sessionId);
        var currentSlot = GetCurrentSlot(session);
        if (currentSlot.DraftOrder != playerSlotIndex)
        {
            throw new InvalidOperationException("Atheist commitment must be confirmed on the current draft slot.");
        }

        if (currentSlot.Choice is PlayerChoice.ChosenChoice)
        {
            throw new InvalidOperationException("Cannot confirm commitment for an already chosen slot.");
        }

        var unchosen = currentSlot.Choice as PlayerChoice.UnchosenChoice ?? PlayerChoice.UnchosenChoice.Empty;
        var updatedSlot = currentSlot with { Choice = new PlayerChoice.UnchosenChoice(unchosen.OfferedIds, true) };
        var updated = ReplaceSlot(session, updatedSlot);

        _sessions[updated.Id] = updated;
        return updated;
    }

    public GameSession RecordChoice(Guid sessionId, int playerSlotIndex, string chosenCharacterId, IReadOnlyList<string> offeredIds, HiddenFlags hiddenFlags)
    {
        if (string.IsNullOrWhiteSpace(chosenCharacterId))
        {
            throw new ArgumentException("Chosen character id is required.", nameof(chosenCharacterId));
        }

        ArgumentNullException.ThrowIfNull(offeredIds);

        var session = GetSession(sessionId);
        if (session.Status != GameStatus.Drafting)
        {
            throw new InvalidOperationException("Cannot record choices for a completed session.");
        }

        var currentSlot = GetCurrentSlot(session);
        if (currentSlot.DraftOrder != playerSlotIndex)
        {
            throw new InvalidOperationException("Choice can only be recorded for the current draft slot.");
        }

        var normalizedOfferedIds = NormalizeOfferedIds(offeredIds);
        if (normalizedOfferedIds.Count < 1 || normalizedOfferedIds.Count > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(offeredIds), "Offered ids must contain 1 to 3 unique entries.");
        }

        var normalizedChosenId = chosenCharacterId.Trim();
        var isEvilSentinel = string.Equals(normalizedChosenId, EvilSentinelCharacterId, StringComparison.OrdinalIgnoreCase);
        if (!normalizedOfferedIds.Contains(normalizedChosenId, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Chosen character must be one of the offered ids.");
        }

        var unchosen = currentSlot.Choice as PlayerChoice.UnchosenChoice ?? PlayerChoice.UnchosenChoice.Empty;
        if (unchosen.OfferedIds.Count > 0 && !AreSameOfferSet(unchosen.OfferedIds, normalizedOfferedIds))
        {
            throw new InvalidOperationException("Recorded offered ids conflict with curated offer for this slot.");
        }

        var chosenDefinition = isEvilSentinel
            ? new CharacterDefinition(EvilSentinelCharacterId, "Evil", CharacterType.Demon, Array.Empty<ISetupRule>(), Array.Empty<IAvailabilityConstraint>(), false, false, false)
            : session.Script.Characters.FirstOrDefault(c => string.Equals(c.Id, normalizedChosenId, StringComparison.OrdinalIgnoreCase))
                ?? _characterDatabase.Resolve(normalizedChosenId);

        if (!isEvilSentinel && hiddenFlags.IsDrunk && chosenDefinition.Type != CharacterType.Townsfolk)
        {
            throw new InvalidOperationException("Drunk can only be applied to a Townsfolk character.");
        }

        if (!isEvilSentinel && hiddenFlags.IsLunatic && chosenDefinition.Type != CharacterType.Demon)
        {
            throw new InvalidOperationException("Lunatic can only be applied to a Demon character.");
        }

        var allowsDrunkAtheistOverride = string.Equals(chosenDefinition.Id, "atheist", StringComparison.OrdinalIgnoreCase)
            && hiddenFlags.IsDrunk;

        var validIds = new HashSet<string>(GetRemainingValidCharacters(session).Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
        if (!validIds.Contains(normalizedChosenId)
            && !isEvilSentinel
            && !allowsDrunkAtheistOverride)
        {
            throw new InvalidOperationException("Chosen character is not currently valid.");
        }

        foreach (var offeredId in normalizedOfferedIds)
        {
            if (!validIds.Contains(offeredId)
                && !string.Equals(offeredId, EvilSentinelCharacterId, StringComparison.OrdinalIgnoreCase)
                && !(allowsDrunkAtheistOverride && string.Equals(offeredId, normalizedChosenId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Offered id '{offeredId}' is not currently valid.");
            }
        }

        if (string.Equals(chosenDefinition.Id, "atheist", StringComparison.OrdinalIgnoreCase)
            && !hiddenFlags.IsDrunk
            && !unchosen.IsAtheistCommitmentConfirmed)
        {
            throw new InvalidOperationException("Atheist requires explicit commitment confirmation unless chosen as Drunk.");
        }

        var storedHiddenFlags = isEvilSentinel ? new HiddenFlags(false, false) : hiddenFlags;
        var newChoice = new PlayerChoice.ChosenChoice(normalizedChosenId, normalizedOfferedIds, storedHiddenFlags);
        var updatedSlot = currentSlot with { Choice = newChoice };
        var updatedSession = ReplaceSlot(session, updatedSlot);
        updatedSession = ApplyAutoAddedRequiredCharacters(updatedSession, chosenDefinition);
        updatedSession = ApplyStorytellerAssignedMinionSlots(updatedSession, chosenDefinition);

        if (GetRemainingSeats(updatedSession) == 0)
        {
            EnsureFinalDemonRequirement(updatedSession);
            updatedSession = updatedSession with { Status = GameStatus.Completed };
        }

        _sessions[updatedSession.Id] = updatedSession;
        return updatedSession;
    }

    public GameSession ResolveMinionSlot(Guid sessionId, int draftOrder, string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            throw new ArgumentException("Resolved character id is required.", nameof(characterId));
        }

        var session = GetSession(sessionId);
        var slot = session.Players.FirstOrDefault(player => player.DraftOrder == draftOrder)
            ?? throw new InvalidOperationException($"Draft slot '{draftOrder}' was not found.");

        if (!slot.IsStAssigned)
        {
            throw new InvalidOperationException("Draft slot is not awaiting minion resolution.");
        }

        var normalizedId = characterId.Trim();
        var resolvedDefinition = session.Script.Characters.FirstOrDefault(c => string.Equals(c.Id, normalizedId, StringComparison.OrdinalIgnoreCase))
            ?? _characterDatabase.Resolve(normalizedId);

        if (resolvedDefinition.Type != CharacterType.Minion)
        {
            throw new InvalidOperationException("Resolved minion assignment must be a Minion.");
        }

        var resolvedSlot = slot with { BorrowedAbilityCharacterId = normalizedId };
        var updatedSession = ReplaceSlot(session, resolvedSlot);
        _sessions[updatedSession.Id] = updatedSession;
        return updatedSession;
    }

    public GameSession ResolveEvilSlot(Guid sessionId, int draftOrder, string actualCharacterId, HiddenFlags hiddenFlags)
    {
        if (string.IsNullOrWhiteSpace(actualCharacterId))
        {
            throw new ArgumentException("Actual character id is required.", nameof(actualCharacterId));
        }

        var session = GetSession(sessionId);
        var slot = session.Players.FirstOrDefault(player => player.DraftOrder == draftOrder)
            ?? throw new InvalidOperationException($"Draft slot '{draftOrder}' was not found.");

        if (slot.Choice is not PlayerChoice.ChosenChoice chosenChoice
            || (!string.Equals(chosenChoice.CharacterId, EvilSentinelCharacterId, StringComparison.OrdinalIgnoreCase)
                && !(chosenChoice.OfferedIds.Count == 1
                    && chosenChoice.OfferedIds.Contains(EvilSentinelCharacterId, StringComparer.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("Draft slot is not awaiting evil resolution.");
        }

        var normalizedId = actualCharacterId.Trim();
        var actualDefinition = session.Script.Characters.FirstOrDefault(c => string.Equals(c.Id, normalizedId, StringComparison.OrdinalIgnoreCase))
            ?? _characterDatabase.Resolve(normalizedId);

        if (actualDefinition.Type != CharacterType.Minion && actualDefinition.Type != CharacterType.Demon)
        {
            throw new InvalidOperationException("Resolved evil assignment must be a Minion or Demon.");
        }

        var resolvedSlot = slot with { Choice = new PlayerChoice.ChosenChoice(normalizedId, chosenChoice.OfferedIds, hiddenFlags) };
        var updatedSession = ReplaceSlot(session, resolvedSlot);
        _sessions[updatedSession.Id] = updatedSession;
        return updatedSession;
    }

    public MakeupSummary GetMakeupSummary(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var current = ResolveSession(session);
        var state = DraftStateSnapshot.FromSession(current);
        var setupResult = _setupCalculator.Calculate(
            current.Script,
            current.PlayerCount,
            state.ChosenCharacterIds,
            current.ActiveLoricIds,
            state.HiddenFlagsByCharacterId,
            new SessionSetupOptions(current.UseMarionette, current.IsLegionGame, current.LegionCount),
            _characterDatabase,
            _loricDatabase);

        return new MakeupSummary(
            DraftMath.ComputeCurrentCounts(current, _characterDatabase),
            setupResult.ValidTargetCounts,
            DraftMath.GroupChosenByUnderlyingType(current, _characterDatabase),
            GetRemainingSeats(current),
            current.Script.OutOfScriptCharacters
                .Select(character => character.Id)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly());
    }

    private static int GetRemainingSeats(GameSession session)
    {
        return session.Players.Count(slot => slot.Choice is not PlayerChoice.ChosenChoice && !slot.IsStAssigned);
    }

    private bool IsAvailableByConstraints(GameSession session, CharacterDefinition candidate, DraftStateSnapshot state)
    {
        if (candidate.AvailabilityConstraints.Count == 0)
        {
            return true;
        }

        var currentSlot = GetCurrentSlot(session);
        var currentUnchosen = currentSlot.Choice as PlayerChoice.UnchosenChoice;

        foreach (var constraint in candidate.AvailabilityConstraints)
        {
            var context = new AvailabilityContext(
                state.ChosenCharacterIds,
                state.HasAnyMinion,
                state.HasAnyDemon,
                state.PicksMade,
                currentUnchosen is not null && currentUnchosen.IsAtheistCommitmentConfirmed,
                false);

            if (!constraint.IsAvailable(context))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHardFeasible(
        CharacterDefinition candidate,
        Script script,
        SetupCounts currentCounts,
        IReadOnlyList<SetupCounts> targetOutcomes,
        DraftStateSnapshot state,
        int remainingSeats,
        HashSet<string> chosenSet)
    {
        if (targetOutcomes.Count == 0)
        {
            return false;
        }

        var pendingRequirements = GetPendingRequiredCharacters(script, chosenSet);
        var isPendingRequiredOutsider = candidate.Type == CharacterType.Outsider
            && pendingRequirements.Any(requiredId => string.Equals(requiredId, candidate.Id, StringComparison.OrdinalIgnoreCase));

        var incremented = Increment(currentCounts, candidate.Type);
        var seatsLeftAfterPick = remainingSeats - 1;

        foreach (var target in targetOutcomes)
        {
            if (incremented.Townsfolk > target.Townsfolk
                || incremented.Minions > target.Minions
                || incremented.Demons > target.Demons)
            {
                continue;
            }

            if (!isPendingRequiredOutsider && incremented.Outsiders > target.Outsiders)
            {
                continue;
            }

            var minimumNeeded = Math.Max(0, target.Townsfolk - incremented.Townsfolk)
                + Math.Max(0, target.Outsiders - incremented.Outsiders)
                + Math.Max(0, target.Minions - incremented.Minions)
                + Math.Max(0, target.Demons - incremented.Demons);

            if (minimumNeeded > seatsLeftAfterPick)
            {
                continue;
            }

            var wouldRealDemons = state.RealDemonsChosen + (candidate.Type == CharacterType.Demon ? 1 : 0);
            if (target.Demons > 0 && seatsLeftAfterPick == 0 && wouldRealDemons == 0)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool SatisfiesPairFeasibility(
        CharacterDefinition candidate,
        Script script,
        HashSet<string> chosenSet,
        SetupCounts currentCounts,
        IReadOnlyList<SetupCounts> targetOutcomes,
        int remainingSeats)
    {
        var chosenAfterPick = new HashSet<string>(chosenSet, StringComparer.OrdinalIgnoreCase)
        {
            candidate.Id,
        };

        var pendingRequirements = GetPendingRequirements(script, chosenAfterPick);
        if (pendingRequirements.Count == 0)
        {
            return true;
        }

        foreach (var requirement in pendingRequirements)
        {
            var isOnScript = script.EffectiveCharacters.Any(character => string.Equals(character.Id, requirement.RequiredId, StringComparison.OrdinalIgnoreCase));
            if (!isOnScript && !requirement.AutoAddIfMissing)
            {
                return false;
            }
        }

        var seatsLeftAfterPick = remainingSeats - 1;
        var distinctPendingRequiredIds = pendingRequirements
            .Select(requirement => requirement.RequiredId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (seatsLeftAfterPick < distinctPendingRequiredIds.Count)
        {
            return false;
        }

        var incremented = Increment(currentCounts, candidate.Type);

        foreach (var target in targetOutcomes)
        {
            var minimumNeeded = Math.Max(0, target.Townsfolk - incremented.Townsfolk)
                + Math.Max(0, target.Outsiders - incremented.Outsiders)
                + Math.Max(0, target.Minions - incremented.Minions)
                + Math.Max(0, target.Demons - incremented.Demons);

            if (minimumNeeded <= seatsLeftAfterPick)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> GetPendingRequiredCharacters(Script script, IReadOnlyCollection<string> chosenIds)
    {
        return GetPendingRequirements(script, chosenIds)
            .Select(requirement => requirement.RequiredId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    private static IReadOnlyList<PendingRequirement> GetPendingRequirements(Script script, IReadOnlyCollection<string> chosenIds)
    {
        var pendingRequirements = new List<PendingRequirement>();

        foreach (var chosenCharacterId in chosenIds)
        {
            var chosenCharacter = script.EffectiveCharacters.FirstOrDefault(character => string.Equals(character.Id, chosenCharacterId, StringComparison.OrdinalIgnoreCase));
            if (chosenCharacter is null)
            {
                continue;
            }

            foreach (var rule in chosenCharacter.SetupRules.OfType<RequiresCharacterSetupRule>())
            {
                if (chosenIds.Contains(rule.RequiredId, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                pendingRequirements.Add(new PendingRequirement(rule.RequiredId, rule.AutoAddIfMissing));
            }
        }

        return pendingRequirements.AsReadOnly();
    }

    private readonly record struct PendingRequirement(string RequiredId, bool AutoAddIfMissing);

    private static SetupCounts Increment(SetupCounts counts, CharacterType type)
    {
        return type switch
        {
            CharacterType.Townsfolk => counts with { Townsfolk = counts.Townsfolk + 1 },
            CharacterType.Outsider => counts with { Outsiders = counts.Outsiders + 1 },
            CharacterType.Minion => counts with { Minions = counts.Minions + 1 },
            CharacterType.Demon => counts with { Demons = counts.Demons + 1 },
            _ => counts,
        };
    }

    private static PlayerSlot GetCurrentSlot(GameSession session)
    {
        var slot = session.Players.FirstOrDefault(p => p.Choice is not PlayerChoice.ChosenChoice && !p.IsStAssigned);

        if (slot is null)
        {
            throw new InvalidOperationException("No pending player slot remains.");
        }

        return slot;
    }

    private static IReadOnlyList<string> NormalizeOfferedIds(IReadOnlyList<string> offeredIds)
    {
        return offeredIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    private static bool AreSameOfferSet(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count == right.Count
            && left.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(right.OrderBy(id => id, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSpecialHiddenCharacter(string characterId)
    {
        return string.Equals(characterId, "drunk", StringComparison.OrdinalIgnoreCase)
            || string.Equals(characterId, "lunatic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(characterId, "marionette", StringComparison.OrdinalIgnoreCase);
    }

    private GameSession ApplyAutoAddedRequiredCharacters(GameSession session, CharacterDefinition chosenDefinition)
    {
        var autoAddRules = chosenDefinition.SetupRules
            .OfType<RequiresCharacterSetupRule>()
            .Where(rule => rule.AutoAddIfMissing)
            .ToList();

        if (autoAddRules.Count == 0)
        {
            return session;
        }

        var updatedCharacters = session.Script.Characters.ToList();

        foreach (var rule in autoAddRules)
        {
            if (updatedCharacters.Any(character => string.Equals(character.Id, rule.RequiredId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var requiredCharacter = _characterDatabase.Resolve(rule.RequiredId) with { IsOutOfScript = true };
            updatedCharacters.Add(requiredCharacter);
        }

        if (updatedCharacters.Count == session.Script.Characters.Count)
        {
            return session;
        }

        var updatedScript = session.Script with { Characters = updatedCharacters.AsReadOnly() };
        return session with { Script = updatedScript };
    }

    private GameSession ApplyStorytellerAssignedMinionSlots(GameSession session, CharacterDefinition chosenDefinition)
    {
        if (!StAssignedMinionCharacterIds.Contains(chosenDefinition.Id, StringComparer.OrdinalIgnoreCase))
        {
            return session;
        }

        var state = DraftStateSnapshot.FromSession(session);
        var setupResult = _setupCalculator.Calculate(
            session.Script,
            session.PlayerCount,
            state.ChosenCharacterIds,
            session.ActiveLoricIds,
            state.HiddenFlagsByCharacterId,
            new SessionSetupOptions(session.UseMarionette, session.IsLegionGame, session.LegionCount),
            _characterDatabase,
            _loricDatabase);

        if (setupResult.ValidTargetCounts.Count == 0)
        {
            return session;
        }

        var currentCounts = DraftMath.ComputeCurrentCounts(session, _characterDatabase);
        var targetMinions = setupResult.ValidTargetCounts.Max(counts => counts.Minions);
        var minionSlotsToAssign = Math.Max(0, targetMinions - currentCounts.Minions);
        if (minionSlotsToAssign == 0)
        {
            return session;
        }

        var slotKeysToAssign = session.Players
            .Where(slot => slot.Choice is not PlayerChoice.ChosenChoice && !slot.IsStAssigned)
            .Take(minionSlotsToAssign)
            .Select(slot => slot.DraftOrder)
            .ToHashSet();

        if (slotKeysToAssign.Count == 0)
        {
            return session;
        }

        var updatedPlayers = session.Players
            .Select(slot => slotKeysToAssign.Contains(slot.DraftOrder)
                ? slot with { IsStAssigned = true, BorrowedAbilityCharacterId = string.Empty }
                : slot)
            .ToList()
            .AsReadOnly();

        return session with { Players = updatedPlayers };
    }

    private void EnsureFinalDemonRequirement(GameSession session)
    {
        var snapshot = DraftStateSnapshot.FromSession(session);
        var setup = _setupCalculator.Calculate(
            session.Script,
            session.PlayerCount,
            snapshot.ChosenCharacterIds,
            session.ActiveLoricIds,
            snapshot.HiddenFlagsByCharacterId,
            new SessionSetupOptions(session.UseMarionette, session.IsLegionGame, session.LegionCount),
            _characterDatabase,
            _loricDatabase);

        var requiresRealDemon = setup.ValidTargetCounts.Any(target => target.Demons > 0);
        if (requiresRealDemon && snapshot.RealDemonsChosen == 0)
        {
            throw new InvalidOperationException("Draft cannot complete without at least one non-Lunatic Demon assignment.");
        }
    }

    private static GameSession ReplaceSlot(GameSession session, PlayerSlot updatedSlot)
    {
        var updatedPlayers = session.Players
            .Select(slot => slot.DraftOrder == updatedSlot.DraftOrder ? updatedSlot : slot)
            .ToList()
            .AsReadOnly();

        return session with { Players = updatedPlayers };
    }

    private GameSession ResolveSession(GameSession session)
    {
        if (_sessions.TryGetValue(session.Id, out var tracked))
        {
            return tracked;
        }

        _sessions[session.Id] = session;
        return session;
    }

    private GameSession GetSession(Guid sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return session;
        }

        throw new KeyNotFoundException($"Session '{sessionId}' was not found.");
    }
}
