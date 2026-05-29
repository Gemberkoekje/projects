using System.Text.Json;
using TheCuratool.Application;
using TheCuratool.Application.Abstractions.Repositories;
using TheCuratool.Domain;

namespace TheCuratool.Web;

public sealed class DraftSessionState
{
    private const string CuratorLoricId = "the_curator";
    private const string EvilSentinelCharacterId = "evil";
    private const string EvilCurationLimitMessage = "When adding Evil, curate up to 2 other characters.";

    private readonly ScriptParser _scriptParser;
    private readonly SetupCalculator _setupCalculator;
    private readonly DraftEngine _draftEngine;
    private readonly CharacterDatabase _characterDatabase;
    private readonly LoricDatabase _loricDatabase;
    private readonly IScriptRepository _scriptRepository;
    private readonly IGameSessionRepository _gameSessionRepository;
    private readonly List<string> _activeLoricIds = new();
    private readonly List<string> _currentOfferIds = new();
    private readonly List<string> _curatedOfferSelection = new();
    private Guid _loadedScriptId;

    public DraftSessionState(
        ScriptParser scriptParser,
        SetupCalculator setupCalculator,
        DraftEngine draftEngine,
        CharacterDatabase characterDatabase,
        LoricDatabase loricDatabase,
        IScriptRepository scriptRepository,
        IGameSessionRepository gameSessionRepository)
    {
        _scriptParser = scriptParser;
        _setupCalculator = setupCalculator;
        _draftEngine = draftEngine;
        _characterDatabase = characterDatabase;
        _loricDatabase = loricDatabase;
        _scriptRepository = scriptRepository;
        _gameSessionRepository = gameSessionRepository;
        EnsureCuratorLoric();
    }

    public string ScriptJson { get; set; } = string.Empty;

    public IReadOnlyList<StoredScript> AvailableScripts { get; private set; } = Array.Empty<StoredScript>();

    public ScriptParseResult LoadResult { get; private set; } = new(new Script(string.Empty, string.Empty, Array.Empty<CharacterDefinition>()), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    public SetupCalculationResult SetupResult { get; private set; } = new(new SetupCounts(0, 0, 0, 0), Array.Empty<SetupCounts>());

    public GameSession CurrentSession { get; private set; } = new(Guid.Empty, new Script(string.Empty, string.Empty, Array.Empty<CharacterDefinition>()), 0, Array.Empty<PlayerSlot>(), GameStatus.Unknown, Array.Empty<string>(), false, false, 0);

    public bool HasCurrentSession { get; private set; }

    public int PlayerCount { get; set; } = 7;

    public bool UseMarionette { get; set; }

    public bool IsLegionGame { get; set; }

    public int LegionCount { get; set; }

    public bool RevealChosenCharacters { get; set; }

    public bool NextChoiceIsDrunk { get; set; }

    public bool NextChoiceIsLunatic { get; set; }

    public bool IsCuratingOffer { get; private set; }

    public bool AddEvilOptionToCuratedOffer { get; private set; }

    public bool SupportsMarionetteOption => ScriptContainsCharacter("marionette");

    public bool SupportsLegionOption => ScriptContainsCharacter("legion");

    public bool SupportsAtheistCommitment => ScriptContainsCharacter("atheist");

    public bool SupportsDrunkFlag => ScriptContainsCharacter("drunk");

    public bool SupportsLunaticFlag => ScriptContainsCharacter("lunatic");

    public string DraftMessage { get; private set; } = string.Empty;

    public Guid CurrentSessionId => CurrentSession.Id;

    public IReadOnlyList<LoricDefinition> AvailableLorics => _loricDatabase.GetAll()
        .OrderBy(loric => loric.DisplayName, StringComparer.Ordinal)
        .ToList()
        .AsReadOnly();

    public IReadOnlyList<string> ActiveLoricIds => _activeLoricIds.AsReadOnly();

    public IReadOnlyList<string> CurrentOfferIds => _currentOfferIds.AsReadOnly();

    public IReadOnlyList<string> CuratedOfferSelection => _curatedOfferSelection.AsReadOnly();

    public MakeupSummary CurrentMakeupSummary => HasCurrentSession
        ? _draftEngine.GetMakeupSummary(CurrentSession)
        : new MakeupSummary(new SetupCounts(0, 0, 0, 0), Array.Empty<SetupCounts>(), new Dictionary<CharacterType, IReadOnlyList<string>>(), 0, Array.Empty<string>());

    public IReadOnlyList<CharacterDefinition> CurrentOfferCharacters => ResolveCharacters(CurrentOfferIds);

    public IReadOnlyList<CharacterDefinition> CurrentValidCharacters => HasCurrentSession
        ? _draftEngine.GetRemainingValidCharacters(CurrentSession)
        : Array.Empty<CharacterDefinition>();

    public int CurrentPlayerSlot => HasCurrentSession
        ? CurrentSession.Players.Where(slot => slot.Choice is not PlayerChoice.ChosenChoice).Select(slot => slot.DraftOrder).FirstOrDefault()
        : 0;

    public Task LoadScriptAsync()
    {
        _loadedScriptId = Guid.Empty;
        LoadResult = _scriptParser.Parse(ScriptJson, _characterDatabase);
        if (!LoadResult.IsSuccess)
        {
            SetupResult = new SetupCalculationResult(new SetupCounts(0, 0, 0, 0), Array.Empty<SetupCounts>());
            ResetDraftState();
            HasCurrentSession = false;
            CurrentSession = new GameSession(Guid.Empty, LoadResult.Script, PlayerCount, Array.Empty<PlayerSlot>(), GameStatus.Unknown, ActiveLoricIds, UseMarionette, IsLegionGame, LegionCount);
            return Task.CompletedTask;
        }

        if (!SupportsLegionOption)
        {
            IsLegionGame = false;
            LegionCount = 0;
        }

        ResetDraftState();
        RecalculateSetup();
        return Task.CompletedTask;
    }

    public async Task LoadAvailableScriptsAsync()
    {
        AvailableScripts = await _scriptRepository.GetAllAsync();
    }

    public async Task LoadStoredScriptAsync(Guid scriptId)
    {
        if (scriptId == Guid.Empty)
        {
            return;
        }

        var storedScript = await _scriptRepository.GetByIdAsync(scriptId);
        _loadedScriptId = storedScript.Id;
        ScriptJson = storedScript.RawJson;
        LoadResult = new ScriptParseResult(storedScript.Script, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        if (!SupportsLegionOption)
        {
            IsLegionGame = false;
            LegionCount = 0;
        }
        ResetDraftState();
        RecalculateSetup();
    }

    public async Task<bool> EnsureSessionLoadedAsync(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            return false;
        }

        if (HasCurrentSession && CurrentSession.Id == sessionId)
        {
            return true;
        }

        try
        {
            var loaded = await _gameSessionRepository.GetByIdAsync(sessionId);
            CurrentSession = loaded;
            LoadResult = new ScriptParseResult(loaded.Script, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            ScriptJson = string.Empty;
            _loadedScriptId = Guid.Empty;
            HasCurrentSession = true;
            PlayerCount = loaded.PlayerCount;
            UseMarionette = loaded.UseMarionette;
            IsLegionGame = loaded.IsLegionGame;
            LegionCount = loaded.LegionCount;
            _activeLoricIds.Clear();
            _activeLoricIds.AddRange(loaded.ActiveLoricIds);
            EnsureCuratorLoric();
            _draftEngine.TrackSession(loaded);
            SyncOfferFromCurrentSlot();
            ClearDraftMessage();
            return true;
        }
        catch (InvalidOperationException)
        {
            SetDraftMessage($"Session '{sessionId}' was not found.");
            return false;
        }
    }

    public void SetPlayerCount(int playerCount)
    {
        PlayerCount = playerCount;
        if (IsLegionGame && LegionCount == 0 && playerCount is >= 5 and <= 15)
        {
            LegionCount = _setupCalculator.GetDefaultLegionCount(playerCount);
        }
    }

    public void SetLegionGame(bool isLegionGame)
    {
        IsLegionGame = isLegionGame && SupportsLegionOption;
        if (IsLegionGame && LegionCount == 0 && PlayerCount is >= 5 and <= 15)
        {
            LegionCount = _setupCalculator.GetDefaultLegionCount(PlayerCount);
        }

        if (!IsLegionGame)
        {
            LegionCount = 0;
        }
    }

    public void SetLegionCount(int legionCount)
    {
        LegionCount = Math.Clamp(legionCount, 0, PlayerCount);
    }

    public void SetLoricActive(string loricId, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(loricId))
        {
            return;
        }

        var normalized = loricId.Trim();
        if (isActive)
        {
            if (!_activeLoricIds.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                _activeLoricIds.Add(normalized);
            }
        }
        else
        {
            _activeLoricIds.RemoveAll(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase));
        }

        EnsureCuratorLoric();
        RecalculateSetup();
    }

    public bool IsLoricActive(string loricId)
    {
        return _activeLoricIds.Contains(loricId, StringComparer.OrdinalIgnoreCase);
    }

    public void RecalculateSetup()
    {
        EnsureCuratorLoric();

        if (LoadResult.Script.Characters.Count == 0)
        {
            SetupResult = new SetupCalculationResult(new SetupCounts(0, 0, 0, 0), Array.Empty<SetupCounts>());
            return;
        }

        SetupResult = _setupCalculator.Calculate(
            LoadResult.Script,
            PlayerCount,
            Array.Empty<string>(),
            ActiveLoricIds,
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(UseMarionette, IsLegionGame, LegionCount),
            _characterDatabase,
            _loricDatabase);
    }

    public async Task StartDraftAsync()
    {
        ClearDraftMessage();

        if (LoadResult.Script.Characters.Count == 0)
        {
            HasCurrentSession = false;
            SetDraftMessage("Load a valid script before starting the draft.");
            return;
        }

        if (PlayerCount < 5 || PlayerCount > 15)
        {
            HasCurrentSession = false;
            SetDraftMessage("Player count must be between 5 and 15.");
            return;
        }

        try
        {
            RecalculateSetup();
            var started = _draftEngine.StartSession(LoadResult.Script, PlayerCount, ActiveLoricIds, UseMarionette, IsLegionGame, LegionCount);
            var scriptId = await EnsureLoadedScriptIdAsync(started.Script);
            var persisted = await _gameSessionRepository.AddAsync(started, scriptId);
            CurrentSession = persisted;
            _draftEngine.TrackSession(persisted);
            HasCurrentSession = true;
            ResetOfferState();
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            HasCurrentSession = false;
            SetDraftMessage(ex.Message);
        }
    }

    public void OfferRandomThree()
    {
        if (!HasCurrentSession || CurrentSession.Status != GameStatus.Drafting)
        {
            SetDraftMessage("No active draft session is available.");
            return;
        }

        ClearDraftMessage();
        var suggestions = _draftEngine.SuggestThree(CurrentSession);
        _currentOfferIds.Clear();
        _currentOfferIds.AddRange(suggestions.Select(character => character.Id));
        IsCuratingOffer = false;
        _curatedOfferSelection.Clear();

        if (_currentOfferIds.Count == 0)
        {
            SetDraftMessage("No valid characters remain for this slot.");
        }
    }

    public void BeginCuratedOffer()
    {
        if (!HasCurrentSession || CurrentSession.Status != GameStatus.Drafting)
        {
            SetDraftMessage("No active draft session is available.");
            return;
        }

        ClearDraftMessage();
        IsCuratingOffer = true;
        AddEvilOptionToCuratedOffer = false;
        _curatedOfferSelection.Clear();
    }

    public void SetAddEvilOptionToCuratedOffer(bool addEvil)
    {
        if (!IsCuratingOffer)
        {
            return;
        }

        if (addEvil && _curatedOfferSelection.Count >= 3)
        {
            SetDraftMessage(EvilCurationLimitMessage);
            return;
        }

        AddEvilOptionToCuratedOffer = addEvil;
        ClearDraftMessage();
    }

    public void ToggleCuratedCharacter(string characterId)
    {
        if (!IsCuratingOffer || string.IsNullOrWhiteSpace(characterId))
        {
            return;
        }

        var normalized = characterId.Trim();
        if (_curatedOfferSelection.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            _curatedOfferSelection.RemoveAll(id => string.Equals(id, normalized, StringComparison.OrdinalIgnoreCase));
            return;
        }

        var maxCuratedCharacters = AddEvilOptionToCuratedOffer ? 2 : 3;
        if (_curatedOfferSelection.Count >= maxCuratedCharacters)
        {
            SetDraftMessage(AddEvilOptionToCuratedOffer
                ? EvilCurationLimitMessage
                : "You can curate up to 3 characters.");
            return;
        }

        _curatedOfferSelection.Add(normalized);
    }

    public async Task ConfirmCuratedOfferAsync()
    {
        if (!HasCurrentSession || CurrentSession.Status != GameStatus.Drafting)
        {
            SetDraftMessage("No active draft session is available.");
            return;
        }

        var slot = CurrentPlayerSlot;
        if (slot < 1)
        {
            SetDraftMessage("No pending player slot remains.");
            return;
        }

        try
        {
            var offeredIds = AddEvilOptionToCuratedOffer
                ? _curatedOfferSelection.Concat(new[] { EvilSentinelCharacterId }).ToList().AsReadOnly()
                : _curatedOfferSelection.AsReadOnly();

            CurrentSession = _draftEngine.CreateCuratedOffer(CurrentSession.Id, slot, offeredIds);
            await _gameSessionRepository.UpdateAsync(CurrentSession);
            SyncOfferFromCurrentSlot();
            IsCuratingOffer = false;
            AddEvilOptionToCuratedOffer = false;
            _curatedOfferSelection.Clear();
            ClearDraftMessage();
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            SetDraftMessage(ex.Message);
        }
    }

    public async Task ConfirmAtheistCommitmentAsync()
    {
        if (!HasCurrentSession || CurrentSession.Status != GameStatus.Drafting)
        {
            SetDraftMessage("No active draft session is available.");
            return;
        }

        var slot = CurrentPlayerSlot;
        if (slot < 1)
        {
            SetDraftMessage("No pending player slot remains.");
            return;
        }

        try
        {
            CurrentSession = _draftEngine.ConfirmAtheistCommitment(CurrentSession.Id, slot);
            await _gameSessionRepository.UpdateAsync(CurrentSession);
            ClearDraftMessage();
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            SetDraftMessage(ex.Message);
        }
    }

    public async Task RecordChoiceAsync(string chosenCharacterId)
    {
        if (!HasCurrentSession || CurrentSession.Status != GameStatus.Drafting)
        {
            SetDraftMessage("No active draft session is available.");
            return;
        }

        if (_currentOfferIds.Count == 0)
        {
            SetDraftMessage("Generate or curate an offer before recording a choice.");
            return;
        }

        var slot = CurrentPlayerSlot;
        if (slot < 1)
        {
            SetDraftMessage("No pending player slot remains.");
            return;
        }

        try
        {
            var hiddenFlags = CreateHiddenFlagsForChoice(chosenCharacterId);
            CurrentSession = _draftEngine.RecordChoice(CurrentSession.Id, slot, chosenCharacterId, CurrentOfferIds, hiddenFlags);
            await _gameSessionRepository.UpdateAsync(CurrentSession);
            ResetOfferState();
            ClearDraftMessage();

            // Check if the chosen character requires a dynamic ability assignment
            var chosenDef = CurrentSession.Script.Characters.FirstOrDefault(c => string.Equals(c.Id, chosenCharacterId, StringComparison.OrdinalIgnoreCase))
                ?? _characterDatabase.Resolve(chosenCharacterId);

            if (chosenDef.IsDynamicSetup)
            {
                PendingDynamicAbilityDraftOrder = slot;
            }
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            SetDraftMessage(ex.Message);
        }
    }

    /// <summary>
    /// When non-null, indicates the draft order of a slot that requires a dynamic ability assignment
    /// (Alchemist / Boffin) before the summary counts are accurate.
    /// </summary>
    public int? PendingDynamicAbilityDraftOrder { get; private set; }

    /// <summary>
    /// Returns the available ability options for the dynamic-setup character at <paramref name="draftOrder"/>.
    /// </summary>
    public IReadOnlyList<AbilityOption> GetDynamicAbilityOptions(int draftOrder)
    {
        if (!HasCurrentSession)
        {
            return Array.Empty<AbilityOption>();
        }

        var slot = CurrentSession.Players.FirstOrDefault(p => p.DraftOrder == draftOrder);
        if (slot is null || slot.Choice is not PlayerChoice.ChosenChoice chosen)
        {
            return Array.Empty<AbilityOption>();
        }

        var slotDef = CurrentSession.Script.Characters.FirstOrDefault(c => string.Equals(c.Id, chosen.CharacterId, StringComparison.OrdinalIgnoreCase))
            ?? _characterDatabase.Resolve(chosen.CharacterId);

        try
        {
            return slotDef.DynamicAbilityScope switch
            {
                DynamicAbilityScope.NotInPlayMinion => _draftEngine.GetAlchemistAbilityOptions(CurrentSession.Id, draftOrder),
                DynamicAbilityScope.NotInPlayTownsfolkOrOutsider => _draftEngine.GetBoffinAbilityOptions(CurrentSession.Id, draftOrder),
                _ => Array.Empty<AbilityOption>(),
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException)
        {
            SetDraftMessage(ex.Message);
            return Array.Empty<AbilityOption>();
        }
    }

    /// <summary>
    /// Assigns the borrowed ability identified by <paramref name="abilityCharacterId"/> to the
    /// dynamic-setup character at <paramref name="draftOrder"/> and persists the session.
    /// </summary>
    public async Task AssignDynamicAbilityAsync(int draftOrder, string abilityCharacterId)
    {
        if (!HasCurrentSession)
        {
            return;
        }

        try
        {
            CurrentSession = _draftEngine.AssignDynamicAbility(CurrentSession.Id, draftOrder, abilityCharacterId);
            await _gameSessionRepository.UpdateAsync(CurrentSession);

            if (PendingDynamicAbilityDraftOrder == draftOrder)
            {
                PendingDynamicAbilityDraftOrder = null;
            }

            ClearDraftMessage();
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            SetDraftMessage(ex.Message);
        }
    }

    /// <summary>
    /// Resolves an unresolved ST-assigned evil slot (from Legion mode or ST-offered evil) by assigning the actual character.
    /// </summary>
    public async Task ResolveEvilSlotAsync(int draftOrder, string actualCharacterId, HiddenFlags hiddenFlags)
    {
        if (!HasCurrentSession || CurrentSession.Status != GameStatus.Drafting)
        {
            SetDraftMessage("No active draft session is available.");
            return;
        }

        if (string.IsNullOrWhiteSpace(actualCharacterId))
        {
            SetDraftMessage("Character id is required.");
            return;
        }

        try
        {
            CurrentSession = _draftEngine.ResolveEvilSlot(CurrentSession.Id, draftOrder, actualCharacterId, hiddenFlags);
            await _gameSessionRepository.UpdateAsync(CurrentSession);
            ClearDraftMessage();
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            SetDraftMessage(ex.Message);
        }
    }

    /// <summary>
    /// Resolves an unresolved ST-assigned minion slot (from Kazali or Lord of Typhon) by assigning the minion character.
    /// </summary>
    public async Task ResolveMinionSlotAsync(int draftOrder, string characterId)
    {
        if (!HasCurrentSession || CurrentSession.Status != GameStatus.Drafting)
        {
            SetDraftMessage("No active draft session is available.");
            return;
        }

        if (string.IsNullOrWhiteSpace(characterId))
        {
            SetDraftMessage("Character id is required.");
            return;
        }

        try
        {
            CurrentSession = _draftEngine.ResolveMinionSlot(CurrentSession.Id, draftOrder, characterId);
            await _gameSessionRepository.UpdateAsync(CurrentSession);
            ClearDraftMessage();
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            SetDraftMessage(ex.Message);
        }
    }

    public string BuildSummaryJson()
    {
        if (!HasCurrentSession)
        {
            return "{}";
        }

        var payload = new
        {
            sessionId = CurrentSession.Id,
            script = new { name = CurrentSession.Script.Name, author = CurrentSession.Script.Author },
            playerCount = CurrentSession.PlayerCount,
            status = CurrentSession.Status.ToString(),
            activeLorics = CurrentSession.ActiveLoricIds,
            useMarionette = CurrentSession.UseMarionette,
            isLegionGame = CurrentSession.IsLegionGame,
            legionCount = CurrentSession.LegionCount,
            assignments = CurrentSession.Players
                .OrderBy(slot => slot.DraftOrder)
                .Select(slot => new
                {
                    slot = slot.DraftOrder,
                    playerId = slot.PlayerId,
                    choice = slot.Choice is PlayerChoice.ChosenChoice chosen
                        ? new
                        {
                            characterId = chosen.CharacterId,
                            offeredIds = chosen.OfferedIds,
                            hiddenFlags = new { chosen.HiddenFlags.IsDrunk, chosen.HiddenFlags.IsLunatic },
                        }
                        : null,
                })
                .ToList(),
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private async Task<Guid> EnsureLoadedScriptIdAsync(Script script)
    {
        if (_loadedScriptId != Guid.Empty)
        {
            return _loadedScriptId;
        }

        var existingScripts = await _scriptRepository.GetAllAsync();
        var matchingScript = existingScripts.FirstOrDefault(candidate => ScriptsMatch(candidate.Script, script));
        if (matchingScript is not null)
        {
            _loadedScriptId = matchingScript.Id;
            return _loadedScriptId;
        }

        var storedScript = await _scriptRepository.AddAsync(script.Name, script.Author, ScriptJson);
        _loadedScriptId = storedScript.Id;
        return _loadedScriptId;
    }

    private static bool ScriptsMatch(Script left, Script right)
    {
        if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            || !string.Equals(left.Author, right.Author, StringComparison.Ordinal)
            || left.Characters.Count != right.Characters.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Characters.Count; index++)
        {
            if (!string.Equals(left.Characters[index].Id, right.Characters[index].Id, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private IReadOnlyList<CharacterDefinition> ResolveCharacters(IReadOnlyList<string> ids)
    {
        var scriptMap = LoadResult.Script.Characters
            .GroupBy(character => character.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var resolved = new List<CharacterDefinition>();
        foreach (var id in ids)
        {
            if (string.Equals(id, EvilSentinelCharacterId, StringComparison.OrdinalIgnoreCase))
            {
                resolved.Add(CreateEvilSentinelCharacter());
                continue;
            }

            if (scriptMap.TryGetValue(id, out var inScript))
            {
                resolved.Add(inScript);
            }
            else
            {
                resolved.Add(_characterDatabase.Resolve(id));
            }
        }

        return resolved.AsReadOnly();
    }

    public bool CanApplyDrunkToChoice(string characterId)
    {
        if (!SupportsDrunkFlag)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(characterId))
        {
            return false;
        }

        if (string.Equals(characterId.Trim(), EvilSentinelCharacterId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var character = ResolveCharacter(characterId);
        return character.Type == CharacterType.Townsfolk;
    }

    public bool CanApplyLunaticToChoice(string characterId)
    {
        if (!SupportsLunaticFlag)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(characterId))
        {
            return false;
        }

        if (string.Equals(characterId.Trim(), EvilSentinelCharacterId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var character = ResolveCharacter(characterId);
        return character.Type == CharacterType.Demon;
    }

    private HiddenFlags CreateHiddenFlagsForChoice(string characterId)
    {
        var canApplyDrunk = CanApplyDrunkToChoice(characterId);
        var canApplyLunatic = CanApplyLunaticToChoice(characterId);

        if (!canApplyDrunk)
        {
            NextChoiceIsDrunk = false;
        }

        if (!canApplyLunatic)
        {
            NextChoiceIsLunatic = false;
        }

        return new HiddenFlags(NextChoiceIsDrunk && canApplyDrunk, NextChoiceIsLunatic && canApplyLunatic);
    }

    private bool ScriptContainsCharacter(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            return false;
        }

        return LoadResult.Script.Characters.Any(character => string.Equals(character.Id, characterId, StringComparison.OrdinalIgnoreCase));
    }

    private CharacterDefinition ResolveCharacter(string characterId)
    {
        var normalized = characterId.Trim();
        if (string.Equals(normalized, EvilSentinelCharacterId, StringComparison.OrdinalIgnoreCase))
        {
            return CreateEvilSentinelCharacter();
        }

        var fromScript = LoadResult.Script.Characters.FirstOrDefault(character => string.Equals(character.Id, normalized, StringComparison.OrdinalIgnoreCase));
        return fromScript ?? _characterDatabase.Resolve(normalized);
    }

    private void SyncOfferFromCurrentSlot()
    {
        _currentOfferIds.Clear();

        var slot = CurrentSession.Players
            .FirstOrDefault(player => player.Choice is not PlayerChoice.ChosenChoice);

        if (slot?.Choice is PlayerChoice.UnchosenChoice unchosen && unchosen.OfferedIds.Count > 0)
        {
            _currentOfferIds.AddRange(unchosen.OfferedIds);
        }
    }

    private void EnsureCuratorLoric()
    {
        if (!_activeLoricIds.Contains(CuratorLoricId, StringComparer.OrdinalIgnoreCase))
        {
            _activeLoricIds.Add(CuratorLoricId);
        }
    }

    private void ResetDraftState()
    {
        HasCurrentSession = false;
        CurrentSession = new GameSession(Guid.Empty, LoadResult.Script, PlayerCount, Array.Empty<PlayerSlot>(), GameStatus.Unknown, ActiveLoricIds, UseMarionette, IsLegionGame, LegionCount);
        ResetOfferState();
        ClearDraftMessage();
    }

    private void ResetOfferState()
    {
        _currentOfferIds.Clear();
        _curatedOfferSelection.Clear();
        IsCuratingOffer = false;
        AddEvilOptionToCuratedOffer = false;
        NextChoiceIsDrunk = false;
        NextChoiceIsLunatic = false;
    }

    private static CharacterDefinition CreateEvilSentinelCharacter()
    {
        return new CharacterDefinition(EvilSentinelCharacterId, "Evil", CharacterType.Demon, Array.Empty<ISetupRule>(), Array.Empty<IAvailabilityConstraint>(), false, false, false);
    }

    private void SetDraftMessage(string message)
    {
        DraftMessage = message;
    }

    private void ClearDraftMessage()
    {
        DraftMessage = string.Empty;
    }
}
