namespace HackerMinigames.Api.Models;

// ---- Server-side mutable state (Solution never leaves the server) -----------

public class PacketSequencerState
{
    public List<PacketColumn> Columns { get; set; } = [];
    public bool Solved { get; set; }
}

public class PacketColumn
{
    public required string Id { get; set; }
    public required string Label { get; set; }
    public List<string> Items { get; set; } = [];     // current (shuffled) order
    public List<string> Solution { get; set; } = [];  // correct order, never sent to client
    public string? LastTouchedBy { get; set; }
    public bool Solved { get; set; }
}

// ---- Client-facing DTOs ----------------------------------------------------

public record PacketSequencerDto(List<PacketColumnDto> Columns, bool Solved);

public record PacketColumnDto(
    string Id,
    string Label,
    string[] Items,
    bool Solved,
    string? LastTouchedBy
);
