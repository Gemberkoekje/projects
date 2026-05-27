namespace TheCuratool.Infrastructure.Repositories;

using TheCuratool.Application;
using TheCuratool.Application.Abstractions.Repositories;

public sealed class GameSessionRepository(CuratoolDbContext dbContext, CharacterDatabase characterDatabase) : IGameSessionRepository
{
    public async Task<GameSession> AddAsync(GameSession session, Guid scriptId, CancellationToken cancellationToken = default)
    {
        var scriptEntity = await EnsureScriptEntityAsync(scriptId, cancellationToken);

        var sessionEntity = new GameSessionEntity
        {
            Id = session.Id,
            ScriptId = scriptEntity.Id,
            Script = scriptEntity,
            PlayerCount = session.PlayerCount,
            ActiveLorics = JsonSerializer.Serialize(session.ActiveLoricIds),
            UseMarionette = session.UseMarionette,
            Status = (int)session.Status,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var playerSlot in session.Players)
        {
            sessionEntity.PlayerSlots.Add(MapPlayerSlotToEntity(playerSlot, session.Id));
        }

        dbContext.GameSessions.Add(sessionEntity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<GameSession> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.GameSessions
            .Include(g => g.Script)
            .Include(g => g.PlayerSlots)
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken) ??
            throw new InvalidOperationException($"GameSession with id {id} not found.");

        return MapEntityToGameSession(entity);
    }

    public async Task<GameSession> UpdateAsync(GameSession session, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.GameSessions
            .Include(g => g.PlayerSlots)
            .FirstOrDefaultAsync(g => g.Id == session.Id, cancellationToken) ??
            throw new InvalidOperationException($"GameSession with id {session.Id} not found.");

        entity.Status = (int)session.Status;
        entity.UseMarionette = session.UseMarionette;
        entity.ActiveLorics = JsonSerializer.Serialize(session.ActiveLoricIds);

        var existingSlots = entity.PlayerSlots.ToList();
        var incomingSlots = session.Players.ToList();

        foreach (var existingSlot in existingSlots)
        {
            var incomingSlot = incomingSlots.FirstOrDefault(slot => slot.DraftOrder == existingSlot.DraftOrder);
            if (incomingSlot is null)
            {
                entity.PlayerSlots.Remove(existingSlot);
                continue;
            }

            UpdatePlayerSlotEntity(existingSlot, incomingSlot);
        }

        foreach (var incomingSlot in incomingSlots)
        {
            if (!existingSlots.Any(slot => slot.DraftOrder == incomingSlot.DraftOrder))
            {
                entity.PlayerSlots.Add(MapPlayerSlotToEntity(incomingSlot, session.Id));
            }
        }

        dbContext.GameSessions.Update(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ScriptEntity> EnsureScriptEntityAsync(Guid scriptId, CancellationToken cancellationToken)
    {
        var scriptEntity = await dbContext.Scripts.FirstOrDefaultAsync(script => script.Id == scriptId, cancellationToken);
        if (scriptEntity is not null)
        {
            return scriptEntity;
        }

        if (!StandardScriptCatalog.TryGetById(scriptId, out var standardScript))
        {
            throw new InvalidOperationException($"Script with id {scriptId} not found.");
        }

        scriptEntity = new ScriptEntity
        {
            Id = standardScript.Id,
            Name = standardScript.Name,
            Author = standardScript.Author,
            RawJson = standardScript.RawJson,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        dbContext.Scripts.Add(scriptEntity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return scriptEntity;
    }

    private GameSession MapEntityToGameSession(GameSessionEntity entity)
    {
        var parser = new ScriptParser();
        var parsed = parser.Parse(entity.Script.RawJson, characterDatabase);
        var script = parsed.IsSuccess
            ? parsed.Script
            : new Script(entity.Script.Name, entity.Script.Author, Array.Empty<CharacterDefinition>());

        var activeLorics = JsonSerializer.Deserialize<string[]>(entity.ActiveLorics) ?? Array.Empty<string>();
        var playerSlots = entity.PlayerSlots
            .OrderBy(slot => slot.DraftOrder)
            .Select(MapPlayerSlotEntity)
            .ToList()
            .AsReadOnly();

        return new GameSession(
            entity.Id,
            script,
            entity.PlayerCount,
            playerSlots,
            (GameStatus)entity.Status,
            activeLorics,
            entity.UseMarionette);
    }

    private static void UpdatePlayerSlotEntity(PlayerSlotEntity entity, PlayerSlot slot)
    {
        entity.PlayerId = slot.PlayerId;

        if (slot.Choice is PlayerChoice.UnchosenChoice unchosen)
        {
            entity.ChosenCharacterId = string.Empty;
            entity.OfferedCharacterIds = JsonSerializer.Serialize(unchosen.OfferedIds);
            entity.IsAtheistCommitmentConfirmed = unchosen.IsAtheistCommitmentConfirmed;
            entity.HiddenFlags = JsonSerializer.Serialize(new HiddenFlags(false, false));
            return;
        }

        var chosen = (PlayerChoice.ChosenChoice)slot.Choice;
        entity.ChosenCharacterId = chosen.CharacterId;
        entity.OfferedCharacterIds = JsonSerializer.Serialize(chosen.OfferedIds);
        entity.HiddenFlags = JsonSerializer.Serialize(chosen.HiddenFlags);
        entity.IsAtheistCommitmentConfirmed = false;
    }

    private static PlayerSlotEntity MapPlayerSlotToEntity(PlayerSlot slot, Guid sessionId)
    {
        var entity = new PlayerSlotEntity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            DraftOrder = slot.DraftOrder,
            PlayerId = slot.PlayerId,
            OfferedCharacterIds = JsonSerializer.Serialize(Array.Empty<string>()),
            HiddenFlags = JsonSerializer.Serialize(new HiddenFlags(false, false)),
            IsAtheistCommitmentConfirmed = false,
        };

        if (slot.Choice is PlayerChoice.UnchosenChoice unchosen)
        {
            entity.OfferedCharacterIds = JsonSerializer.Serialize(unchosen.OfferedIds);
            entity.IsAtheistCommitmentConfirmed = unchosen.IsAtheistCommitmentConfirmed;
            return entity;
        }

        var chosen = (PlayerChoice.ChosenChoice)slot.Choice;
        entity.ChosenCharacterId = chosen.CharacterId;
        entity.OfferedCharacterIds = JsonSerializer.Serialize(chosen.OfferedIds);
        entity.HiddenFlags = JsonSerializer.Serialize(chosen.HiddenFlags);
        return entity;
    }

    private static PlayerSlot MapPlayerSlotEntity(PlayerSlotEntity entity)
    {
        var offeredIds = JsonSerializer.Deserialize<string[]>(entity.OfferedCharacterIds) ?? Array.Empty<string>();
        if (string.IsNullOrEmpty(entity.ChosenCharacterId))
        {
            return new PlayerSlot(entity.DraftOrder, entity.PlayerId, new PlayerChoice.UnchosenChoice(offeredIds, entity.IsAtheistCommitmentConfirmed));
        }

        var hiddenFlags = JsonSerializer.Deserialize<HiddenFlags>(entity.HiddenFlags) ?? new HiddenFlags(false, false);
        return new PlayerSlot(entity.DraftOrder, entity.PlayerId, new PlayerChoice.ChosenChoice(entity.ChosenCharacterId, offeredIds, hiddenFlags));
    }
}
