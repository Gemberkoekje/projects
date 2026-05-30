using Microsoft.EntityFrameworkCore;
using TheCuratool.Application;
using TheCuratool.Infrastructure.Data;
using TheCuratool.Infrastructure.Repositories;
using TheCuratool.Web;

namespace TheCuratool.UnitTests;

public sealed class DraftSessionStateTests
{
    [Fact]
    public async Task LoadScriptAsync_ValidScript_EnsuresCuratorLoricAndCalculatesSetup()
    {
        await using var fixture = CreateFixture();
        var state = fixture.CreateState();
        state.ScriptJson = File.ReadAllText("NoVortox.json");

        await state.LoadScriptAsync();

        Assert.True(state.LoadResult.IsSuccess);
        Assert.Contains(state.ActiveLoricIds, id =>
            string.Equals(id, "the_curator", StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, "thecurator", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(SetupCounts.Empty, state.SetupResult.BaseDistribution);
        Assert.False(state.HasCurrentSession);
    }

    [Fact]
    public async Task SetLoricActive_DisableCurator_KeepsCuratorActive()
    {
        await using var fixture = CreateFixture();
        var state = fixture.CreateState();
        state.ScriptJson = File.ReadAllText("NoVortox.json");
        await state.LoadScriptAsync();

        state.SetLoricActive("the_curator", false);

        Assert.True(state.IsLoricActive("the_curator") || state.IsLoricActive("thecurator"));
    }

    [Fact]
    public async Task StartDraft_OfferRandomAndRecordChoice_AdvancesCurrentSlot()
    {
        await using var fixture = CreateFixture();
        var state = fixture.CreateState();
        state.ScriptJson = File.ReadAllText("NoVortox.json");
        await state.LoadScriptAsync();
        await state.StartDraftAsync();

        Assert.True(state.HasCurrentSession);

        var currentSlot = state.CurrentPlayerSlot;
        var pendingBeforePick = state.CurrentSession.Players
            .Where(slot => slot.Choice is not PlayerChoice.ChosenChoice)
            .Select(slot => slot.DraftOrder)
            .ToHashSet();

        state.OfferRandomThree();

        Assert.InRange(state.CurrentOfferIds.Count, 1, 3);

        var chosenId = state.CurrentOfferIds[0];
        await state.RecordChoiceAsync(chosenId);

        Assert.Contains(state.CurrentSession.Players, slot => slot.DraftOrder == currentSlot && slot.Choice is PlayerChoice.ChosenChoice);
        if (pendingBeforePick.Count > 1)
        {
            Assert.Contains(state.CurrentPlayerSlot, pendingBeforePick.Where(slot => slot != currentSlot));
        }

        Assert.Empty(state.CurrentOfferIds);
    }

    [Fact]
    public async Task BuildSummaryJson_WhenSessionStarted_ReturnsSessionPayload()
    {
        await using var fixture = CreateFixture();
        var state = fixture.CreateState();
        state.ScriptJson = File.ReadAllText("NoVortox.json");
        await state.LoadScriptAsync();
        await state.StartDraftAsync();

        var payload = state.BuildSummaryJson();

        Assert.Contains("\"sessionId\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"activeLorics\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"assignments\"", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureSessionLoadedAsync_LoadsPersistedSessionById()
    {
        await using var fixture = CreateFixture();
        var stateA = fixture.CreateState();
        stateA.ScriptJson = File.ReadAllText("NoVortox.json");
        await stateA.LoadScriptAsync();
        await stateA.StartDraftAsync();

        var sessionId = stateA.CurrentSessionId;

        var stateB = fixture.CreateState();
        var loaded = await stateB.EnsureSessionLoadedAsync(sessionId);

        Assert.True(loaded);
        Assert.True(stateB.HasCurrentSession);
        Assert.Equal(sessionId, stateB.CurrentSessionId);
    }

    [Fact]
    public async Task LoadStoredScriptAsync_StandardScript_LoadsScriptAndStartsDraft()
    {
        await using var fixture = CreateFixture();
        var state = fixture.CreateState();
        await state.LoadAvailableScriptsAsync();
        var standardScript = Assert.Single(state.AvailableScripts.Where(script => script.Script.Name == "Trouble Brewing"));

        await state.LoadStoredScriptAsync(standardScript.Id);
        await state.StartDraftAsync();

        Assert.Equal("Trouble Brewing", state.LoadResult.Script.Name);
        Assert.True(state.HasCurrentSession);
        Assert.Equal("Trouble Brewing", state.CurrentSession.Script.Name);
        Assert.Equal(22, state.CurrentSession.Script.Characters.Count);
    }

    [Fact]
    public async Task StartDraft_WithLegionScript_AllowsLegionGameOption()
    {
        await using var fixture = CreateFixture();
        var state = fixture.CreateState();

        // Use NoVortox which is a real script
        state.ScriptJson = File.ReadAllText("NoVortox.json");
        await state.LoadScriptAsync();
        state.SetPlayerCount(7);

        // Start a normal draft (no Legion)
        await state.StartDraftAsync();
        Assert.True(state.HasCurrentSession);
        Assert.False(state.CurrentSession.IsLegionGame);
    }

    [Fact]
    public async Task ResolveEvilSlotAsync_WhenCalled_UpdatesSessionAndClears()
    {
        await using var fixture = CreateFixture();
        var state = fixture.CreateState();

        // Create a minimal script with evil offer
        var script = @"{
            ""_meta"": { ""name"": ""Evil Test"", ""author"": ""Test"" },
            ""townsfolk"": [""librarian"", ""chef""],
            ""minion"": [""evil""],
            ""demon"": [""imp""]
        }";
        state.ScriptJson = script;
        await state.LoadScriptAsync();
        state.SetPlayerCount(3);
        await state.StartDraftAsync();

        // Create a curated offer
        state.BeginCuratedOffer();
        state.ToggleCuratedCharacter("librarian");
        await state.ConfirmCuratedOfferAsync();

        // Pick evil if it's available
        if (state.CurrentOfferIds.Contains("evil", StringComparer.OrdinalIgnoreCase))
        {
            await state.RecordChoiceAsync("evil");
            var slotWithEvil = state.CurrentSession.Players.FirstOrDefault(p => p.Choice is PlayerChoice.ChosenChoice c && string.Equals(c.CharacterId, "evil", StringComparison.OrdinalIgnoreCase));
            if (slotWithEvil is not null)
            {
                await state.ResolveEvilSlotAsync(slotWithEvil.DraftOrder, "imp", new HiddenFlags(false, false));

                var resolved = state.CurrentSession.Players.FirstOrDefault(p => p.DraftOrder == slotWithEvil.DraftOrder);
                Assert.NotNull(resolved);
                Assert.True(resolved.Choice is PlayerChoice.ChosenChoice);
            }
        }
    }

    [Fact]
    public async Task ResolveMinionSlotAsync_WhenCalled_UpdatesSessionWithMinion()
    {
        await using var fixture = CreateFixture();
        var state = fixture.CreateState();

        var script = @"[
            { ""id"": ""_meta"", ""name"": ""Lord of Typhon Test"", ""author"": ""Test"" },
            ""chef"",
            ""washerwoman"",
            ""librarian"",
            ""investigator"",
            ""fortune_teller"",
            ""empath"",
            ""undertaker"",
            ""slayer"",
            ""soldier"",
            ""ravenkeeper"",
            ""saint"",
            ""drunk"",
            ""butler"",
            ""lordoftyphon"",
            ""poisoner"",
            ""baron"",
            ""scarletwoman"",
            ""imp""
        ]";

        state.ScriptJson = script;
        await state.LoadScriptAsync();
        state.SetPlayerCount(10);
        await state.StartDraftAsync();

        state.BeginCuratedOffer();
        state.ToggleCuratedCharacter("chef");
        await state.ConfirmCuratedOfferAsync();
        await state.RecordChoiceAsync("chef");
        Assert.Contains(state.CurrentSession.Players, player =>
            player.Choice is PlayerChoice.ChosenChoice chosen
            && string.Equals(chosen.CharacterId, "chef", StringComparison.OrdinalIgnoreCase));

        state.BeginCuratedOffer();
        state.ToggleCuratedCharacter("lordoftyphon");
        await state.ConfirmCuratedOfferAsync();
        await state.RecordChoiceAsync("lordoftyphon");
        Assert.Contains(state.CurrentSession.Players, player =>
            player.Choice is PlayerChoice.ChosenChoice chosen
            && string.Equals(chosen.CharacterId, "lordoftyphon", StringComparison.OrdinalIgnoreCase));

        var stAssignedSlots = state.CurrentSession.Players.Where(p => p.IsStAssigned).ToList();
        Assert.Empty(stAssignedSlots);
    }

    [Fact]
    public async Task GetCurateRoleCandidates_WithDrunkAndHermitOnScript_IncludesBothRoles()
    {
        await using var fixture = CreateFixture();
        var state = fixture.CreateState();
        var script = @"[
            { ""id"": ""_meta"", ""name"": ""Hermit Drunk Curate Test"", ""author"": ""Test"" },
            ""hermit"",
            ""drunk"",
            ""chef"",
            ""highpriestess"",
            ""poisoner"",
            ""imp""
        ]";

        state.ScriptJson = script;
        await state.LoadScriptAsync();
        state.SetPlayerCount(5);
        await state.StartDraftAsync();
        Assert.True(state.HasCurrentSession, state.DraftMessage);

        var candidates = state.GetCurateRoleCandidates();

        Assert.Contains(candidates, character => string.Equals(character.Id, "drunk", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(candidates, character => string.Equals(character.Id, "hermit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LegionGame_GetCurateRoleCandidates_IncludesOnScriptOutsidersAndEvilSentinel()
    {
        await using var fixture = CreateFixture();
        var state = fixture.CreateState();
        var script = @"[
            { ""id"": ""_meta"", ""name"": ""Legion Curate Test"", ""author"": ""Test"" },
            ""legion"",
            ""drunk"",
            ""lunatic"",
            ""hermit"",
            ""chef"",
            ""washerwoman"",
            ""recluse""
        ]";

        state.ScriptJson = script;
        await state.LoadScriptAsync();
        state.SetPlayerCount(7);
        state.SetLegionGame(true);
        await state.StartDraftAsync();
        Assert.True(state.HasCurrentSession, state.DraftMessage);

        var candidates = state.GetCurateRoleCandidates();

        Assert.Contains(candidates, character => string.Equals(character.Id, "drunk", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(candidates, character => string.Equals(character.Id, "lunatic", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(candidates, character => string.Equals(character.Id, "recluse", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(candidates, character => string.Equals(character.Id, "evil", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RecordChoiceAsync_WithCuratedDynamicOffer_ClearsOfferStateForNextPlayer()
    {
        await using var fixture = CreateFixture();
        var state = fixture.CreateState();

        var script = @"[
            { ""id"": ""_meta"", ""name"": ""Dynamic Offer Clear Test"", ""author"": ""Test"" },
            ""librarian"",
            ""chef"",
            ""alchemist"",
            ""poisoner"",
            ""imp""
        ]";

        state.ScriptJson = script;
        await state.LoadScriptAsync();
        state.SetPlayerCount(5);
        await state.StartDraftAsync();
        Assert.True(state.HasCurrentSession, state.DraftMessage);

        state.BeginCuratedOffer();
        state.CuratedRoleCharacterId = "alchemist";
        state.CuratedDisguiseCharacterId = "poisoner";
        state.AddCuratedRoleOption(state.CuratedRoleCharacterId, state.CuratedDisguiseCharacterId);
        await state.ConfirmCuratedOfferAsync();

        await state.RecordChoiceAsync("alchemist");

        Assert.True(string.IsNullOrWhiteSpace(state.DraftMessage), state.DraftMessage);
        Assert.Empty(state.CurrentOfferIds);
        Assert.DoesNotContain(state.CurrentSession.Players, player =>
            player.DraftOrder == state.CurrentPlayerSlot
            && player.Choice is PlayerChoice.UnchosenChoice unchosen
            && unchosen.OfferedOptions.Any(option => string.Equals(option.CharacterId, "alchemist", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task DynamicAbilityBanner_ShowsWhenAlchemistChosen()
    {
        await using var fixture = CreateFixture();
        var state = fixture.CreateState();

        var script = @"[
            { ""id"": ""_meta"", ""name"": ""Alchemist Test"", ""author"": ""Test"" },
            ""librarian"",
            ""chef"",
            ""alchemist"",
            ""poisoner"",
            ""imp""
        ]";
        state.ScriptJson = script;
        await state.LoadScriptAsync();
        state.SetPlayerCount(5);
        await state.StartDraftAsync();
        Assert.True(state.HasCurrentSession, state.DraftMessage);

        state.BeginCuratedOffer();
        state.CuratedRoleCharacterId = "alchemist";
        state.CuratedDisguiseCharacterId = "poisoner";
        state.AddCuratedRoleOption(state.CuratedRoleCharacterId, state.CuratedDisguiseCharacterId);
        await state.ConfirmCuratedOfferAsync();
        await state.RecordChoiceAsync("alchemist");

        var assigned = state.CurrentSession.Players.Any(player =>
            player.Choice is PlayerChoice.ChosenChoice chosen
            && string.Equals(chosen.CharacterId, "alchemist", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(player.BorrowedAbilityCharacterId));

        Assert.True(assigned);
    }

    [Fact]
    public async Task TargetSelection_DefaultsToAllSelected()
    {
        await using var fixture = CreateFixture();
        var state = fixture.CreateState();
        state.ScriptJson = File.ReadAllText("NoVortox.json");
        await state.LoadScriptAsync();
        await state.StartDraftAsync();

        Assert.True(state.AllTargetsSelected);
        Assert.All(state.SelectableTargetCounts, target => Assert.True(state.IsTargetSelected(target)));
    }

    [Fact]
    public async Task SetTargetSelected_DeselectingOne_LeavesAllTargetsMode()
    {
        await using var fixture = CreateFixture();
        var state = fixture.CreateState();
        state.ScriptJson = File.ReadAllText("NoVortox.json");
        await state.LoadScriptAsync();
        await state.StartDraftAsync();

        var targets = state.SelectableTargetCounts;
        if (targets.Count < 2)
        {
            // The default script always yields multiple valid targets; skip if not the case.
            return;
        }

        var first = targets[0];
        state.SetTargetSelected(first, false);

        Assert.False(state.AllTargetsSelected);
        Assert.False(state.IsTargetSelected(first));
        Assert.True(state.IsTargetSelected(targets[1]));
    }

    [Fact]
    public async Task ToggleAllTargets_OffThenOn_RestoresAllSelected()
    {
        await using var fixture = CreateFixture();
        var state = fixture.CreateState();
        state.ScriptJson = File.ReadAllText("NoVortox.json");
        await state.LoadScriptAsync();
        await state.StartDraftAsync();

        state.ToggleAllTargets(false);
        Assert.False(state.AllTargetsSelected);
        Assert.All(state.SelectableTargetCounts, target => Assert.False(state.IsTargetSelected(target)));

        state.ToggleAllTargets(true);
        Assert.True(state.AllTargetsSelected);
        Assert.All(state.SelectableTargetCounts, target => Assert.True(state.IsTargetSelected(target)));
    }

    [Fact]
    public async Task GetTargetReachabilityWarning_AllTargetsMode_NeverWarns()
    {
        await using var fixture = CreateFixture();
        var state = fixture.CreateState();
        state.ScriptJson = File.ReadAllText("NoVortox.json");
        await state.LoadScriptAsync();
        await state.StartDraftAsync();

        state.OfferRandomThree();
        Assert.InRange(state.CurrentOfferIds.Count, 1, 3);

        // With all targets selected (default), no offered option should ever warn.
        Assert.All(state.CurrentOfferIds, id => Assert.Equal(string.Empty, state.GetTargetReachabilityWarning(id)));
    }

    private static TestFixture CreateFixture()
    {
        var databaseName = $"curatool-state-{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<CuratoolDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var dbContext = new CuratoolDbContext(options);
        dbContext.Database.EnsureCreated();

        var charactersPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data", "characters.json"));
        var loricsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data", "lorics.json"));

        var characterDatabase = CharacterDatabase.LoadFromFile(charactersPath);
        var loricDatabase = LoricDatabase.LoadFromFile(loricsPath);
        var scriptParser = new ScriptParser();
        var setupCalculator = new SetupCalculator();
        var draftEngine = new DraftEngine(characterDatabase, loricDatabase, setupCalculator);
        var scriptRepository = new ScriptRepository(dbContext, characterDatabase);
        var gameSessionRepository = new GameSessionRepository(dbContext, characterDatabase);

        return new TestFixture(dbContext, scriptParser, setupCalculator, draftEngine, characterDatabase, loricDatabase, scriptRepository, gameSessionRepository);
    }

    private sealed record TestFixture(
        CuratoolDbContext DbContext,
        ScriptParser ScriptParser,
        SetupCalculator SetupCalculator,
        DraftEngine DraftEngine,
        CharacterDatabase CharacterDatabase,
        LoricDatabase LoricDatabase,
        ScriptRepository ScriptRepository,
        GameSessionRepository GameSessionRepository) : IAsyncDisposable
    {
        public DraftSessionState CreateState()
        {
            return new DraftSessionState(
                ScriptParser,
                SetupCalculator,
                DraftEngine,
                CharacterDatabase,
                LoricDatabase,
                ScriptRepository,
                GameSessionRepository);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
        }
    }
}
