namespace TheCuratool.Infrastructure.Entities;

public sealed class PlayerSlotEntity
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    public GameSessionEntity GameSession { get; set; } = null!;

    public int DraftOrder { get; set; }

    public required Guid PlayerId { get; set; }

    public string ChosenCharacterId { get; set; } = string.Empty;

    public required string OfferedCharacterIds { get; set; } // JSON array

    public required string HiddenFlags { get; set; } // JSON object: { "isDrunk": bool, "isLunatic": bool }

    public bool IsAtheistCommitmentConfirmed { get; set; }
}
