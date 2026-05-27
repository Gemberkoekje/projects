namespace TheCuratool.Infrastructure.Entities;

public sealed class ScriptEntity
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Author { get; set; }

    public required string RawJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<GameSessionEntity> GameSessions { get; set; } = new List<GameSessionEntity>();
}
