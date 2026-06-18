namespace HackerMinigames.Api.Models;

// ---- Server-side mutable state (plaintext & target never leave the server) ---

public class CipherWheelGameState
{
    public List<CipherSentence> Sentences { get; set; } = [];
    public bool Solved { get; set; }
}

public class CipherSentence
{
    public required string Id { get; set; }
    /// <summary>
    /// Each letter at position p belongs to group g = p % N.
    /// It is encoded using the sum of TargetWheelValues for all wheels EXCEPT wheel g,
    /// so that letter needs every wheel except its own to be correctly set.
    /// </summary>
    public required string EncodedText { get; set; }
    public required string Plaintext { get; set; }        // never sent to clients
    public required int[] TargetWheelValues { get; set; } // never sent to clients; one per wheel
    public List<SentenceWheel> Wheels { get; set; } = [];
    public bool Solved { get; set; }
}

public class SentenceWheel
{
    public required string Id { get; set; }
    public int Value { get; set; }           // 0–25
    public string? LastTouchedBy { get; set; }
}

// ---- Client-facing DTOs (no secrets) ----------------------------------------
//
// The client gets encodedText + all wheel values and decodes locally:
//   decodedText = rotate(encodedText, sum(wheelValues) % 26)
// Closeness (0–1) drives the text colour — no per-letter locks, no hints.

public record CipherWheelGameDto(List<CipherSentenceDto> Sentences, bool Solved);

public record CipherSentenceDto(
    string Id,
    string EncodedText,
    string[] WheelIds,
    int[] WheelValues,
    string?[] LastTouchedBy,
    bool Solved,
    double Closeness   // 0 = far from solution, 1 = solved
);
