using Microsoft.EntityFrameworkCore;
using TheCuratool.Infrastructure.Data;
using TheCuratool.Infrastructure.Repositories;

namespace TheCuratool.UnitTests;

public sealed class GameSessionPersistenceTests : IDisposable
{
    private readonly CuratoolDbContext _dbContext;
    private readonly GameSessionRepository _repository;
    private readonly Guid _scriptId;

    public GameSessionPersistenceTests()
    {
        var options = new DbContextOptionsBuilder<CuratoolDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new CuratoolDbContext(options);
        _repository = new GameSessionRepository(_dbContext, new CharacterDatabase(new Dictionary<string, CharacterDefinition>()));
        _scriptId = SeedScript();
    }

    [Fact]
    public async Task AddAsync_ShouldPersistGameSession()
    {
        // Arrange
        var script = new Script("Test Script", "Author", Array.Empty<CharacterDefinition>());
        var players = new List<PlayerSlot>
        {
            new PlayerSlot(0, Guid.NewGuid(), PlayerChoice.UnchosenChoice.Empty),
            new PlayerSlot(1, Guid.NewGuid(), PlayerChoice.UnchosenChoice.Empty),
        };
        var session = new GameSession(
            Guid.NewGuid(),
            script,
            2,
            players,
            GameStatus.Drafting,
            Array.Empty<string>(),
            false);

        var addedSession = await _repository.AddAsync(session, _scriptId);

        // Assert
        Assert.NotNull(addedSession);
        Assert.Equal(session.Id, addedSession.Id);
        Assert.Equal(GameStatus.Drafting, addedSession.Status);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldRetrievePersistedGameSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var script = new Script("Test Script", "Author", Array.Empty<CharacterDefinition>());
        var players = new List<PlayerSlot>
        {
            new PlayerSlot(0, Guid.NewGuid(), PlayerChoice.UnchosenChoice.Empty),
        };
        var session = new GameSession(
            sessionId,
            script,
            1,
            players,
            GameStatus.Drafting,
            Array.Empty<string>(),
            false);

        await _repository.AddAsync(session, _scriptId);

        // Act
        var retrievedSession = await _repository.GetByIdAsync(sessionId);

        // Assert
        Assert.NotNull(retrievedSession);
        Assert.Equal(sessionId, retrievedSession.Id);
        Assert.Equal(1, retrievedSession.PlayerCount);
        Assert.Single(retrievedSession.Players);
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateGameSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var script = new Script("Test Script", "Author", Array.Empty<CharacterDefinition>());
        var players = new List<PlayerSlot>
        {
            new PlayerSlot(0, Guid.NewGuid(), PlayerChoice.UnchosenChoice.Empty),
        };
        var session = new GameSession(
            sessionId,
            script,
            1,
            players,
            GameStatus.Drafting,
            Array.Empty<string>(),
            false);

        await _repository.AddAsync(session, _scriptId);

        // Update session status
        var playerId = players[0].PlayerId;
        var updatedPlayers = new List<PlayerSlot>
        {
            new PlayerSlot(0, playerId, new PlayerChoice.ChosenChoice("baron", Array.Empty<string>(), new HiddenFlags(false, false))),
        };
        var completedSession = new GameSession(
            sessionId,
            script,
            1,
            updatedPlayers,
            GameStatus.Completed,
            Array.Empty<string>(),
            false);

        // Act
        await _repository.UpdateAsync(completedSession);

        // Assert
        var retrievedSession = await _repository.GetByIdAsync(sessionId);
        Assert.Equal(GameStatus.Completed, retrievedSession.Status);
        Assert.Single(retrievedSession.Players);
        Assert.True(retrievedSession.Players[0].IsChosen);
    }

    [Fact]
    public async Task RecordChoice_WithHiddenFlags_ShouldPersistFlags()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var script = new Script("Test Script", "Author", Array.Empty<CharacterDefinition>());
        var playerId = Guid.NewGuid();
        var players = new List<PlayerSlot>
        {
            new PlayerSlot(0, playerId, PlayerChoice.UnchosenChoice.Empty),
        };
        var session = new GameSession(
            sessionId,
            script,
            1,
            players,
            GameStatus.Drafting,
            Array.Empty<string>(),
            false);

        await _repository.AddAsync(session, _scriptId);

        // Record a choice with hidden flags
        var updatedPlayers = new List<PlayerSlot>
        {
            new PlayerSlot(
                0,
                playerId,
                new PlayerChoice.ChosenChoice(
                    "drunk",
                    new[] { "drunk", "gossip" },
                    new HiddenFlags(true, false))),
        };
        var chosenSession = new GameSession(
            sessionId,
            script,
            1,
            updatedPlayers,
            GameStatus.Drafting,
            Array.Empty<string>(),
            false);

        // Act
        await _repository.UpdateAsync(chosenSession);

        // Assert
        var retrievedSession = await _repository.GetByIdAsync(sessionId);
        var playerSlot = retrievedSession.Players[0];
        Assert.True(playerSlot.IsChosen);

        if (playerSlot.Choice is PlayerChoice.ChosenChoice chosenChoice)
        {
            Assert.Equal("drunk", chosenChoice.CharacterId);
            Assert.True(chosenChoice.HiddenFlags.IsDrunk);
            Assert.False(chosenChoice.HiddenFlags.IsLunatic);
        }
        else
        {
            Assert.Fail("Expected ChosenChoice");
        }
    }

    [Fact]
    public async Task Persistence_WithActiveLorics_ShouldMaintainLoricList()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var script = new Script("Test Script", "Author", Array.Empty<CharacterDefinition>());
        var players = new List<PlayerSlot>
        {
            new PlayerSlot(0, Guid.NewGuid(), PlayerChoice.UnchosenChoice.Empty),
        };
        var activeLorics = new[] { "sentinel", "socialite" };
        var session = new GameSession(
            sessionId,
            script,
            1,
            players,
            GameStatus.Drafting,
            activeLorics,
            true);

        // Act
        await _repository.AddAsync(session, _scriptId);
        var retrievedSession = await _repository.GetByIdAsync(sessionId);

        // Assert
        Assert.Equal(activeLorics, retrievedSession.ActiveLoricIds);
        Assert.True(retrievedSession.UseMarionette);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
    }

    private Guid SeedScript()
    {
        var scriptId = Guid.NewGuid();
        _dbContext.Scripts.Add(new TheCuratool.Infrastructure.Entities.ScriptEntity
        {
            Id = scriptId,
            Name = "Test Script",
            Author = "Author",
            RawJson = "[]",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _dbContext.SaveChanges();
        return scriptId;
    }
}
