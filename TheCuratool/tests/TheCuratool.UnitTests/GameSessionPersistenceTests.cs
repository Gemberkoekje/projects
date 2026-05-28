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
            false,
            false,
            0);

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
            false,
            false,
            0);

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
            false,
            false,
            0);

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
            false,
            false,
            0);

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
            false,
            false,
            0);

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
            false,
            false,
            0);

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
            true,
            false,
            0);

        // Act
        await _repository.AddAsync(session, _scriptId);
        var retrievedSession = await _repository.GetByIdAsync(sessionId);

        // Assert
        Assert.Equal(activeLorics, retrievedSession.ActiveLoricIds);
        Assert.True(retrievedSession.UseMarionette);
    }

    [Fact]
    public async Task BorrowedAbilityCharacterId_RoundTrips_ForChosenChoice()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var script = new Script("Test Script", "Author", Array.Empty<CharacterDefinition>());
        var playerId = Guid.NewGuid();
        var players = new List<PlayerSlot>
        {
            new PlayerSlot(
                1,
                playerId,
                new PlayerChoice.ChosenChoice("alchemist", new[] { "alchemist" }, new HiddenFlags(false, false)),
                false,
                "baron"),
        };
        var session = new GameSession(
            sessionId,
            script,
            1,
            players,
            GameStatus.Completed,
            Array.Empty<string>(),
            false,
            false,
            0);

        await _repository.AddAsync(session, _scriptId);

        // Act
        var retrieved = await _repository.GetByIdAsync(sessionId);

        // Assert
        Assert.NotNull(retrieved);
        var slot = retrieved.Players.Single();
        Assert.True(slot.IsChosen);
        Assert.Equal("baron", slot.BorrowedAbilityCharacterId);

        if (slot.Choice is PlayerChoice.ChosenChoice chosen)
        {
            Assert.Equal("alchemist", chosen.CharacterId);
        }
        else
        {
            Assert.Fail("Expected ChosenChoice");
        }
    }

    [Fact]
    public async Task BorrowedAbilityCharacterId_PersistedOnUpdate_ForChosenChoice()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var script = new Script("Test Script", "Author", Array.Empty<CharacterDefinition>());
        var playerId = Guid.NewGuid();
        var initialPlayers = new List<PlayerSlot>
        {
            new PlayerSlot(1, playerId, PlayerChoice.UnchosenChoice.Empty),
        };
        var session = new GameSession(
            sessionId, script, 1, initialPlayers, GameStatus.Drafting,
            Array.Empty<string>(), false, false, 0);

        await _repository.AddAsync(session, _scriptId);

        // Update: assign a chosen choice with a borrowed ability
        var updatedPlayers = new List<PlayerSlot>
        {
            new PlayerSlot(
                1,
                playerId,
                new PlayerChoice.ChosenChoice("alchemist", new[] { "alchemist" }, new HiddenFlags(false, false)),
                false,
                "poisoner"),
        };
        var updatedSession = new GameSession(
            sessionId, script, 1, updatedPlayers, GameStatus.Completed,
            Array.Empty<string>(), false, false, 0);

        await _repository.UpdateAsync(updatedSession);

        // Act
        var retrieved = await _repository.GetByIdAsync(sessionId);

        // Assert
        Assert.NotNull(retrieved);
        var slot = retrieved.Players.Single();
        Assert.Equal("poisoner", slot.BorrowedAbilityCharacterId);
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
