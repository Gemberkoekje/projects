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
    private static readonly string[] StAssignedMinionCharacterIds = ["lordoftyphon"];

    private readonly CharacterDatabase _characterDatabase;
    private readonly LoricDatabase _loricDatabase;
    private readonly SetupCalculator _setupCalculator;
    private readonly Random _random;
    private readonly Dictionary<Guid, GameSession> _sessions = new();

    public DraftEngine(CharacterDatabase characterDatabase, LoricDatabase loricDatabase, SetupCalculator setupCalculator)
        : this(characterDatabase, loricDatabase, setupCalculator, Random.Shared)
    {
    }

    public DraftEngine(CharacterDatabase characterDatabase, LoricDatabase loricDatabase, SetupCalculator setupCalculator, Random random)
    {
        _characterDatabase = characterDatabase;
        _loricDatabase = loricDatabase;
        _setupCalculator = setupCalculator;
        _random = random;
    }

    /// <summary>
    /// Creates a new <see cref="GameSession"/> for the given script and player count.
    /// Players are randomly assigned a draft order.
    /// </summary>
    /// <param name="script">The script to use for character selection.</param>
    /// <param name="playerCount">Number of players (5–15).</param>
    /// <param name="activeLoricIds">IDs of Lorics that are active for this session.</param>
    /// <param name="useMarionette">When <see langword="true"/>, applies the Marionette pre-draft adjustment.</param>
    /// <param name="useAtheist">When <see langword="true"/>, applies Atheist setup/draft semantics.</param>
    public GameSession StartSession(
        Script script,
        int playerCount,
        IReadOnlyList<string> activeLoricIds,
        bool useMarionette = false,
        bool useAtheist = false,
        bool isLegionGame = false,
        int legionCount = 0)
    {
        if (playerCount < 5)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount), "Player count must be at least 5.");
        }

        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(activeLoricIds);

        var players = Enumerable.Range(0, playerCount)
            .Select(index => new PlayerSlot(index + 1, Guid.NewGuid(), PlayerChoice.UnchosenChoice.Empty))
            .OrderBy(_ => _random.Next())
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
            useAtheist,
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
        return GetRemainingValidCharacters(session, Array.Empty<SetupCounts>());
    }

    /// <summary>
    /// Returns the characters that remain valid for the current slot, optionally restricted to the
    /// Storyteller's selected target distributions. When <paramref name="selectedTargets"/> is non-empty,
    /// only characters that keep at least one selected target reachable are returned. An empty collection
    /// means "all targets" (no filtering applied).
    /// </summary>
    public IReadOnlyList<CharacterDefinition> GetRemainingValidCharacters(GameSession session, IReadOnlyCollection<SetupCounts> selectedTargets)
    {
        ArgumentNullException.ThrowIfNull(session);

        var current = ResolveSession(session);
        var state = DraftStateSnapshot.FromSession(current);
        var currentCounts = DraftMath.ComputeCurrentCounts(current, _characterDatabase);
        var chosenSet = new HashSet<string>(state.ChosenCharacterIds, StringComparer.OrdinalIgnoreCase);
        var consumedDrunkDisguises = GetConsumedDrunkDisguiseIds(current);
        var remainingSeats = GetRemainingSeats(current);
        var setupResult = _setupCalculator.Calculate(
            current.Script,
            current.PlayerCount,
            state.ChosenCharacterIds,
            current.ActiveLoricIds,
            state.HiddenFlagsByCharacterId,
            new SessionSetupOptions(current.UseMarionette, current.UseAtheist, current.IsLegionGame, current.LegionCount),
            _characterDatabase,
            _loricDatabase);

        var includeEvilSentinel = false;
        if (current.IsLegionGame)
        {
            var legionTarget = setupResult.ValidTargetCounts[0].Demons;
            includeEvilSentinel = currentCounts.Demons < legionTarget;
        }

        var valid = new List<CharacterDefinition>();
        var hasDrunkOnScript = current.Script.Characters.Any(character => string.Equals(character.Id, "drunk", StringComparison.OrdinalIgnoreCase));
        var hasLunaticOnScript = current.Script.Characters.Any(character => string.Equals(character.Id, "lunatic", StringComparison.OrdinalIgnoreCase));

        foreach (var character in current.Script.Characters)
        {
            if (character.IsDraftExcluded)
            {
                continue;
            }

            if (!current.UseAtheist && string.Equals(character.Id, "atheist", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (chosenSet.Contains(character.Id) || consumedDrunkDisguises.Contains(character.Id))
            {
                continue;
            }

            if (IsSpecialHiddenCharacter(character.Id))
            {
                continue;
            }

            if (string.Equals(character.Id, "hermit", StringComparison.OrdinalIgnoreCase))
            {
                if ((hasDrunkOnScript && hasLunaticOnScript) || hasDrunkOnScript || hasLunaticOnScript)
                {
                    continue;
                }
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

            var projectedTargets = FilterToSelectedTargets(projectedSetup.ValidTargetCounts, selectedTargets);
            if (projectedTargets.Count == 0)
            {
                continue;
            }

            if (!IsHardFeasible(character, current.Script, currentCounts, projectedTargets, state, remainingSeats, chosenSet, setupResult.BaseDistribution.Outsiders))
            {
                continue;
            }

            if (!SatisfiesPairFeasibility(character, current.Script, chosenSet, currentCounts, projectedTargets, remainingSeats))
            {
                continue;
            }

            valid.Add(character);
        }

        if (includeEvilSentinel)
        {
            valid.Add(new CharacterDefinition(EvilSentinelCharacterId, "Evil", CharacterType.Demon, Array.Empty<ISetupRule>(), Array.Empty<IAvailabilityConstraint>(), false, false, false));
        }

        return valid
            .OrderBy(c => c.Type)
            .ThenBy(c => c.DisplayName, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<OfferOption> SuggestThree(GameSession session)
    {
        return SuggestThree(session, Array.Empty<SetupCounts>());
    }

    /// <summary>
    /// Suggests up to three offer options for the current slot, optionally restricted to the
    /// Storyteller's selected target distributions. An empty collection means "all targets".
    /// </summary>
    public IReadOnlyList<OfferOption> SuggestThree(GameSession session, IReadOnlyCollection<SetupCounts> selectedTargets)
    {
        ArgumentNullException.ThrowIfNull(session);

        var current = ResolveSession(session);
        var valid = GetRemainingValidCharacters(current, selectedTargets);
        var normalPool = BuildNormalOfferOptions(current, valid);
        var specialPool = BuildSpecialDisguiseOfferOptions(current)
            .Where(option => IsSpecialOfferCompatibleWithSelectedTargets(current, option, selectedTargets))
            .Where(option => IsSpecialOfferFeasible(current, option, selectedTargets))
            .ToList();
        var pool = normalPool.Concat(specialPool).ToList();

        if (pool.Count <= 3)
        {
            return pool.AsReadOnly();
        }

        var selected = new List<OfferOption>();
        var random = _random;
        var orderedTypes = new[] { CharacterType.Townsfolk, CharacterType.Outsider, CharacterType.Minion, CharacterType.Demon };
        var hasLegionEvilFacingOffer = false;

        foreach (var type in orderedTypes)
        {
            var candidates = pool
                .Where(option => ResolveOfferedType(current, option) == type)
                .Where(option => selected.All(existing => !OfferOptionEquals(existing, option)))
                .Where(option => !hasLegionEvilFacingOffer || !IsLegionEvilFacingOption(current, option))
                .ToList();

            if (candidates.Count == 0)
            {
                continue;
            }

            var pick = candidates[random.Next(candidates.Count)];
            selected.Add(pick);
            if (IsLegionEvilFacingOption(current, pick))
            {
                hasLegionEvilFacingOffer = true;
            }

            if (selected.Count == 3)
            {
                return selected.AsReadOnly();
            }
        }

        var fallback = pool
            .Where(option => selected.All(existing => !OfferOptionEquals(existing, option)))
            .Where(option => !hasLegionEvilFacingOffer || !IsLegionEvilFacingOption(current, option))
            .OrderBy(_ => random.Next())
            .ToList();

        foreach (var option in fallback)
        {
            selected.Add(option);
            if (IsLegionEvilFacingOption(current, option))
            {
                hasLegionEvilFacingOffer = true;
            }

            if (selected.Count == 3)
            {
                break;
            }
        }

        return selected.AsReadOnly();
    }

    /// <summary>
    /// Determines whether committing <paramref name="characterId"/> to the current slot would keep at least
    /// one of the Storyteller's selected target distributions reachable. An empty <paramref name="selectedTargets"/>
    /// collection means "all targets", so the warning never fires falsely in the deselected-all state.
    /// This is a read-only projection and does not mutate session state.
    /// </summary>
    public bool WouldPickKeepAnyTargetReachable(GameSession session, string characterId, IReadOnlyCollection<SetupCounts> selectedTargets)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrWhiteSpace(characterId))
        {
            return true;
        }

        // Empty selection means "all targets" - no filtering, so a pick can never strand a selected target.
        if (selectedTargets.Count == 0)
        {
            return true;
        }

        var normalizedId = characterId.Trim();

        // A character that is not even offerable under the full (unfiltered) valid set is not something this
        // warning reasons about (e.g. an already-chosen role or a special disguise); never warn for it.
        var unfilteredValid = GetRemainingValidCharacters(session);
        if (!unfilteredValid.Any(c => string.Equals(c.Id, normalizedId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // The pick keeps a selected target reachable exactly while it remains valid once the selection filter
        // is applied. If it drops out of the filtered set, every selected target would become unachievable.
        var filteredValid = GetRemainingValidCharacters(session, selectedTargets);
        return filteredValid.Any(c => string.Equals(c.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
    }

    public GameSession CreateCuratedOffer(Guid sessionId, int playerSlotIndex, IReadOnlyList<string> offeredIds)
    {
        ArgumentNullException.ThrowIfNull(offeredIds);
        return CreateCuratedOffer(sessionId, playerSlotIndex, offeredIds.Select(OfferOption.Normal).ToList().AsReadOnly());
    }

    public GameSession CreateCuratedOffer(Guid sessionId, int playerSlotIndex, IReadOnlyList<OfferOption> offeredOptions)
    {
        ArgumentNullException.ThrowIfNull(offeredOptions);

        var session = GetSession(sessionId);
        var currentSlot = GetCurrentSlot(session);
        if (currentSlot.DraftOrder != playerSlotIndex)
        {
            throw new InvalidOperationException("Curated offers are only valid for the current draft slot.");
        }

        var normalizedOptions = NormalizeOfferOptions(offeredOptions);
        if (normalizedOptions.Count < 1 || normalizedOptions.Count > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(offeredOptions), "Curated offers must include 1 to 3 unique options.");
        }

        var state = DraftStateSnapshot.FromSession(session);
        var validIds = new HashSet<string>(GetRemainingValidCharacters(session).Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
        var drunkAlreadyChosen = state.ChosenCharacterIds.Contains("drunk", StringComparer.OrdinalIgnoreCase);
        var lunaticAlreadyChosen = state.ChosenCharacterIds.Contains("lunatic", StringComparer.OrdinalIgnoreCase);

        foreach (var option in normalizedOptions)
        {
            ValidateOfferOption(session, option, validIds, state.ChosenCharacterIds, drunkAlreadyChosen, lunaticAlreadyChosen);
        }

        var updatedSlot = currentSlot with { Choice = new PlayerChoice.UnchosenChoice(normalizedOptions) };

        var updated = ReplaceSlot(session, updatedSlot);
        _sessions[updated.Id] = updated;
        return updated;
    }

    public GameSession RecordChoice(Guid sessionId, int playerSlotIndex, string chosenCharacterId, IReadOnlyList<string> offeredIds, HiddenFlags hiddenFlags)
    {
        ArgumentNullException.ThrowIfNull(offeredIds);

        var session = GetSession(sessionId);
        var currentSlot = GetCurrentSlot(session);
        if (currentSlot.DraftOrder != playerSlotIndex)
        {
            throw new InvalidOperationException("Choice can only be recorded for the current draft slot.");
        }

        var existingOptions = (currentSlot.Choice as PlayerChoice.UnchosenChoice)?.OfferedOptions ?? Array.Empty<OfferOption>();
        var projectedOptions = new List<OfferOption>();

        foreach (var offeredId in offeredIds)
        {
            var normalized = offeredId.Trim();
            var matched = existingOptions.FirstOrDefault(option =>
                string.Equals(option.CharacterId, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(GetPresentedCharacterId(option), normalized, StringComparison.OrdinalIgnoreCase));

            projectedOptions.Add(matched ?? OfferOption.Normal(normalized));
        }

        if ((hiddenFlags.IsDrunk || hiddenFlags.IsLunatic)
            && projectedOptions.Count > 0
            && !string.Equals(chosenCharacterId, EvilSentinelCharacterId, StringComparison.OrdinalIgnoreCase))
        {
            for (var index = 0; index < projectedOptions.Count; index++)
            {
                var option = projectedOptions[index];
                if (!string.Equals(option.CharacterId, chosenCharacterId, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(GetPresentedCharacterId(option), chosenCharacterId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                projectedOptions[index] = new OfferOption(option.CharacterId, hiddenFlags, option.DisguiseCharacterId, option.BorrowedAbilityCharacterId);
                break;
            }
        }

        return RecordChoice(sessionId, playerSlotIndex, chosenCharacterId, projectedOptions.AsReadOnly());
    }

    public GameSession RecordChoice(Guid sessionId, int playerSlotIndex, string chosenCharacterId, IReadOnlyList<OfferOption> offeredOptions)
    {
        if (string.IsNullOrWhiteSpace(chosenCharacterId))
        {
            throw new ArgumentException("Chosen character id is required.", nameof(chosenCharacterId));
        }

        ArgumentNullException.ThrowIfNull(offeredOptions);

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

        var normalizedOfferedOptions = NormalizeOfferOptions(offeredOptions);
        if (normalizedOfferedOptions.Count < 1 || normalizedOfferedOptions.Count > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(offeredOptions), "Offered options must contain 1 to 3 unique entries.");
        }

        var selectedOption = normalizedOfferedOptions.FirstOrDefault(option =>
            string.Equals(GetPresentedCharacterId(option), chosenCharacterId.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(option.CharacterId, chosenCharacterId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (selectedOption is null)
        {
            throw new InvalidOperationException("Chosen character must be one of the offered options.");
        }

        var unchosen = currentSlot.Choice as PlayerChoice.UnchosenChoice ?? PlayerChoice.UnchosenChoice.Empty;
        if (unchosen.OfferedOptions.Count > 0 && !AreSameOfferSet(unchosen.OfferedOptions, normalizedOfferedOptions))
        {
            throw new InvalidOperationException("Recorded offered options conflict with curated offer for this slot.");
        }

        var normalizedChosenId = selectedOption.CharacterId;
        var isEvilSentinel = string.Equals(normalizedChosenId, EvilSentinelCharacterId, StringComparison.OrdinalIgnoreCase);
        if (isEvilSentinel && !session.IsLegionGame)
        {
            throw new InvalidOperationException("Evil sentinel can only be used in Legion mode.");
        }

        var chosenDefinition = isEvilSentinel
            ? new CharacterDefinition(EvilSentinelCharacterId, "Evil", CharacterType.Demon, Array.Empty<ISetupRule>(), Array.Empty<IAvailabilityConstraint>(), false, false, false)
            : session.Script.Characters.FirstOrDefault(c => string.Equals(c.Id, normalizedChosenId, StringComparison.OrdinalIgnoreCase))
                ?? _characterDatabase.Resolve(normalizedChosenId);

        var hiddenFlags = isEvilSentinel ? new HiddenFlags(false, false) : selectedOption.HiddenFlags;

        if (!isEvilSentinel && hiddenFlags.IsDrunk && !string.IsNullOrWhiteSpace(selectedOption.DisguiseCharacterId)
            && !string.Equals(chosenDefinition.Id, "drunk", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(chosenDefinition.Id, "hermit", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Drunk-hidden options must resolve to the Drunk or Hermit character.");
        }

        if (!isEvilSentinel && hiddenFlags.IsLunatic && !string.IsNullOrWhiteSpace(selectedOption.DisguiseCharacterId)
            && !string.Equals(chosenDefinition.Id, "lunatic", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(chosenDefinition.Id, "hermit", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Lunatic-hidden options must resolve to the Lunatic or Hermit character.");
        }

        var alreadyHasRealDrunk = session.Players.Any(p => p.Choice is PlayerChoice.ChosenChoice chosen
            && string.Equals(chosen.CharacterId, "drunk", StringComparison.OrdinalIgnoreCase));
        var alreadyHasHermitDrunk = session.Players.Any(p => p.Choice is PlayerChoice.ChosenChoice chosen
            && string.Equals(chosen.CharacterId, "hermit", StringComparison.OrdinalIgnoreCase)
            && chosen.HiddenFlags.IsDrunk);
        if (hiddenFlags.IsDrunk)
        {
            if (string.Equals(chosenDefinition.Id, "drunk", StringComparison.OrdinalIgnoreCase) && alreadyHasRealDrunk)
            {
                throw new InvalidOperationException("Drunk is already chosen in this game.");
            }

            if (string.Equals(chosenDefinition.Id, "hermit", StringComparison.OrdinalIgnoreCase) && alreadyHasHermitDrunk)
            {
                throw new InvalidOperationException("Hermit already counts as Drunk in this game.");
            }
        }

        var alreadyHasRealLunatic = session.Players.Any(p => p.Choice is PlayerChoice.ChosenChoice chosen
            && string.Equals(chosen.CharacterId, "lunatic", StringComparison.OrdinalIgnoreCase));
        var alreadyHasHermitLunatic = session.Players.Any(p => p.Choice is PlayerChoice.ChosenChoice chosen
            && string.Equals(chosen.CharacterId, "hermit", StringComparison.OrdinalIgnoreCase)
            && chosen.HiddenFlags.IsLunatic);
        if (hiddenFlags.IsLunatic)
        {
            if (string.Equals(chosenDefinition.Id, "lunatic", StringComparison.OrdinalIgnoreCase) && alreadyHasRealLunatic)
            {
                throw new InvalidOperationException("Lunatic is already chosen in this game.");
            }

            if (string.Equals(chosenDefinition.Id, "hermit", StringComparison.OrdinalIgnoreCase) && alreadyHasHermitLunatic)
            {
                throw new InvalidOperationException("Hermit already counts as Lunatic in this game.");
            }
        }

        if (hiddenFlags.IsDrunk)
        {
            if (!string.IsNullOrWhiteSpace(selectedOption.DisguiseCharacterId))
            {
                var disguiseDefinition = ResolveDef(session, selectedOption.DisguiseCharacterId);
                if (disguiseDefinition.Type != CharacterType.Townsfolk)
                {
                    throw new InvalidOperationException("Drunk option disguise must be a Townsfolk character.");
                }
            }
            else if (chosenDefinition.Type != CharacterType.Townsfolk)
            {
                throw new InvalidOperationException("Drunk can only be applied to a Townsfolk character.");
            }
        }

        if (hiddenFlags.IsLunatic)
        {
            if (!string.IsNullOrWhiteSpace(selectedOption.DisguiseCharacterId))
            {
                var disguiseDefinition = ResolveDef(session, selectedOption.DisguiseCharacterId);
                if (disguiseDefinition.Type != CharacterType.Demon)
                {
                    throw new InvalidOperationException("Lunatic option disguise must be a Demon character.");
                }
            }
            else if (chosenDefinition.Type != CharacterType.Demon)
            {
                throw new InvalidOperationException("Lunatic can only be applied to a Demon character.");
            }
        }

        var validIds = new HashSet<string>(GetRemainingValidCharacters(session).Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
        var isHiddenSpecialSelection = (hiddenFlags.IsDrunk || hiddenFlags.IsLunatic)
            && (string.Equals(normalizedChosenId, "drunk", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedChosenId, "lunatic", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedChosenId, "hermit", StringComparison.OrdinalIgnoreCase));
        var allowsDrunkAtheistOverride = string.Equals(chosenDefinition.Id, "atheist", StringComparison.OrdinalIgnoreCase)
            && hiddenFlags.IsDrunk;
        if (!isHiddenSpecialSelection && !validIds.Contains(normalizedChosenId) && !isEvilSentinel && !allowsDrunkAtheistOverride)
        {
            throw new InvalidOperationException("Chosen character is not currently valid.");
        }


        var newChoice = new PlayerChoice.ChosenChoice(normalizedChosenId, normalizedOfferedOptions, hiddenFlags, selectedOption.DisguiseCharacterId);
        var updatedSlot = currentSlot with
        {
            Choice = newChoice,
            BorrowedAbilityCharacterId = string.IsNullOrWhiteSpace(selectedOption.BorrowedAbilityCharacterId)
                ? currentSlot.BorrowedAbilityCharacterId
                : selectedOption.BorrowedAbilityCharacterId,
        };
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
                && !(chosenChoice.OfferedOptions.Count == 1
                    && chosenChoice.OfferedOptions.Any(option => string.Equals(option.CharacterId, EvilSentinelCharacterId, StringComparison.OrdinalIgnoreCase)))))
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

        var resolvedSlot = slot with { Choice = new PlayerChoice.ChosenChoice(normalizedId, chosenChoice.OfferedOptions, hiddenFlags, string.Empty) };
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
            _loricDatabase,
            state.BorrowedAbilityCharacterIds,
            BuildFeasibilityContext(current, state));

        // A Storyteller setup confirmation is needed when a dynamic-setup character is chosen
        // but its borrowed ability has not yet been assigned.
        var requiresConfirmation = current.Players.Any(slot =>
            slot.Choice is PlayerChoice.ChosenChoice chosen
            && string.IsNullOrEmpty(slot.BorrowedAbilityCharacterId)
            && ResolveDef(current, chosen.CharacterId).IsDynamicSetup);

        return new MakeupSummary(
            DraftMath.ComputeCurrentCounts(current, _characterDatabase),
            setupResult.ValidTargetCounts,
            DraftMath.GroupChosenByUnderlyingType(current, _characterDatabase),
            GetRemainingSeats(current),
            current.Script.OutOfScriptCharacters
                .Select(character => character.Id)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly(),
            requiresConfirmation);
    }

    private CharacterDefinition ResolveDef(GameSession session, string characterId)
    {
        return session.Script.Characters.FirstOrDefault(c => string.Equals(c.Id, characterId, StringComparison.OrdinalIgnoreCase))
            ?? _characterDatabase.Resolve(characterId);
    }

    /// <summary>
    /// Returns the list of Minion abilities on the script that an Alchemist at <paramref name="draftOrder"/> may borrow.
    /// </summary>
    public IReadOnlyList<AbilityOption> GetAlchemistAbilityOptions(Guid sessionId, int draftOrder)
    {
        return GetDynamicAbilityOptions(sessionId, draftOrder, DynamicAbilityScope.NotInPlayMinion);
    }

    /// <summary>
    /// Returns the list of Townsfolk/Outsider abilities on the script that a Boffin at <paramref name="draftOrder"/> may borrow.
    /// </summary>
    public IReadOnlyList<AbilityOption> GetBoffinAbilityOptions(Guid sessionId, int draftOrder)
    {
        return GetDynamicAbilityOptions(sessionId, draftOrder, DynamicAbilityScope.NotInPlayTownsfolkOrOutsider);
    }

    private IReadOnlyList<AbilityOption> GetDynamicAbilityOptions(Guid sessionId, int draftOrder, DynamicAbilityScope scope)
    {
        var session = GetSession(sessionId);
        var slot = session.Players.FirstOrDefault(p => p.DraftOrder == draftOrder)
            ?? throw new InvalidOperationException($"Draft slot '{draftOrder}' was not found.");

        if (slot.Choice is not PlayerChoice.ChosenChoice chosen)
        {
            throw new InvalidOperationException("Draft slot does not have a chosen character.");
        }

        var slotDef = ResolveDef(session, chosen.CharacterId);
        if (!slotDef.IsDynamicSetup || slotDef.DynamicAbilityScope != scope)
        {
            throw new InvalidOperationException($"Draft slot character is not a dynamic-setup character with scope {scope}.");
        }

        var state = DraftStateSnapshot.FromSession(session);
        var chosenSet = new HashSet<string>(state.ChosenCharacterIds, StringComparer.OrdinalIgnoreCase);
        var borrowedSet = new HashSet<string>(state.BorrowedAbilityCharacterIds, StringComparer.OrdinalIgnoreCase);

        IEnumerable<CharacterDefinition> candidates = scope switch
        {
            DynamicAbilityScope.NotInPlayMinion =>
                session.Script.Characters.Where(c => c.Type == CharacterType.Minion),
            DynamicAbilityScope.NotInPlayTownsfolkOrOutsider =>
                session.Script.Characters.Where(c => c.Type == CharacterType.Townsfolk || c.Type == CharacterType.Outsider),
            _ => throw new InvalidOperationException("Unknown dynamic ability scope.")
        };

        // Alchemist may borrow minion abilities even if that minion is already chosen.
        // Boffin abilities must remain not-in-play.
        candidates = scope == DynamicAbilityScope.NotInPlayMinion
            ? candidates.Where(c => !borrowedSet.Contains(c.Id))
            : candidates
                .Where(c => !chosenSet.Contains(c.Id))
                .Where(c => !borrowedSet.Contains(c.Id));

        var currentCounts = DraftMath.ComputeCurrentCounts(session, _characterDatabase);
        var remainingSeats = GetRemainingSeats(session);
        var options = new List<AbilityOption>();

        foreach (var candidate in candidates)
        {
            // Validate RequiresCharacter constraints before count-affecting rule check
            var requiresCharRules = candidate.SetupRules.OfType<RequiresCharacterSetupRule>().ToList();
            string? unavailableReason = null;

            foreach (var rule in requiresCharRules)
            {
                var requiredId = rule.RequiredId;
                if (!session.Script.Characters.Any(c => string.Equals(c.Id, requiredId, StringComparison.OrdinalIgnoreCase)))
                {
                    var requiredDef = _characterDatabase.Resolve(requiredId);
                    unavailableReason = $"{requiredDef.DisplayName} is not on the script.";
                    break;
                }

                if (chosenSet.Contains(requiredId))
                {
                    var requiredDef = _characterDatabase.Resolve(requiredId);
                    unavailableReason = $"{requiredDef.DisplayName} is already chosen, cannot satisfy required-pair.";
                    break;
                }
            }

            if (unavailableReason is not null)
            {
                options.Add(new AbilityOption(candidate.Id, candidate.DisplayName, false, unavailableReason));
                continue;
            }

            // If there are no count-affecting rules the ability is freely assignable
            var countAffectingRules = candidate.SetupRules
                .Where(r => r is not RequiresCharacterSetupRule)
                .ToList();

            if (countAffectingRules.Count == 0)
            {
                options.Add(new AbilityOption(candidate.Id, candidate.DisplayName, true, string.Empty));
                continue;
            }

            // Speculative feasibility check
            var speculativeBorrowedIds = state.BorrowedAbilityCharacterIds
                .Append(candidate.Id)
                .ToList()
                .AsReadOnly();

            var speculativeResult = _setupCalculator.Calculate(
                session.Script,
                session.PlayerCount,
                state.ChosenCharacterIds,
                session.ActiveLoricIds,
                state.HiddenFlagsByCharacterId,
                new SessionSetupOptions(session.UseMarionette, session.IsLegionGame, session.LegionCount),
                _characterDatabase,
                _loricDatabase,
                speculativeBorrowedIds);

            if (IsSetupFeasible(speculativeResult.ValidTargetCounts, currentCounts, remainingSeats, session, chosenSet, borrowedSet))
            {
                options.Add(new AbilityOption(candidate.Id, candidate.DisplayName, true, string.Empty));
            }
            else
            {
                var reason = GenerateUnavailableReason(candidate, remainingSeats, session, chosenSet);
                options.Add(new AbilityOption(candidate.Id, candidate.DisplayName, false, reason));
            }
        }

        return options
            .OrderBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Assigns a borrowed ability to a dynamic-setup character (Alchemist / Boffin).
    /// </summary>
    public GameSession AssignDynamicAbility(Guid sessionId, int draftOrder, string abilityCharacterId)
    {
        if (string.IsNullOrWhiteSpace(abilityCharacterId))
        {
            throw new ArgumentException("Ability character id is required.", nameof(abilityCharacterId));
        }

        var session = GetSession(sessionId);
        var slot = session.Players.FirstOrDefault(p => p.DraftOrder == draftOrder)
            ?? throw new InvalidOperationException($"Draft slot '{draftOrder}' was not found.");

        if (slot.Choice is not PlayerChoice.ChosenChoice chosen)
        {
            throw new InvalidOperationException("Draft slot does not have a chosen character.");
        }

        var slotDef = ResolveDef(session, chosen.CharacterId);
        if (!slotDef.IsDynamicSetup)
        {
            throw new InvalidOperationException("Draft slot character is not a dynamic-setup character.");
        }

        var normalizedId = abilityCharacterId.Trim();
        var candidateDef = session.Script.Characters.FirstOrDefault(c => string.Equals(c.Id, normalizedId, StringComparison.OrdinalIgnoreCase))
            ?? _characterDatabase.Resolve(normalizedId);

        // Validate scope
        CharacterType[] expectedTypes = slotDef.DynamicAbilityScope switch
        {
            DynamicAbilityScope.NotInPlayMinion => [CharacterType.Minion],
            DynamicAbilityScope.NotInPlayTownsfolkOrOutsider => [CharacterType.Townsfolk, CharacterType.Outsider],
            _ => throw new InvalidOperationException("Character has unknown dynamic ability scope.")
        };

        if (!expectedTypes.Contains(candidateDef.Type))
        {
            throw new InvalidOperationException($"Ability character '{normalizedId}' does not match the expected scope for {slotDef.DisplayName}.");
        }

        if (!session.Script.Characters.Any(c => string.Equals(c.Id, normalizedId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Character '{normalizedId}' is not on the script.");
        }

        var state = DraftStateSnapshot.FromSession(session);
        var chosenSet = new HashSet<string>(state.ChosenCharacterIds, StringComparer.OrdinalIgnoreCase);
        var borrowedSet = new HashSet<string>(state.BorrowedAbilityCharacterIds, StringComparer.OrdinalIgnoreCase);

        if (slotDef.DynamicAbilityScope != DynamicAbilityScope.NotInPlayMinion
            && chosenSet.Contains(normalizedId))
        {
            throw new InvalidOperationException($"Character '{normalizedId}' is already chosen.");
        }

        if (borrowedSet.Contains(normalizedId))
        {
            throw new InvalidOperationException($"Character '{normalizedId}' is already borrowed by another character.");
        }

        // Re-validate feasibility for count-affecting abilities (stale-state rejection)
        var countAffectingRules = candidateDef.SetupRules.Where(r => r is not RequiresCharacterSetupRule).ToList();
        if (countAffectingRules.Count > 0)
        {
            var speculativeBorrowedIds = state.BorrowedAbilityCharacterIds
                .Append(normalizedId)
                .ToList()
                .AsReadOnly();

            var speculativeResult = _setupCalculator.Calculate(
                session.Script,
                session.PlayerCount,
                state.ChosenCharacterIds,
                session.ActiveLoricIds,
                state.HiddenFlagsByCharacterId,
                new SessionSetupOptions(session.UseMarionette, session.IsLegionGame, session.LegionCount),
                _characterDatabase,
                _loricDatabase,
                speculativeBorrowedIds);

            var currentCounts = DraftMath.ComputeCurrentCounts(session, _characterDatabase);
            var remainingSeats = GetRemainingSeats(session);

            if (!IsSetupFeasible(speculativeResult.ValidTargetCounts, currentCounts, remainingSeats, session, chosenSet, borrowedSet))
            {
                throw new InvalidOperationException($"Assigning ability '{normalizedId}' would make the setup infeasible.");
            }
        }

        var updatedSlot = slot with { BorrowedAbilityCharacterId = normalizedId };
        var updatedSession = ReplaceSlot(session, updatedSlot);
        _sessions[updatedSession.Id] = updatedSession;
        return updatedSession;
    }

    private static bool IsSetupFeasible(
        IReadOnlyList<SetupCounts> targets,
        SetupCounts currentCounts,
        int remainingSeats,
        GameSession session,
        HashSet<string> chosenSet,
        HashSet<string> borrowedSet)
    {
        var context = BuildFeasibilityContext(currentCounts, remainingSeats, session, chosenSet, borrowedSet);
        return targets.Any(context.IsReachable);
    }

    private static SetupFeasibilityContext BuildFeasibilityContext(
        SetupCounts currentCounts,
        int remainingSeats,
        GameSession session,
        HashSet<string> chosenSet,
        HashSet<string> borrowedSet)
    {
        var availableTF = session.Script.Characters.Count(c => c.Type == CharacterType.Townsfolk && !chosenSet.Contains(c.Id) && !borrowedSet.Contains(c.Id));
        var availableOut = session.Script.Characters.Count(c => c.Type == CharacterType.Outsider && !chosenSet.Contains(c.Id) && !borrowedSet.Contains(c.Id));
        var availableMin = session.Script.Characters.Count(c => c.Type == CharacterType.Minion && !chosenSet.Contains(c.Id) && !borrowedSet.Contains(c.Id));
        var availableDem = session.Script.Characters.Count(c => c.Type == CharacterType.Demon && !chosenSet.Contains(c.Id) && !borrowedSet.Contains(c.Id));

        return new SetupFeasibilityContext(
            true,
            currentCounts,
            remainingSeats,
            availableTF,
            availableOut,
            availableMin,
            availableDem);
    }

    /// <summary>
    /// Builds an enforcing <see cref="SetupFeasibilityContext"/> from the current draft state so the
    /// setup calculator only emits target distributions still completable by a legal draft.
    /// </summary>
    private SetupFeasibilityContext BuildFeasibilityContext(GameSession session, DraftStateSnapshot state)
    {
        var currentCounts = DraftMath.ComputeCurrentCounts(session, _characterDatabase);
        var remainingSeats = GetRemainingSeats(session);
        var chosenSet = new HashSet<string>(state.ChosenCharacterIds, StringComparer.OrdinalIgnoreCase);
        var borrowedSet = new HashSet<string>(state.BorrowedAbilityCharacterIds, StringComparer.OrdinalIgnoreCase);

        return BuildFeasibilityContext(currentCounts, remainingSeats, session, chosenSet, borrowedSet);
    }

    private string GetRandomBorrowedAbilityCharacterId(GameSession session, DraftStateSnapshot state, CharacterDefinition character)
    {
        var availableAbilityIds = GetAvailableBorrowedAbilityIds(session, state, character);
        if (availableAbilityIds.Count == 0)
        {
            return string.Empty;
        }

        return availableAbilityIds[_random.Next(availableAbilityIds.Count)];
    }

    private IReadOnlyList<string> GetAvailableBorrowedAbilityIds(GameSession session, DraftStateSnapshot state, CharacterDefinition character)
    {
        if (!character.IsDynamicSetup)
        {
            return Array.Empty<string>();
        }

        var chosenSet = new HashSet<string>(state.ChosenCharacterIds, StringComparer.OrdinalIgnoreCase);
        var borrowedSet = new HashSet<string>(state.BorrowedAbilityCharacterIds, StringComparer.OrdinalIgnoreCase);

        IEnumerable<CharacterDefinition> candidates = character.DynamicAbilityScope switch
        {
            DynamicAbilityScope.NotInPlayMinion => session.Script.Characters.Where(c => c.Type == CharacterType.Minion),
            DynamicAbilityScope.NotInPlayTownsfolkOrOutsider => session.Script.Characters.Where(c => c.Type == CharacterType.Townsfolk || c.Type == CharacterType.Outsider),
            _ => Array.Empty<CharacterDefinition>(),
        };

        candidates = character.DynamicAbilityScope == DynamicAbilityScope.NotInPlayMinion
            ? candidates.Where(c => !borrowedSet.Contains(c.Id))
            : candidates.Where(c => !chosenSet.Contains(c.Id)).Where(c => !borrowedSet.Contains(c.Id));

        var currentCounts = DraftMath.ComputeCurrentCounts(session, _characterDatabase);
        var remainingSeats = GetRemainingSeats(session);
        var availableIds = new List<string>();

        foreach (var candidate in candidates)
        {
            var validationError = GetBorrowedAbilityOfferValidationError(session, character.Id, candidate.Id);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                continue;
            }

            var countAffectingRules = candidate.SetupRules.Where(r => r is not RequiresCharacterSetupRule).ToList();
            if (countAffectingRules.Count > 0)
            {
                var speculativeBorrowedIds = state.BorrowedAbilityCharacterIds
                    .Append(candidate.Id)
                    .ToList()
                    .AsReadOnly();

                var speculativeResult = _setupCalculator.Calculate(
                    session.Script,
                    session.PlayerCount,
                    state.ChosenCharacterIds,
                    session.ActiveLoricIds,
                    state.HiddenFlagsByCharacterId,
                    new SessionSetupOptions(session.UseMarionette, session.IsLegionGame, session.LegionCount),
                    _characterDatabase,
                    _loricDatabase,
                    speculativeBorrowedIds);

                if (!IsSetupFeasible(speculativeResult.ValidTargetCounts, currentCounts, remainingSeats, session, chosenSet, borrowedSet))
                {
                    continue;
                }
            }

            availableIds.Add(candidate.Id);
        }

        return availableIds
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    private void ValidateBorrowedAbilityOffer(GameSession session, string roleCharacterId, string borrowedAbilityCharacterId)
    {
        var validationError = GetBorrowedAbilityOfferValidationError(session, roleCharacterId, borrowedAbilityCharacterId);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            throw new InvalidOperationException(validationError);
        }
    }

    private string GetBorrowedAbilityOfferValidationError(GameSession session, string roleCharacterId, string borrowedAbilityCharacterId)
    {
        if (string.IsNullOrWhiteSpace(borrowedAbilityCharacterId))
        {
            return string.Empty;
        }

        var roleDefinition = ResolveDef(session, roleCharacterId);
        if (!roleDefinition.IsDynamicSetup)
        {
            return "Only dynamic-setup roles can specify a borrowed ability in an offer.";
        }

        var borrowedDefinition = ResolveDef(session, borrowedAbilityCharacterId);
        var expectedTypes = roleDefinition.DynamicAbilityScope switch
        {
            DynamicAbilityScope.NotInPlayMinion => new[] { CharacterType.Minion },
            DynamicAbilityScope.NotInPlayTownsfolkOrOutsider => new[] { CharacterType.Townsfolk, CharacterType.Outsider },
            _ => Array.Empty<CharacterType>(),
        };

        if (!expectedTypes.Contains(borrowedDefinition.Type))
        {
            return $"Ability character '{borrowedAbilityCharacterId}' does not match the expected scope for {roleDefinition.DisplayName}.";
        }

        if (!session.Script.Characters.Any(c => string.Equals(c.Id, borrowedAbilityCharacterId, StringComparison.OrdinalIgnoreCase)))
        {
            return $"Character '{borrowedAbilityCharacterId}' is not on the script.";
        }

        var state = DraftStateSnapshot.FromSession(session);
        var chosenSet = new HashSet<string>(state.ChosenCharacterIds, StringComparer.OrdinalIgnoreCase);
        var borrowedSet = new HashSet<string>(state.BorrowedAbilityCharacterIds, StringComparer.OrdinalIgnoreCase);

        if (roleDefinition.DynamicAbilityScope != DynamicAbilityScope.NotInPlayMinion
            && chosenSet.Contains(borrowedAbilityCharacterId))
        {
            return $"Character '{borrowedAbilityCharacterId}' is already chosen.";
        }

        if (borrowedSet.Contains(borrowedAbilityCharacterId))
        {
            return $"Character '{borrowedAbilityCharacterId}' is already borrowed by another character.";
        }

        var requiresCharRules = borrowedDefinition.SetupRules.OfType<RequiresCharacterSetupRule>().ToList();
        foreach (var rule in requiresCharRules)
        {
            if (!session.Script.Characters.Any(c => string.Equals(c.Id, rule.RequiredId, StringComparison.OrdinalIgnoreCase)))
            {
                var requiredDef = _characterDatabase.Resolve(rule.RequiredId);
                return $"{requiredDef.DisplayName} is not on the script.";
            }

            if (chosenSet.Contains(rule.RequiredId))
            {
                var requiredDef = _characterDatabase.Resolve(rule.RequiredId);
                return $"{requiredDef.DisplayName} is already chosen, cannot satisfy required-pair.";
            }
        }

        return string.Empty;
    }

    private IReadOnlyList<OfferOption> BuildNormalOfferOptions(GameSession session, IReadOnlyList<CharacterDefinition> validCharacters)
    {
        var state = DraftStateSnapshot.FromSession(session);
        var options = new List<OfferOption>(validCharacters.Count);

        foreach (var character in validCharacters)
        {
            if (!character.IsDynamicSetup)
            {
                options.Add(OfferOption.Normal(character.Id));
                continue;
            }

            var borrowedAbilityCharacterId = GetRandomBorrowedAbilityCharacterId(session, state, character);
            options.Add(new OfferOption(character.Id, new HiddenFlags(false, false), string.Empty, borrowedAbilityCharacterId));
        }

        return options.AsReadOnly();
    }

    private IReadOnlyList<OfferOption> BuildSpecialDisguiseOfferOptions(GameSession session)
    {
        var state = DraftStateSnapshot.FromSession(session);
        var chosenSet = new HashSet<string>(state.ChosenCharacterIds, StringComparer.OrdinalIgnoreCase);
        var consumedDrunkDisguises = GetConsumedDrunkDisguiseIds(session);
        var options = new List<OfferOption>();

        var hasDrunkOnScript = session.Script.Characters.Any(character => string.Equals(character.Id, "drunk", StringComparison.OrdinalIgnoreCase));
        var hasLunaticOnScript = session.Script.Characters.Any(character => string.Equals(character.Id, "lunatic", StringComparison.OrdinalIgnoreCase));
        var hasHermitOnScript = session.Script.Characters.Any(character => string.Equals(character.Id, "hermit", StringComparison.OrdinalIgnoreCase));

        var realDrunkAlreadyChosen = session.Players.Any(slot =>
            slot.Choice is PlayerChoice.ChosenChoice chosen
            && string.Equals(chosen.CharacterId, "drunk", StringComparison.OrdinalIgnoreCase));

        if (hasDrunkOnScript && !realDrunkAlreadyChosen)
        {
            var disguises = GetAvailableDrunkDisguiseIds(session, chosenSet, consumedDrunkDisguises);
            options.AddRange(disguises.Select(disguiseId => new OfferOption("drunk", new HiddenFlags(true, false), disguiseId, string.Empty)));
        }

        var realLunaticAlreadyChosen = session.Players.Any(slot =>
            slot.Choice is PlayerChoice.ChosenChoice chosen
            && string.Equals(chosen.CharacterId, "lunatic", StringComparison.OrdinalIgnoreCase));

        if (hasLunaticOnScript && !realLunaticAlreadyChosen)
        {
            var disguises = GetAvailableLunaticDisguiseIds(session, chosenSet);
            options.AddRange(disguises.Select(disguiseId => new OfferOption("lunatic", new HiddenFlags(false, true), disguiseId, string.Empty)));
        }

        var hermitAlreadyChosen = session.Players.Any(slot =>
            slot.Choice is PlayerChoice.ChosenChoice chosen
            && string.Equals(chosen.CharacterId, "hermit", StringComparison.OrdinalIgnoreCase));

        if (hasHermitOnScript && !hermitAlreadyChosen)
        {
            if (hasDrunkOnScript && !hasLunaticOnScript)
            {
                var disguises = GetAvailableDrunkDisguiseIds(session, chosenSet, consumedDrunkDisguises);
                options.AddRange(disguises.Select(disguiseId => new OfferOption("hermit", new HiddenFlags(true, false), disguiseId, string.Empty)));
            }
            else if (hasLunaticOnScript && !hasDrunkOnScript)
            {
                var disguises = GetAvailableLunaticDisguiseIds(session, chosenSet);
                options.AddRange(disguises.Select(disguiseId => new OfferOption("hermit", new HiddenFlags(false, true), disguiseId, string.Empty)));
            }
        }

        return options
            .Distinct()
            .OrderBy(option => option.DisguiseCharacterId, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    private static CharacterType ResolveOfferedType(GameSession session, OfferOption option)
    {
        if (IsLegionEvilFacingOption(session, option))
        {
            return CharacterType.Demon;
        }

        // Drunk and lunatic hidden options both count as Outsiders in the draft type ordering.
        if (option.HiddenFlags.IsDrunk || option.HiddenFlags.IsLunatic)
        {
            return CharacterType.Outsider;
        }

        var presentedCharacterId = GetPresentedCharacterId(option);
        var definition = session.Script.Characters.FirstOrDefault(character =>
                string.Equals(character.Id, presentedCharacterId, StringComparison.OrdinalIgnoreCase))
            ?? session.Script.Characters.FirstOrDefault(character =>
                string.Equals(character.Id, option.CharacterId, StringComparison.OrdinalIgnoreCase));

        return definition?.Type ?? CharacterType.Unknown;
    }

    private static bool IsLegionEvilFacingOption(GameSession session, OfferOption option)
    {
        if (!session.IsLegionGame)
        {
            return false;
        }

        if (string.Equals(option.CharacterId, EvilSentinelCharacterId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return option.HiddenFlags.IsLunatic;
    }

    private static bool OfferOptionEquals(OfferOption left, OfferOption right)
    {
        return string.Equals(left.CharacterId, right.CharacterId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.DisguiseCharacterId, right.DisguiseCharacterId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.BorrowedAbilityCharacterId, right.BorrowedAbilityCharacterId, StringComparison.OrdinalIgnoreCase)
            && left.HiddenFlags == right.HiddenFlags;
    }

    private bool IsSpecialOfferCompatibleWithSelectedTargets(
        GameSession session,
        OfferOption option,
        IReadOnlyCollection<SetupCounts> selectedTargets)
    {
        if (selectedTargets.Count == 0)
        {
            return true;
        }

        if (!option.HiddenFlags.IsDrunk && !option.HiddenFlags.IsLunatic)
        {
            return true;
        }

        var hiddenType = option.HiddenFlags.IsLunatic ? CharacterType.Outsider : CharacterType.Outsider;
        return selectedTargets.Any(target =>
            hiddenType switch
            {
                CharacterType.Outsider => target.Outsiders > 0,
                _ => true,
            });
    }

    private bool IsSpecialOfferFeasible(
        GameSession session,
        OfferOption option,
        IReadOnlyCollection<SetupCounts> selectedTargets)
    {
        // Only hidden-outsider options need the extra feasibility gate.
        if (!option.HiddenFlags.IsDrunk && !option.HiddenFlags.IsLunatic)
        {
            return true;
        }

        var state = DraftStateSnapshot.FromSession(session);
        var currentCounts = DraftMath.ComputeCurrentCounts(session, _characterDatabase);
        var remainingSeats = GetRemainingSeats(session);
        var chosenSet = new HashSet<string>(state.ChosenCharacterIds, StringComparer.OrdinalIgnoreCase);

        var setupResult = _setupCalculator.Calculate(
            session.Script,
            session.PlayerCount,
            state.ChosenCharacterIds,
            session.ActiveLoricIds,
            state.HiddenFlagsByCharacterId,
            new SessionSetupOptions(session.UseMarionette, session.UseAtheist, session.IsLegionGame, session.LegionCount),
            _characterDatabase,
            _loricDatabase);

        var targets = FilterToSelectedTargets(setupResult.ValidTargetCounts, selectedTargets);
        if (targets.Count == 0)
        {
            return false;
        }

        // Treat the hidden option as adding an Outsider to the current counts.
        var outsiderCharacter = new CharacterDefinition(
            option.CharacterId,
            option.CharacterId,
            CharacterType.Outsider,
            Array.Empty<ISetupRule>(),
            Array.Empty<IAvailabilityConstraint>(),
            false,
            false,
            false);

        return IsHardFeasible(outsiderCharacter, session.Script, currentCounts, targets, state, remainingSeats, chosenSet, setupResult.BaseDistribution.Outsiders);
    }

    private IReadOnlySet<string> GetConsumedDrunkDisguiseIds(GameSession session)
    {
        return session.Players
            .Select(slot => slot.Choice)
            .OfType<PlayerChoice.ChosenChoice>()
            .Where(chosen => chosen.HiddenFlags.IsDrunk && !string.IsNullOrWhiteSpace(chosen.DisguiseCharacterId))
            .Select(chosen => chosen.DisguiseCharacterId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetAvailableDrunkDisguiseIds(
        GameSession session,
        IReadOnlySet<string> chosenSet,
        IReadOnlySet<string> consumedDrunkDisguises)
    {
        return session.Script.Characters
            .Where(character => character.Type == CharacterType.Townsfolk)
            .Where(character => !chosenSet.Contains(character.Id))
            .Where(character => !consumedDrunkDisguises.Contains(character.Id))
            .Select(character => character.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    private static IReadOnlyList<string> GetAvailableLunaticDisguiseIds(GameSession session, IReadOnlySet<string> chosenSet)
    {
        return session.Script.Characters
            .Where(character => character.Type == CharacterType.Demon)
            .Where(character => !chosenSet.Contains(character.Id))
            .Select(character => character.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    private string GenerateUnavailableReason(
        CharacterDefinition candidate,
        int remainingSeats,
        GameSession session,
        HashSet<string> chosenSet)
    {
        foreach (var rule in candidate.SetupRules)
        {
            if (rule is OutsiderDeltaSetupRule deltaRule && !deltaRule.IsStorytellerChoice && deltaRule.Delta > 0)
            {
                var availableOutsiders = session.Script.Characters.Count(c => c.Type == CharacterType.Outsider && !chosenSet.Contains(c.Id));
                if (availableOutsiders < deltaRule.Delta)
                {
                    return $"Not enough Outsiders remaining on the script to satisfy +{deltaRule.Delta} Outsider count.";
                }

                if (remainingSeats < deltaRule.Delta)
                {
                    return $"Not enough remaining seats to add {deltaRule.Delta} Outsiders.";
                }
            }

            if (rule is StoryTellerChoiceSetupRule)
            {
                return "No Outsider can be added or removed to satisfy ±1.";
            }
        }

        return "Resulting counts cannot be satisfied by the remaining script.";
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
                false,
                false);

            if (!constraint.IsAvailable(context))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<SetupCounts> FilterToSelectedTargets(
        IReadOnlyList<SetupCounts> targets,
        IReadOnlyCollection<SetupCounts> selectedTargets)
    {
        // An empty selection means "all targets" — no filtering applied.
        if (selectedTargets.Count == 0)
        {
            return targets;
        }

        var selectedSet = selectedTargets as ISet<SetupCounts> ?? selectedTargets.ToHashSet();
        return targets.Where(selectedSet.Contains).ToList().AsReadOnly();
    }

    private static bool IsHardFeasible(
        CharacterDefinition candidate,
        Script script,
        SetupCounts currentCounts,
        IReadOnlyList<SetupCounts> targetOutcomes,
        DraftStateSnapshot state,
        int remainingSeats,
        HashSet<string> chosenSet,
        int baseOutsiderCount)
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

            var outsiderOverflow = Math.Max(0, incremented.Outsiders - target.Outsiders);
            if (isPendingRequiredOutsider)
            {
                outsiderOverflow = Math.Max(0, incremented.Outsiders - baseOutsiderCount);
            }

            var maximumNeeded = Math.Max(0, target.Townsfolk - incremented.Townsfolk)
                + Math.Max(0, target.Outsiders - incremented.Outsiders)
                + Math.Max(0, target.Minions - incremented.Minions)
                + Math.Max(0, target.Demons - incremented.Demons)
                + outsiderOverflow;

            if (minimumNeeded > seatsLeftAfterPick || maximumNeeded < seatsLeftAfterPick)
            {
                continue;
            }

            var wouldRealDemons = state.RealDemonsChosen + (candidate.Type == CharacterType.Demon ? 1 : 0);
            if (target.Demons > 0 && seatsLeftAfterPick == 0 && wouldRealDemons == 0)
            {
                continue;
            }

            var lockedPositiveOutsiderDelta = GetLockedPositiveOutsiderDelta(script, chosenSet, candidate.Id);
            var requiredPositiveOutsiderDelta = Math.Max(0, incremented.Outsiders - baseOutsiderCount - lockedPositiveOutsiderDelta);
            if (seatsLeftAfterPick == 0 && requiredPositiveOutsiderDelta > 0)
            {
                continue;
            }

            var requiresFinalDemonPick = target.Demons - incremented.Demons == 1;
            if (seatsLeftAfterPick == 1 && requiredPositiveOutsiderDelta > 0 && requiresFinalDemonPick)
            {
                var canFinalDemonSupplyOutsiderDelta = script.EffectiveCharacters
                    .Where(character => character.Type == CharacterType.Demon)
                    .Where(character => !chosenSet.Contains(character.Id))
                    .Where(character => !string.Equals(character.Id, candidate.Id, StringComparison.OrdinalIgnoreCase))
                    .Any(character => GetMaxPositiveOutsiderDelta(character.SetupRules) >= requiredPositiveOutsiderDelta);

                if (!canFinalDemonSupplyOutsiderDelta)
                {
                    continue;
                }
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

    private static int GetLockedPositiveOutsiderDelta(Script script, IReadOnlyCollection<string> chosenIds, string speculativeCandidateId)
    {
        return script.EffectiveCharacters
            .Where(character => chosenIds.Contains(character.Id, StringComparer.OrdinalIgnoreCase)
                || string.Equals(character.Id, speculativeCandidateId, StringComparison.OrdinalIgnoreCase))
            .Sum(character => GetMaxPositiveOutsiderDelta(character.SetupRules));
    }

    private static int GetMaxPositiveOutsiderDelta(IReadOnlyList<ISetupRule> setupRules)
    {
        var maxDelta = 0;

        foreach (var setupRule in setupRules)
        {
            if (setupRule is OutsiderDeltaSetupRule outsiderDelta && outsiderDelta.Delta > 0)
            {
                maxDelta += outsiderDelta.Delta;
                continue;
            }

            if (setupRule is StoryTellerChoiceSetupRule storytellerChoice)
            {
                maxDelta += storytellerChoice.Options
                    .Select(option => GetMaxPositiveOutsiderDelta(new[] { option }))
                    .DefaultIfEmpty(0)
                    .Max();
                continue;
            }

            if (setupRule is UnconstrainedOutsiderDeltaSetupRule)
            {
                return int.MaxValue;
            }
        }

        return maxDelta;
    }

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

    private IReadOnlyList<OfferOption> NormalizeOfferOptions(IReadOnlyList<OfferOption> offeredOptions)
    {
        return offeredOptions
            .Where(option => !string.IsNullOrWhiteSpace(option.CharacterId))
            .Select(option => new OfferOption(
                option.CharacterId.Trim(),
                option.HiddenFlags,
                option.DisguiseCharacterId.Trim(),
                option.BorrowedAbilityCharacterId.Trim()))
            .Distinct()
            .OrderBy(option => option.CharacterId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.DisguiseCharacterId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.BorrowedAbilityCharacterId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.HiddenFlags.IsDrunk)
            .ThenBy(option => option.HiddenFlags.IsLunatic)
            .ToList()
            .AsReadOnly();
    }

    private void ValidateOfferOption(
        GameSession session,
        OfferOption option,
        HashSet<string> validIds,
        IReadOnlyList<string> chosenCharacterIds,
        bool drunkAlreadyChosen,
        bool lunaticAlreadyChosen)
    {
        if (option.HiddenFlags.IsDrunk && option.HiddenFlags.IsLunatic)
        {
            throw new InvalidOperationException("An offered option cannot be both Drunk and Lunatic.");
        }

        if (option.HiddenFlags.IsDrunk)
        {
            var isDrunkRole = string.Equals(option.CharacterId, "drunk", StringComparison.OrdinalIgnoreCase);
            var isHermitRole = string.Equals(option.CharacterId, "hermit", StringComparison.OrdinalIgnoreCase);
            if (!isDrunkRole && !isHermitRole)
            {
                throw new InvalidOperationException("Drunk option must use character id 'drunk' or 'hermit'.");
            }

            if (isDrunkRole && drunkAlreadyChosen)
            {
                throw new InvalidOperationException("Drunk is already chosen in this game.");
            }

            if (isHermitRole)
            {
                var hasDrunkOnScript = session.Script.Characters.Any(character => string.Equals(character.Id, "drunk", StringComparison.OrdinalIgnoreCase));
                var hasLunaticOnScript = session.Script.Characters.Any(character => string.Equals(character.Id, "lunatic", StringComparison.OrdinalIgnoreCase));
                if (!hasDrunkOnScript || hasLunaticOnScript)
                {
                    throw new InvalidOperationException("Hermit can only be offered as a drunk-hidden option when Drunk is on script and Lunatic is not.");
                }
            }

            if (string.IsNullOrWhiteSpace(option.DisguiseCharacterId))
            {
                throw new InvalidOperationException("Drunk option requires a shown Townsfolk disguise.");
            }

            var disguise = ResolveDef(session, option.DisguiseCharacterId);
            if (disguise.Type != CharacterType.Townsfolk)
            {
                throw new InvalidOperationException("Drunk option disguise must be a Townsfolk character.");
            }

            if (chosenCharacterIds.Contains(option.DisguiseCharacterId, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Drunk option disguise must not already be in play.");
            }

            return;
        }

        if (option.HiddenFlags.IsLunatic)
        {
            var isLunaticRole = string.Equals(option.CharacterId, "lunatic", StringComparison.OrdinalIgnoreCase);
            var isHermitRole = string.Equals(option.CharacterId, "hermit", StringComparison.OrdinalIgnoreCase);
            if (!isLunaticRole && !isHermitRole)
            {
                throw new InvalidOperationException("Lunatic option must use character id 'lunatic' or 'hermit'.");
            }

            if (isLunaticRole && lunaticAlreadyChosen)
            {
                throw new InvalidOperationException("Lunatic is already chosen in this game.");
            }

            if (isHermitRole)
            {
                var hasDrunkOnScript = session.Script.Characters.Any(character => string.Equals(character.Id, "drunk", StringComparison.OrdinalIgnoreCase));
                var hasLunaticOnScript = session.Script.Characters.Any(character => string.Equals(character.Id, "lunatic", StringComparison.OrdinalIgnoreCase));
                if (!hasLunaticOnScript || hasDrunkOnScript)
                {
                    throw new InvalidOperationException("Hermit can only be offered as a lunatic-hidden option when Lunatic is on script and Drunk is not.");
                }
            }

            if (string.IsNullOrWhiteSpace(option.DisguiseCharacterId))
            {
                throw new InvalidOperationException("Lunatic option requires a shown Demon disguise.");
            }

            var disguise = ResolveDef(session, option.DisguiseCharacterId);
            if (disguise.Type != CharacterType.Demon)
            {
                throw new InvalidOperationException("Lunatic option disguise must be a Demon character.");
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(option.DisguiseCharacterId))
        {
            throw new InvalidOperationException("Normal options cannot specify a disguise character.");
        }

        if (!validIds.Contains(option.CharacterId)
            && !(session.IsLegionGame && string.Equals(option.CharacterId, EvilSentinelCharacterId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Curated offer contains invalid id '{option.CharacterId}'.");
        }

        if (!string.IsNullOrWhiteSpace(option.BorrowedAbilityCharacterId))
        {
            var character = ResolveDef(session, option.CharacterId);
            if (!character.IsDynamicSetup)
            {
                throw new InvalidOperationException("Only dynamic-setup roles can specify a borrowed ability in an offer.");
            }

            ValidateBorrowedAbilityOffer(session, option.CharacterId, option.BorrowedAbilityCharacterId);
        }
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

    private static bool AreSameOfferSet(IReadOnlyList<OfferOption> left, IReadOnlyList<OfferOption> right)
    {
        return left.Count == right.Count
            && left.OrderBy(option => option.CharacterId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(option => option.DisguiseCharacterId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(option => option.BorrowedAbilityCharacterId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(option => option.HiddenFlags.IsDrunk)
                .ThenBy(option => option.HiddenFlags.IsLunatic)
                .SequenceEqual(
                    right.OrderBy(option => option.CharacterId, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(option => option.DisguiseCharacterId, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(option => option.BorrowedAbilityCharacterId, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(option => option.HiddenFlags.IsDrunk)
                        .ThenBy(option => option.HiddenFlags.IsLunatic));
    }

    private static string GetPresentedCharacterId(OfferOption option)
    {
        return string.IsNullOrWhiteSpace(option.DisguiseCharacterId)
            ? option.CharacterId
            : option.DisguiseCharacterId;
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
            _loricDatabase,
            state.BorrowedAbilityCharacterIds,
            BuildFeasibilityContext(session, state));

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
            new SessionSetupOptions(session.UseMarionette, session.UseAtheist, session.IsLegionGame, session.LegionCount),
            _characterDatabase,
            _loricDatabase,
            snapshot.BorrowedAbilityCharacterIds,
            BuildFeasibilityContext(session, snapshot));

        var requiresRealDemon = setup.ValidTargetCounts.Count > 0
            && setup.ValidTargetCounts.All(target => target.Demons > 0);
        if (requiresRealDemon && snapshot.RealDemonsChosen == 0)
        {
            throw new InvalidOperationException("Draft cannot complete without at least one non-Lunatic Demon assignment.");
        }

        if (session.UseAtheist)
        {
            var hasSoberAtheist = session.Players.Any(slot =>
                slot.Choice is PlayerChoice.ChosenChoice chosen
                && string.Equals(chosen.CharacterId, "atheist", StringComparison.OrdinalIgnoreCase)
                && !chosen.HiddenFlags.IsDrunk);

            if (!hasSoberAtheist)
            {
                throw new InvalidOperationException("Draft cannot complete without at least one sober Atheist assignment.");
            }
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
