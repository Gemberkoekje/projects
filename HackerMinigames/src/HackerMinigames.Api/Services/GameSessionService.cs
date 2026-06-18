using System.Collections.Concurrent;
using HackerMinigames.Api.Models;

namespace HackerMinigames.Api.Services;

/// <summary>
/// In-memory game-state manager. Replace with Redis + SignalR backplane before
/// scaling past one replica.
/// </summary>
public class GameSessionService
{
    private readonly ConcurrentDictionary<Guid, GameSession> _sessions = new();
    private readonly ConcurrentDictionary<string, Player> _players = new();

    private static readonly string[] PlayerColours =
    [
        "#00e5ff", "#ff5252", "#69f0ae", "#ffd740",
        "#e040fb", "#ff6e40", "#40c4ff", "#b2ff59",
    ];

    // ---- Players ---------------------------------------------------------

    public Player CreatePlayer()
    {
        var id = Guid.NewGuid().ToString();
        var colour = PlayerColours[Random.Shared.Next(PlayerColours.Length)];
        var player = new Player(id, colour);
        _players[id] = player;
        return player;
    }

    public string ColourFor(string playerId) =>
        _players.TryGetValue(playerId, out var p) ? p.Colour : "#ffffff";

    // ---- Sessions --------------------------------------------------------

    public Guid CreateGame(string gameType)
    {
        var id = Guid.NewGuid();
        var session = new GameSession(id, gameType) { LastActivity = DateTime.UtcNow };
        switch (gameType)
        {
            case "cipher-wheel":     session.CipherWheel     = BuildCipherWheelGame(); break;
            case "packet-sequencer": session.PacketSequencer = BuildPacketSequencer(); break;
            case "memory-leak":      session.MemoryLeak      = BuildMemoryLeak();      break;
        }
        _sessions[id] = session;
        return id;
    }

    public bool TryGetSession(Guid gameId, out GameSession session) =>
        _sessions.TryGetValue(gameId, out session!);

    // ---- Cipher Wheel ----------------------------------------------------

    public CipherWheelGameDto? GetCipherWheelGame(Guid gameId) =>
        TryGetSession(gameId, out var s) ? CipherWheelToDto(s.CipherWheel!) : null;

    public WheelMoveResult ApplySetWheel(
        Guid gameId, string sentenceId, string wheelId, int value, string playerId)
    {
        var colour = ColourFor(playerId);
        if (!TryGetSession(gameId, out var session) || session.CipherWheel is null)
            return new WheelMoveResult(colour, 0.0, false, false);

        var sentence = session.CipherWheel.Sentences.FirstOrDefault(s => s.Id == sentenceId);
        if (sentence is null) return new WheelMoveResult(colour, 0.0, false, false);

        var wheel = sentence.Wheels.FirstOrDefault(w => w.Id == wheelId);
        if (wheel is null) return new WheelMoveResult(colour, 0.0, false, false);

        wheel.Value = ((value % 26) + 26) % 26;
        wheel.LastTouchedBy = playerId;
        session.LastActivity = DateTime.UtcNow;

        var (closeness, sentenceSolved) = EvaluateSentence(sentence);
        if (sentenceSolved && !sentence.Solved) sentence.Solved = true;

        var boardSolved = session.CipherWheel.Sentences.All(s => s.Solved);
        if (boardSolved) session.CipherWheel.Solved = true;

        return new WheelMoveResult(colour, closeness, sentenceSolved, boardSolved);
    }

    private static (double closeness, bool solved) EvaluateSentence(CipherSentence s)
    {
        var N = s.Wheels.Count;
        var currentTotal = s.Wheels.Sum(w => w.Value);
        var targetTotal  = s.TargetWheelValues.Sum();
        var totalCloseness = 0.0;
        var allSolved = true;
        for (var g = 0; g < N; g++)
        {
            var current = ((currentTotal - s.Wheels[g].Value) % 26 + 26) % 26;
            var target  = ((targetTotal  - s.TargetWheelValues[g]) % 26 + 26) % 26;
            if (current != target) allSolved = false;
            var diff = Math.Abs(current - target);
            totalCloseness += 1.0 - Math.Min(diff, 26 - diff) / 13.0;
        }
        return (totalCloseness / N, allSolved);
    }

    private static char RotateLetter(char c, int offset)
    {
        if (c is < 'A' or > 'Z') return c;
        return (char)('A' + (c - 'A' + offset) % 26);
    }

    private CipherWheelGameDto CipherWheelToDto(CipherWheelGameState state) =>
        new(state.Sentences.Select(s =>
        {
            var (closeness, _) = EvaluateSentence(s);
            return new CipherSentenceDto(
                s.Id, s.EncodedText,
                [.. s.Wheels.Select(w => w.Id)],
                [.. s.Wheels.Select(w => w.Value)],
                [.. s.Wheels.Select(w => w.LastTouchedBy)],
                s.Solved, closeness);
        }).ToList(), state.Solved);

    private static CipherWheelGameState BuildCipherWheelGame()
    {
        string[] plainTexts = [
            "THE MAINFRAME IS EXPOSED",
            "DISABLE FIREWALL PROTOCOL",
            "EXTRACT THE ROOT ACCESS KEY",
        ];
        var state = new CipherWheelGameState();
        for (var i = 0; i < plainTexts.Length; i++)
        {
            var plaintext  = plainTexts[i];
            var wheelCount = i + 2; // signal 0 → 2 wheels, 1 → 3, 2 → 4

            int[] targets;
            do { targets = [.. Enumerable.Range(0, wheelCount).Select(_ => Random.Shared.Next(0, 26))]; }
            while (targets.Sum() % 26 == 0);

            var targetTotal = targets.Sum();
            var encoded = new char[plaintext.Length];
            for (var p = 0; p < plaintext.Length; p++)
            {
                var c = plaintext[p];
                if (c is < 'A' or > 'Z') { encoded[p] = c; continue; }
                var g  = p % wheelCount;
                var effectiveTarget = ((targetTotal - targets[g]) % 26 + 26) % 26;
                encoded[p] = RotateLetter(c, 26 - effectiveTarget);
            }

            List<SentenceWheel> wheels;
            bool startSolved;
            do
            {
                wheels = [.. Enumerable.Range(0, wheelCount).Select(j =>
                    new SentenceWheel { Id = $"s{i}w{j}", Value = Random.Shared.Next(0, 26) })];
                var ct = wheels.Sum(w => w.Value);
                startSolved = Enumerable.Range(0, wheelCount).All(g =>
                    ((ct - wheels[g].Value) % 26 + 26) % 26 ==
                    ((targetTotal - targets[g]) % 26 + 26) % 26);
            } while (startSolved);

            state.Sentences.Add(new CipherSentence
            {
                Id = $"s{i}", EncodedText = new string(encoded),
                Plaintext = plaintext, TargetWheelValues = targets, Wheels = wheels,
            });
        }
        return state;
    }

    // ---- Packet Sequencer ------------------------------------------------

    public PacketSequencerDto? GetPacketSequencer(Guid gameId) =>
        TryGetSession(gameId, out var s) && s.PacketSequencer is not null
            ? PacketSequencerToDto(s.PacketSequencer)
            : null;

    /// <summary>
    /// Moves the item at <paramref name="fromIndex"/> so it ends up at
    /// <paramref name="toIndex"/> in the final array (i.e. after removal).
    /// </summary>
    public MoveItemResult ApplyMoveItem(
        Guid gameId, string columnId, int fromIndex, int toIndex, string playerId)
    {
        var colour = ColourFor(playerId);
        if (!TryGetSession(gameId, out var session) || session.PacketSequencer is null)
            return new MoveItemResult(colour, [], false, false);

        var col = session.PacketSequencer.Columns.FirstOrDefault(c => c.Id == columnId);
        if (col is null
            || fromIndex < 0 || fromIndex >= col.Items.Count
            || toIndex   < 0 || toIndex   >= col.Items.Count)
            return new MoveItemResult(colour, [.. col?.Items ?? []], false, false);

        var item = col.Items[fromIndex];
        col.Items.RemoveAt(fromIndex);
        col.Items.Insert(Math.Clamp(toIndex, 0, col.Items.Count), item);
        col.LastTouchedBy = playerId;
        session.LastActivity = DateTime.UtcNow;

        var columnSolved = col.Items.SequenceEqual(col.Solution);
        if (columnSolved && !col.Solved) col.Solved = true;

        var boardSolved = session.PacketSequencer.Columns.All(c => c.Solved);
        if (boardSolved) session.PacketSequencer.Solved = true;

        return new MoveItemResult(colour, [.. col.Items], columnSolved, boardSolved);
    }

    private static PacketSequencerDto PacketSequencerToDto(PacketSequencerState state) =>
        new(state.Columns.Select(c =>
            new PacketColumnDto(c.Id, c.Label, [.. c.Items], c.Solved, c.LastTouchedBy)
        ).ToList(), state.Solved);

    private static PacketSequencerState BuildPacketSequencer()
    {
        // 4 columns of hacking-pipeline steps; each must be restored to logical order.
        var columns = new (string id, string label, string[] lines)[]
        {
            ("c0", "EXPLOIT", [
                "SCAN    open_ports",
                "PROBE   vuln_list",
                "CRAFT   shellcode",
                "INJECT  payload",
                "ESCALATE root",
            ]),
            ("c1", "DECRYPT", [
                "FETCH   cipher.blob",
                "LOAD    private.key",
                "STRIP   padding",
                "APPLY   decrypt()",
                "DUMP    secrets",
            ]),
            ("c2", "PIVOT", [
                "MAP     subnet/24",
                "TUNNEL  via_proxy",
                "SPOOF   source_ip",
                "MOUNT   smb_share",
                "PULL    creds.db",
            ]),
            ("c3", "CLEANUP", [
                "WIPE    access.log",
                "PATCH   timestamps",
                "CLEAR   bash_hist",
                "DELETE  temp_files",
                "CLOSE   sessions",
            ]),
        };

        var state = new PacketSequencerState();
        foreach (var (id, label, lines) in columns)
        {
            var solution = lines.ToList();
            List<string> shuffled;
            do { shuffled = [.. lines.OrderBy(_ => Random.Shared.Next())]; }
            while (shuffled.SequenceEqual(solution));

            state.Columns.Add(new PacketColumn
            {
                Id = id, Label = label, Items = shuffled, Solution = solution,
            });
        }
        return state;
    }

    // ---- Memory Leak (nonogram) ------------------------------------------

    public MemoryLeakDto? GetMemoryLeak(Guid gameId) =>
        TryGetSession(gameId, out var s) && s.MemoryLeak is not null
            ? MemoryLeakToDto(s.MemoryLeak) : null;

    /// <summary>
    /// Flips the on/off state of a single cell. Win is checked by exact match
    /// against the solution grid (identical to the nonogram's intended picture).
    /// </summary>
    public ToggleCellResult ApplyToggleCell(Guid gameId, int row, int col, string playerId)
    {
        var colour = ColourFor(playerId);
        if (!TryGetSession(gameId, out var session) || session.MemoryLeak is null)
            return new ToggleCellResult(colour, row, col, false, false);

        var s = session.MemoryLeak;
        if (row < 0 || row >= s.Rows || col < 0 || col >= s.Cols)
            return new ToggleCellResult(colour, row, col, false, false);

        s.On[row][col]        = !s.On[row][col];
        s.ToggledBy[row][col] = playerId;
        session.LastActivity  = DateTime.UtcNow;

        var boardSolved = CheckSolved(s);
        if (boardSolved) s.Solved = true;

        return new ToggleCellResult(colour, row, col, s.On[row][col], boardSolved);
    }

    private static bool CheckSolved(MemoryLeakState s)
    {
        for (var r = 0; r < s.Rows; r++)
            for (var c = 0; c < s.Cols; c++)
                if (s.On[r][c] != s.Solution[r][c]) return false;
        return true;
    }

    private static MemoryLeakDto MemoryLeakToDto(MemoryLeakState s) =>
        new(s.Rows, s.Cols, s.Values, s.RowClues, s.ColClues, s.On, s.ToggledBy, s.Solved);

    /// <summary>Computes nonogram run-lengths from a single row or column.</summary>
    private static int[] ComputeRuns(bool[] line)
    {
        var runs = new List<int>();
        var count = 0;
        foreach (var cell in line)
        {
            if (cell) count++;
            else if (count > 0) { runs.Add(count); count = 0; }
        }
        if (count > 0) runs.Add(count);
        return [.. runs];
    }

    private static MemoryLeakState BuildMemoryLeak()
    {
        // Three hand-authored 8×8 pixel-art images (0=off, 1=on in the solution).
        // Each is a solvable nonogram: players highlight cells to match the run-length
        // clues per row and column.
        var images = new int[][][]
        {
            // Image 0 — Skull
            [ [0,1,1,1,1,1,1,0],
              [1,0,0,0,0,0,0,1],
              [1,0,1,0,0,1,0,1],
              [1,0,0,0,0,0,0,1],
              [1,0,1,1,1,1,0,1],
              [1,0,0,1,1,0,0,1],
              [0,1,0,0,0,0,1,0],
              [0,0,1,1,1,1,0,0] ],

            // Image 1 — Bug
            [ [0,0,0,1,1,0,0,0],
              [0,0,1,1,1,1,0,0],
              [0,1,1,1,1,1,1,0],
              [1,1,0,1,1,0,1,1],
              [1,1,0,1,1,0,1,1],
              [0,1,1,1,1,1,1,0],
              [0,0,1,0,0,1,0,0],
              [0,0,1,0,0,1,0,0] ],

            // Image 2 — Key
            [ [1,1,1,1,0,0,0,1],
              [1,0,0,0,1,0,0,1],
              [1,0,0,0,0,1,0,1],
              [1,1,1,1,0,0,1,1],
              [1,0,0,0,1,0,0,1],
              [1,0,0,0,0,1,0,1],
              [1,1,1,1,0,0,1,1],
              [1,0,0,0,0,0,0,1] ],
        };

        var img = images[Random.Shared.Next(images.Length)];
        const int rows = 8, cols = 8;

        var solution = img.Select(r => r.Select(v => v == 1).ToArray()).ToArray();

        // Compute nonogram clues from the solution.
        var rowClues = solution.Select(row => ComputeRuns(row)).ToArray();
        var colClues = Enumerable.Range(0, cols)
            .Select(c => ComputeRuns(solution.Select(r => r[c]).ToArray()))
            .ToArray();

        // Assign random bytes (1–255) to every cell (hacker-aesthetic flavour only).
        var values = Enumerable.Range(0, rows)
            .Select(_ => Enumerable.Range(0, cols)
                .Select(_ => Random.Shared.Next(0x01, 0x100))
                .ToArray())
            .ToArray();

        return new MemoryLeakState
        {
            Rows      = rows,
            Cols      = cols,
            Values    = values,
            RowClues  = rowClues,
            ColClues  = colClues,
            Solution  = solution,
            On        = Enumerable.Range(0, rows).Select(_ => new bool[cols]).ToArray(),
            ToggledBy = Enumerable.Range(0, rows).Select(_ => new string?[cols]).ToArray(),
        };
    }

    // ---- Signal Reconstruction grab-lock stub (game 7) -------------------

    public IReadOnlyList<(string gameId, string sliceId)> ReleaseHeldSlices(string connectionId) => [];
}

// ---- Result records -------------------------------------------------------

public record WheelMoveResult(string Colour, double Closeness, bool SentenceSolved, bool BoardSolved);

public record MoveItemResult(string Colour, string[] Items, bool ColumnSolved, bool BoardSolved);


// ---- Session --------------------------------------------------------------

public class GameSession(Guid id, string gameType)
{
    public Guid Id { get; } = id;
    public string GameType { get; } = gameType;
    public CipherWheelGameState?    CipherWheel     { get; set; }
    public PacketSequencerState?    PacketSequencer { get; set; }
    public MemoryLeakState?         MemoryLeak      { get; set; }
    public DateTime LastActivity { get; set; }
}
