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
        Assert.Contains(state.ActiveLoricIds, id => string.Equals(id, "the_curator", StringComparison.OrdinalIgnoreCase));
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

        Assert.True(state.IsLoricActive("the_curator"));
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
