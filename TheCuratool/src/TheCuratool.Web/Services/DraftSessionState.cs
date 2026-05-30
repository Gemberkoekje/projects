using System.Text.Json;
using TheCuratool.Application;
using TheCuratool.Application.Abstractions.Repositories;
using TheCuratool.Domain;

namespace TheCuratool.Web;

public sealed class DraftSessionState
{
    private const string CuratorLoricId = "thecurator";
    private const string EvilSentinelCharacterId = "evil";

    private readonly ScriptParser _scriptParser;
    private readonly SetupCalculator _setupCalculator;
    private readonly DraftEngine _draftEngine;
    private readonly CharacterDatabase _characterDatabase;
    private readonly LoricDatabase _loricDatabase;
    private readonly IScriptRepository _scriptRepository;
    private readonly IGameSessionRepository _gameSessionRepository;
    private readonly List<string> _activeLoricIds = new();
    private readonly List<OfferOption> _currentOfferOptions = new();
    private readonly List<OfferOption> _curatedOfferSelection = new();
    private readonly Dictionary<string, string> _curatedDynamicAbilitySelections = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<SetupCounts> _selectedTargetCounts = new();
    private bool _allTargetsMode = true;
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

    public GameSession CurrentSession { get; private set; } = new(Guid.Empty, new Script(string.Empty, string.Empty, Array.Empty<CharacterDefinition>()), 0, Array.Empty<PlayerSlot>(), GameStatus.Unknown, Array.Empty<string>(), false, false, false, 0);

    public bool HasCurrentSession { get; private set; }

    public int PlayerCount { get; set; } = 7;

    public bool UseMarionette { get; set; }

    public bool UseAtheist { get; set; }

    public bool IsLegionGame { get; set; }

    public int LegionCount { get; set; }

    public bool RevealChosenCharacters { get; set; }

    public string CuratedRoleCharacterId { get; set; } = string.Empty;

    public string CuratedDisguiseCharacterId { get; set; } = string.Empty;

    public bool IsCuratingOffer { get; private set; }

    public bool SupportsMarionetteOption => ScriptContainsCharacter("marionette");

    public bool SupportsLegionOption => ScriptContainsCharacter("legion");

    public bool SupportsAtheistOption => ScriptContainsCharacter("atheist");

    public bool SupportsDrunkFlag => ScriptContainsCharacter("drunk");

    public bool SupportsLunaticFlag => ScriptContainsCharacter("lunatic");

    public bool SupportsAlchemistOption => ScriptContainsCharacter("alchemist");

    public bool SupportsBoffinOption => ScriptContainsCharacter("boffin");

    public string DraftMessage { get; private set; } = string.Empty;

    public Guid CurrentSessionId => CurrentSession.Id;

    public IReadOnlyList<LoricDefinition> AvailableLorics => _loricDatabase.GetAll()
        .OrderBy(loric => loric.DisplayName, StringComparer.Ordinal)
        .ToList()
        .AsReadOnly();

    public IReadOnlyList<string> ActiveLoricIds => _activeLoricIds.AsReadOnly();

    public IReadOnlyList<OfferOption> CurrentOfferOptions => _currentOfferOptions.AsReadOnly();

    public IReadOnlyList<string> CurrentOfferIds => _currentOfferOptions.Select(GetPresentedCharacterId).ToList().AsReadOnly();

    public IReadOnlyList<OfferOption> CuratedOfferSelection => _curatedOfferSelection.AsReadOnly();

    public MakeupSummary CurrentMakeupSummary => HasCurrentSession
        ? _draftEngine.GetMakeupSummary(CurrentSession)
        : new MakeupSummary(new SetupCounts(0, 0, 0, 0), Array.Empty<SetupCounts>(), new Dictionary<CharacterType, IReadOnlyList<string>>(), 0, Array.Empty<string>());

    /// <summary>
    /// The currently valid target distributions the Storyteller can select among.
    /// </summary>
    public IReadOnlyList<SetupCounts> SelectableTargetCounts => CurrentMakeupSummary.TargetCounts;

    /// <summary>
    /// Whether the given target distribution is currently selected (in scope for Random 3 and curation warnings).
    /// </summary>
    public bool IsTargetSelected(SetupCounts target) => _allTargetsMode || _selectedTargetCounts.Contains(target);

    /// <summary>
    /// Whether every currently-valid target is selected.
    /// </summary>
    public bool AllTargetsSelected
    {
        get
        {
            if (_allTargetsMode)
            {
                return true;
            }

            var targets = SelectableTargetCounts;
            return targets.Count > 0 && targets.All(_selectedTargetCounts.Contains);
        }
    }

    /// <summary>
    /// Selects or deselects a single target distribution. Leaves explicit "all targets" mode when toggled.
    /// </summary>
    public void SetTargetSelected(SetupCounts target, bool isSelected)
    {
        if (_allTargetsMode)
        {
            // Materialize the explicit selection set from the current targets before diverging from "all".
            _allTargetsMode = false;
            _selectedTargetCounts.Clear();
            foreach (var current in SelectableTargetCounts)
            {
                _selectedTargetCounts.Add(current);
            }
        }

        if (isSelected)
        {
            _selectedTargetCounts.Add(target);
        }
        else
        {
            _selectedTargetCounts.Remove(target);
        }

        RefreshOffersForTargetSelection();
    }

    /// <summary>
    /// Convenience toggle that selects or clears all target distributions at once.
    /// </summary>
    public void ToggleAllTargets(bool selectAll)
    {
        if (selectAll)
        {
            _allTargetsMode = true;
            _selectedTargetCounts.Clear();
        }
        else
        {
            _allTargetsMode = false;
            _selectedTargetCounts.Clear();
        }

        RefreshOffersForTargetSelection();
    }

    /// <summary>
    /// The selected targets to pass to the engine. An empty collection means "all targets" (no filtering).
    /// </summary>
    private IReadOnlyCollection<SetupCounts> EffectiveSelectedTargets =>
        _allTargetsMode ? Array.Empty<SetupCounts>() : _selectedTargetCounts.ToArray();

    /// <summary>
    /// Returns a non-empty warning when locking in <paramref name="characterId"/> would make every selected
    /// target distribution unachievable; otherwise returns an empty string. Non-blocking by design.
    /// </summary>
    public string GetTargetReachabilityWarning(string characterId)
    {
        if (!HasCurrentSession || CurrentSession.Status != GameStatus.Drafting || string.IsNullOrWhiteSpace(characterId))
        {
            return string.Empty;
        }

        if (_draftEngine.WouldPickKeepAnyTargetReachable(CurrentSession, characterId, EffectiveSelectedTargets))
        {
            return string.Empty;
        }

        return "This option would make your desired target distribution unachievable.";
    }

    public IReadOnlyList<CharacterDefinition> CurrentOfferCharacters => ResolvePresentedCharacters(CurrentOfferOptions);

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
            CurrentSession = new GameSession(Guid.Empty, LoadResult.Script, PlayerCount, Array.Empty<PlayerSlot>(), GameStatus.Unknown, ActiveLoricIds, UseMarionette, UseAtheist, IsLegionGame, LegionCount);
            return Task.CompletedTask;
        }

        if (!SupportsLegionOption)
        {
            IsLegionGame = false;
            LegionCount = 0;
        }

        if (!SupportsAtheistOption)
        {
            UseAtheist = false;
        }

        ResetDraftState();
        RecalculateSetup();
        return Task.CompletedTask;
    }

    public async Task LoadAvailableScriptsAsync()
    {
        AvailableScripts = await _scriptRepository.GetAllAsync(includeCustomScripts: false);
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

        if (!SupportsAtheistOption)
        {
            UseAtheist = false;
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
            UseAtheist = loaded.UseAtheist;
            IsLegionGame = loaded.IsLegionGame;
            LegionCount = loaded.LegionCount;
            _activeLoricIds.Clear();
            _activeLoricIds.AddRange(loaded.ActiveLoricIds);
            EnsureCuratorLoric();
            _draftEngine.TrackSession(loaded);
            ResetTargetSelection();
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
            new SessionSetupOptions(UseMarionette, UseAtheist, IsLegionGame, LegionCount),
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
            var started = _draftEngine.StartSession(LoadResult.Script, PlayerCount, ActiveLoricIds, UseMarionette, UseAtheist, IsLegionGame, LegionCount);
            var scriptId = await EnsureLoadedScriptIdAsync(started.Script);
            var persisted = await _gameSessionRepository.AddAsync(started, scriptId);
            CurrentSession = persisted;
            _draftEngine.TrackSession(persisted);
            HasCurrentSession = true;
            ResetTargetSelection();
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
        var suggestions = _draftEngine.SuggestThree(CurrentSession, EffectiveSelectedTargets);
        _currentOfferOptions.Clear();
        _currentOfferOptions.AddRange(suggestions);
        IsCuratingOffer = false;
        _curatedOfferSelection.Clear();
        _curatedDynamicAbilitySelections.Clear();
        CuratedRoleCharacterId = string.Empty;
        CuratedDisguiseCharacterId = string.Empty;

        if (_currentOfferOptions.Count == 0)
        {
            SetDraftMessage("No valid characters remain for this slot.");
        }
    }

    /// <summary>
    /// Re-generates the active Random 3 offer when the Storyteller changes the selected target distributions,
    /// so the offered pool always reflects the current selection. Curated offers are left untouched.
    /// </summary>
    private void RefreshOffersForTargetSelection()
    {
        if (!HasCurrentSession || CurrentSession.Status != GameStatus.Drafting)
        {
            return;
        }

        if (IsCuratingOffer || _currentOfferOptions.Count == 0)
        {
            return;
        }

        var suggestions = _draftEngine.SuggestThree(CurrentSession, EffectiveSelectedTargets);
        _currentOfferOptions.Clear();
        _currentOfferOptions.AddRange(suggestions);
    }

    /// <summary>
    /// Reconciles the explicit selected-target set against the latest valid targets. Targets that are no longer
    /// valid are dropped; newly appearing targets are auto-included only while "all targets" mode is active.
    /// In all-targets mode the explicit set stays empty (the empty=all sentinel covers any newly valid targets).
    /// </summary>
    private void ReconcileSelectedTargets()
    {
        if (_allTargetsMode)
        {
            _selectedTargetCounts.Clear();
            return;
        }

        var validTargets = SelectableTargetCounts.ToHashSet();
        _selectedTargetCounts.IntersectWith(validTargets);

        // If every explicit selection disappeared, fall back to "all targets" so the pool never becomes empty.
        if (_selectedTargetCounts.Count == 0)
        {
            _allTargetsMode = true;
        }
    }

    /// <summary>
    /// Resets the target selection back to the default "all targets" mode for a fresh or newly loaded session.
    /// The selection is in-memory only and is intentionally not persisted across browser refreshes.
    /// </summary>
    private void ResetTargetSelection()
    {
        _allTargetsMode = true;
        _selectedTargetCounts.Clear();
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
        _curatedOfferSelection.Clear();
        _curatedDynamicAbilitySelections.Clear();
        CuratedRoleCharacterId = string.Empty;
        CuratedDisguiseCharacterId = string.Empty;
    }

    public void AddCuratedRoleOption(string roleCharacterId, string secondaryCharacterId)
    {
        if (!IsCuratingOffer || string.IsNullOrWhiteSpace(roleCharacterId))
        {
            return;
        }

        var normalizedRole = roleCharacterId.Trim();
        if (IsCuratedSpecialRole(normalizedRole))
        {
            AddCuratedSpecialOption(normalizedRole, secondaryCharacterId);
            return;
        }

        if (_curatedOfferSelection.Count >= 3)
        {
            SetDraftMessage("You can curate up to 3 characters.");
            return;
        }

        var option = OfferOption.Normal(normalizedRole);
        if (_curatedOfferSelection.Any(existing => existing == option))
        {
            return;
        }

        _curatedOfferSelection.Add(option);
        ClearDraftMessage();
    }

    public void ToggleCuratedCharacter(string characterId)
    {
        if (!IsCuratingOffer || string.IsNullOrWhiteSpace(characterId))
        {
            return;
        }

        var normalized = characterId.Trim();
        var option = OfferOption.Normal(normalized);
        var existing = _curatedOfferSelection.FirstOrDefault(current => current == option);

        if (existing is not null)
        {
            _curatedOfferSelection.Remove(existing);
            return;
        }

        AddCuratedRoleOption(normalized, string.Empty);
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
            CurrentSession = _draftEngine.CreateCuratedOffer(CurrentSession.Id, slot, _curatedOfferSelection.AsReadOnly());
            await _gameSessionRepository.UpdateAsync(CurrentSession);
            SyncOfferFromCurrentSlot();
            IsCuratingOffer = false;
            _curatedOfferSelection.Clear();
            CuratedRoleCharacterId = string.Empty;
            CuratedDisguiseCharacterId = string.Empty;
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

        if (_currentOfferOptions.Count == 0)
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
            var offeredOptions = _currentOfferOptions.ToList().AsReadOnly();
            var chosenOption = offeredOptions.FirstOrDefault(option =>
                string.Equals(GetPresentedCharacterId(option), chosenCharacterId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option.CharacterId, chosenCharacterId, StringComparison.OrdinalIgnoreCase));

            CurrentSession = _draftEngine.RecordChoice(CurrentSession.Id, slot, chosenCharacterId, offeredOptions);
            await _gameSessionRepository.UpdateAsync(CurrentSession);

            var resolvedChosenId = chosenOption?.CharacterId ?? chosenCharacterId;

            var chosenDef = CurrentSession.Script.Characters.FirstOrDefault(c => string.Equals(c.Id, resolvedChosenId, StringComparison.OrdinalIgnoreCase))
                ?? _characterDatabase.Resolve(resolvedChosenId);

            if (chosenDef.IsDynamicSetup)
            {
                if (_curatedDynamicAbilitySelections.TryGetValue(resolvedChosenId, out var preselectedAbilityId))
                {
                    if (string.IsNullOrWhiteSpace(CurrentSession.Players.First(player => player.DraftOrder == slot).BorrowedAbilityCharacterId))
                    {
                        CurrentSession = _draftEngine.AssignDynamicAbility(CurrentSession.Id, slot, preselectedAbilityId);
                        await _gameSessionRepository.UpdateAsync(CurrentSession);
                    }

                    _curatedDynamicAbilitySelections.Remove(resolvedChosenId);
                }
                else if (string.IsNullOrWhiteSpace(CurrentSession.Players.First(player => player.DraftOrder == slot).BorrowedAbilityCharacterId))
                {
                    var abilityOptions = GetDynamicAbilityOptions(slot)
                        .Where(option => option.IsAvailable)
                        .ToList();

                    if (abilityOptions.Count == 0)
                    {
                        throw new InvalidOperationException($"No available borrowed abilities for {chosenDef.DisplayName}.");
                    }

                    var randomIndex = Random.Shared.Next(abilityOptions.Count);
                    var selectedAbility = abilityOptions[randomIndex];
                    CurrentSession = _draftEngine.AssignDynamicAbility(CurrentSession.Id, slot, selectedAbility.AbilityCharacterId);
                    await _gameSessionRepository.UpdateAsync(CurrentSession);
                }
            }

            ResetOfferState();
            ReconcileSelectedTargets();
            ClearDraftMessage();
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            SetDraftMessage(ex.Message);
        }
    }

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
                            disguiseCharacterId = chosen.DisguiseCharacterId,
                            offeredOptions = chosen.OfferedOptions,
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

    private IReadOnlyList<CharacterDefinition> ResolvePresentedCharacters(IReadOnlyList<OfferOption> options)
    {
        var scriptMap = LoadResult.Script.Characters
            .GroupBy(character => character.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var resolved = new List<CharacterDefinition>();
        foreach (var option in options)
        {
            var presentedId = GetPresentedCharacterId(option);

            if (CurrentSession.IsLegionGame
                && string.Equals(presentedId, EvilSentinelCharacterId, StringComparison.OrdinalIgnoreCase))
            {
                resolved.Add(CreateEvilSentinelCharacter());
                continue;
            }

            if (scriptMap.TryGetValue(presentedId, out var inScript))
            {
                resolved.Add(inScript);
            }
            else
            {
                resolved.Add(_characterDatabase.Resolve(presentedId));
            }
        }

        return resolved.AsReadOnly();
    }

    public bool CanUseDrunkRoleForCurate()
    {
        if (!SupportsDrunkFlag || !HasCurrentSession)
        {
            return false;
        }

        return !CurrentSession.Players.Any(player =>
            player.Choice is PlayerChoice.ChosenChoice chosen
            && string.Equals(chosen.CharacterId, "drunk", StringComparison.OrdinalIgnoreCase));
    }

    public bool CanUseLunaticRoleForCurate()
    {
        if (!SupportsLunaticFlag || !HasCurrentSession)
        {
            return false;
        }

        return !CurrentSession.Players.Any(player =>
            player.Choice is PlayerChoice.ChosenChoice chosen
            && string.Equals(chosen.CharacterId, "lunatic", StringComparison.OrdinalIgnoreCase));
    }

    public bool CanUseAlchemistRoleForCurate()
    {
        if (!SupportsAlchemistOption || !HasCurrentSession)
        {
            return false;
        }

        return !CurrentSession.Players.Any(player =>
            player.Choice is PlayerChoice.ChosenChoice chosen
            && string.Equals(chosen.CharacterId, "alchemist", StringComparison.OrdinalIgnoreCase));
    }

    public bool CanUseBoffinRoleForCurate()
    {
        if (!SupportsBoffinOption || !HasCurrentSession)
        {
            return false;
        }

        return !CurrentSession.Players.Any(player =>
            player.Choice is PlayerChoice.ChosenChoice chosen
            && string.Equals(chosen.CharacterId, "boffin", StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<CharacterDefinition> GetCurateRoleCandidates()
    {
        if (!HasCurrentSession)
        {
            return Array.Empty<CharacterDefinition>();
        }

        var candidates = CurrentValidCharacters.ToList();

        if (CanUseDrunkRoleForCurate())
        {
            candidates.Add(ResolveCharacter("drunk"));
        }

        if (CanUseLunaticRoleForCurate())
        {
            candidates.Add(ResolveCharacter("lunatic"));
        }

        if (IsCuratedSpecialRole("hermit"))
        {
            candidates.Add(ResolveCharacter("hermit"));
        }

        if (CanUseAlchemistRoleForCurate())
        {
            candidates.Add(ResolveCharacter("alchemist"));
        }

        if (CanUseBoffinRoleForCurate())
        {
            candidates.Add(ResolveCharacter("boffin"));
        }

        return candidates
            .GroupBy(character => character.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(character => character.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    public bool IsCuratedSpecialRole(string roleCharacterId)
    {
        if (string.IsNullOrWhiteSpace(roleCharacterId))
        {
            return false;
        }

        var normalizedRole = roleCharacterId.Trim();

        if (string.Equals(normalizedRole, "drunk", StringComparison.OrdinalIgnoreCase))
        {
            return CanUseDrunkRoleForCurate();
        }

        if (string.Equals(normalizedRole, "lunatic", StringComparison.OrdinalIgnoreCase))
        {
            return CanUseLunaticRoleForCurate();
        }

        if (string.Equals(normalizedRole, "hermit", StringComparison.OrdinalIgnoreCase))
        {
            if (!ScriptContainsCharacter("hermit") || !HasCurrentSession)
            {
                return false;
            }

            var hasDrunkOnScript = ScriptContainsCharacter("drunk");
            var hasLunaticOnScript = ScriptContainsCharacter("lunatic");
            if (hasDrunkOnScript == hasLunaticOnScript)
            {
                return false;
            }

            return !CurrentSession.Players.Any(player =>
                player.Choice is PlayerChoice.ChosenChoice chosen
                && string.Equals(chosen.CharacterId, "hermit", StringComparison.OrdinalIgnoreCase));
        }

        if (string.Equals(normalizedRole, "alchemist", StringComparison.OrdinalIgnoreCase))
        {
            return CanUseAlchemistRoleForCurate();
        }

        if (string.Equals(normalizedRole, "boffin", StringComparison.OrdinalIgnoreCase))
        {
            return CanUseBoffinRoleForCurate();
        }

        return false;
    }

    public IReadOnlyList<CharacterDefinition> GetCurateDisguiseCandidates(string roleCharacterId)
    {
        if (!HasCurrentSession || string.IsNullOrWhiteSpace(roleCharacterId))
        {
            return Array.Empty<CharacterDefinition>();
        }

        var chosenSet = CurrentSession.Players
            .Select(player => player.Choice)
            .OfType<PlayerChoice.ChosenChoice>()
            .Select(chosen => chosen.CharacterId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var consumedDrunkDisguises = CurrentSession.Players
            .Select(player => player.Choice)
            .OfType<PlayerChoice.ChosenChoice>()
            .Where(chosen => chosen.HiddenFlags.IsDrunk && !string.IsNullOrWhiteSpace(chosen.DisguiseCharacterId))
            .Select(chosen => chosen.DisguiseCharacterId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (string.Equals(roleCharacterId, "drunk", StringComparison.OrdinalIgnoreCase))
        {
            return CurrentSession.Script.Characters
                .Where(character => character.Type == CharacterType.Townsfolk)
                .Where(character => !chosenSet.Contains(character.Id))
                .Where(character => !consumedDrunkDisguises.Contains(character.Id))
                .OrderBy(character => character.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly();
        }

        if (string.Equals(roleCharacterId, "lunatic", StringComparison.OrdinalIgnoreCase))
        {
            return CurrentSession.Script.Characters
                .Where(character => character.Type == CharacterType.Demon)
                .Where(character => !chosenSet.Contains(character.Id))
                .OrderBy(character => character.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly();
        }

        if (string.Equals(roleCharacterId, "hermit", StringComparison.OrdinalIgnoreCase))
        {
            var hasDrunkOnScript = ScriptContainsCharacter("drunk");
            var hasLunaticOnScript = ScriptContainsCharacter("lunatic");

            if (hasDrunkOnScript && hasLunaticOnScript)
            {
                return Array.Empty<CharacterDefinition>();
            }

            if (hasDrunkOnScript)
            {
                return CurrentSession.Script.Characters
                    .Where(character => character.Type == CharacterType.Townsfolk)
                    .Where(character => !chosenSet.Contains(character.Id))
                    .Where(character => !consumedDrunkDisguises.Contains(character.Id))
                    .OrderBy(character => character.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    .AsReadOnly();
            }

            if (hasLunaticOnScript)
            {
                return CurrentSession.Script.Characters
                    .Where(character => character.Type == CharacterType.Demon)
                    .Where(character => !chosenSet.Contains(character.Id))
                    .OrderBy(character => character.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    .AsReadOnly();
            }

            return Array.Empty<CharacterDefinition>();
        }

        if (string.Equals(roleCharacterId, "alchemist", StringComparison.OrdinalIgnoreCase))
        {
            return CurrentSession.Script.Characters
                .Where(character => character.Type == CharacterType.Minion)
                .OrderBy(character => character.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly();
        }

        if (string.Equals(roleCharacterId, "boffin", StringComparison.OrdinalIgnoreCase))
        {
            return CurrentSession.Script.Characters
                .Where(character => character.Type == CharacterType.Townsfolk || character.Type == CharacterType.Outsider)
                .Where(character => !chosenSet.Contains(character.Id))
                .Where(character => !consumedDrunkDisguises.Contains(character.Id))
                .OrderBy(character => character.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly();
        }

        return Array.Empty<CharacterDefinition>();
    }

    public void AddCuratedSpecialOption(string roleCharacterId, string disguiseCharacterId)
    {
        if (!IsCuratingOffer || string.IsNullOrWhiteSpace(roleCharacterId) || string.IsNullOrWhiteSpace(disguiseCharacterId))
        {
            return;
        }

        var normalizedRole = roleCharacterId.Trim();
        var normalizedDisguise = disguiseCharacterId.Trim();

        if (_curatedOfferSelection.Count >= 3)
        {
            SetDraftMessage("You can curate up to 3 characters.");
            return;
        }

        OfferOption option;
        if (string.Equals(normalizedRole, "drunk", StringComparison.OrdinalIgnoreCase))
        {
            if (!CanUseDrunkRoleForCurate())
            {
                SetDraftMessage("Drunk is already chosen in this game.");
                return;
            }

            option = new OfferOption(normalizedRole, new HiddenFlags(true, false), normalizedDisguise, string.Empty);
        }
        else if (string.Equals(normalizedRole, "lunatic", StringComparison.OrdinalIgnoreCase))
        {
            if (!CanUseLunaticRoleForCurate())
            {
                SetDraftMessage("Lunatic is already chosen in this game.");
                return;
            }

            option = new OfferOption(normalizedRole, new HiddenFlags(false, true), normalizedDisguise, string.Empty);
        }
        else if (string.Equals(normalizedRole, "hermit", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsCuratedSpecialRole(normalizedRole))
            {
                SetDraftMessage("Hermit cannot be curated in the current script configuration.");
                return;
            }

            var hasDrunkOnScript = ScriptContainsCharacter("drunk");
            var hasLunaticOnScript = ScriptContainsCharacter("lunatic");
            var isDrunkVariant = hasDrunkOnScript && !hasLunaticOnScript;
            option = new OfferOption(normalizedRole, new HiddenFlags(isDrunkVariant, !isDrunkVariant), normalizedDisguise, string.Empty);
        }
        else if (string.Equals(normalizedRole, "alchemist", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedRole, "boffin", StringComparison.OrdinalIgnoreCase))
        {
            if (!CurrentSession.Script.Characters.Any(character => string.Equals(character.Id, normalizedRole, StringComparison.OrdinalIgnoreCase)))
            {
                SetDraftMessage($"{normalizedRole} is not on this script.");
                return;
            }

            option = new OfferOption(normalizedRole, new HiddenFlags(false, false), string.Empty, normalizedDisguise);
            _curatedDynamicAbilitySelections[normalizedRole] = normalizedDisguise;
        }
        else
        {
            SetDraftMessage("Special option role must be drunk, lunatic, hermit, alchemist, or boffin.");
            return;
        }

        if (_curatedOfferSelection.Any(existing => existing == option))
        {
            return;
        }

        _curatedOfferSelection.Add(option);
        ClearDraftMessage();
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
        if (CurrentSession.IsLegionGame
            && string.Equals(normalized, EvilSentinelCharacterId, StringComparison.OrdinalIgnoreCase))
        {
            return CreateEvilSentinelCharacter();
        }

        var fromScript = LoadResult.Script.Characters.FirstOrDefault(character => string.Equals(character.Id, normalized, StringComparison.OrdinalIgnoreCase));
        return fromScript ?? _characterDatabase.Resolve(normalized);
    }

    private static string GetPresentedCharacterId(OfferOption option)
    {
        return string.IsNullOrWhiteSpace(option.DisguiseCharacterId)
            ? option.CharacterId
            : option.DisguiseCharacterId;
    }

    private void SyncOfferFromCurrentSlot()
    {
        _currentOfferOptions.Clear();

        var slot = CurrentSession.Players
            .FirstOrDefault(player => player.Choice is not PlayerChoice.ChosenChoice);

        if (slot?.Choice is PlayerChoice.UnchosenChoice unchosen && unchosen.OfferedOptions.Count > 0)
        {
            _currentOfferOptions.AddRange(unchosen.OfferedOptions);
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
        CurrentSession = new GameSession(Guid.Empty, LoadResult.Script, PlayerCount, Array.Empty<PlayerSlot>(), GameStatus.Unknown, ActiveLoricIds, UseMarionette, UseAtheist, IsLegionGame, LegionCount);
        ResetOfferState();
        ClearDraftMessage();
    }

    private void ResetOfferState()
    {
        _currentOfferOptions.Clear();
        _curatedOfferSelection.Clear();
        _curatedDynamicAbilitySelections.Clear();
        IsCuratingOffer = false;
        CuratedRoleCharacterId = string.Empty;
        CuratedDisguiseCharacterId = string.Empty;
    }

    private CharacterDefinition CreateEvilSentinelCharacter()
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
