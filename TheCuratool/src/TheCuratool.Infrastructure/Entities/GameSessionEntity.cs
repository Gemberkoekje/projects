namespace TheCuratool.Infrastructure.Entities;

public sealed class GameSessionEntity
{
    public Guid Id { get; set; }

    public Guid ScriptId { get; set; }

    public ScriptEntity Script { get; set; } = null!;

    public int PlayerCount { get; set; }

    public required string ActiveLorics { get; set; } // JSON array

    public bool UseMarionette { get; set; }

    public bool UseAtheist { get; set; }

    public bool IsLegionGame { get; set; }

    public int LegionCount { get; set; }

    public int Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<PlayerSlotEntity> PlayerSlots { get; set; } = new List<PlayerSlotEntity>();
}
