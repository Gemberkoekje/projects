namespace HackerMinigames.Api.Models;

// ---- Server-side mutable state -------------------------------------------

public class MemoryLeakState
{
    public int Rows { get; set; }
    public int Cols { get; set; }
    /// <summary>Fixed hex byte per cell — visible to clients, never changes.</summary>
    public int[][] Values { get; set; } = [];
    /// <summary>Run-length clues per row (e.g. [1, 4]) — sent to client.</summary>
    public int[][] RowClues { get; set; } = [];
    /// <summary>Run-length clues per column — sent to client.</summary>
    public int[][] ColClues { get; set; } = [];
    /// <summary>Correct on/off pattern — server-only, used for win check.</summary>
    public bool[][] Solution { get; set; } = [];
    /// <summary>Current toggle state — the only mutable part of the puzzle.</summary>
    public bool[][] On { get; set; } = [];
    public string?[][] ToggledBy { get; set; } = [];
    public bool Solved { get; set; }
}

// ---- Client-facing DTO ---------------------------------------------------
// Solution is omitted. Clients solve from RowClues/ColClues like a nonogram.

public record MemoryLeakDto(
    int Rows,
    int Cols,
    int[][] Values,
    int[][] RowClues,
    int[][] ColClues,
    bool[][] On,
    string?[][] ToggledBy,
    bool Solved
);

// ---- Toggle result --------------------------------------------------------

public record ToggleCellResult(
    string Colour,
    int Row,
    int Col,
    bool IsOn,
    bool BoardSolved
);
