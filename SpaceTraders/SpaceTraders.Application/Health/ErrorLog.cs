namespace SpaceTraders.Application.Health;

/// <summary>
/// The warnings and errors the bot logged in the last <see cref="Window"/>, per log statement, for the
/// <c>RepeatingError</c> rule. The host's log pipeline records every one (a Serilog sink), except
/// journal lines: those that carry an <c>EventKind</c> report something another rule watches.
/// </summary>
/// <remarks>Thread-safe: whatever thread logs records into it.</remarks>
public sealed class ErrorLog
{
    /// <summary>How long a warning or error is remembered: the rule's ten minutes.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>At most this many are remembered per statement; far more than any limit.</summary>
    private const int MaxPerStatement = 1_000;

    private readonly Dictionary<string, Statement> _statements = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    /// <summary>Records one warning or error.</summary>
    /// <param name="statement">The log statement it came from: its source and message template.</param>
    /// <param name="message">The rendered message.</param>
    /// <param name="at">When it was logged.</param>
    public void Record(string statement, string message, DateTimeOffset at)
    {
        lock (_lock)
        {
            if (!_statements.TryGetValue(statement, out var entry))
            {
                entry = new Statement();
                _statements[statement] = entry;
            }

            entry.Times.Enqueue(at);
            if (entry.Times.Count > MaxPerStatement)
            {
                entry.Times.Dequeue();
            }

            entry.LastMessage = message;
            entry.LastAt = at;
            Forget(at - Window);
        }
    }

    /// <summary>Each statement that logged from <paramref name="since"/> on: how often, when last and what.</summary>
    /// <param name="since">The start of the window.</param>
    /// <returns>One entry per statement.</returns>
    public IReadOnlyList<LoggedStatement> Since(DateTimeOffset since)
    {
        lock (_lock)
        {
            return
            [
                .. _statements
                    .Select(statement => new LoggedStatement(
                        statement.Key,
                        statement.Value.Times.Count(time => time >= since),
                        statement.Value.LastAt,
                        statement.Value.LastMessage))
                    .Where(statement => statement.Count > 0),
            ];
        }
    }

    private void Forget(DateTimeOffset before)
    {
        foreach (var (key, entry) in _statements.ToList())
        {
            while (entry.Times.Count > 0 && entry.Times.Peek() < before)
            {
                entry.Times.Dequeue();
            }

            if (entry.Times.Count == 0)
            {
                _statements.Remove(key);
            }
        }
    }

    [Mutable]
    private sealed class Statement
    {
        public Queue<DateTimeOffset> Times { get; } = new();

        public string LastMessage { get; set; } = string.Empty;

        public DateTimeOffset LastAt { get; set; }
    }
}

/// <summary>A log statement that logged warnings or errors in a window.</summary>
public sealed record LoggedStatement
{
    /// <summary>Creates the summary of one statement.</summary>
    /// <param name="Statement">Its source and message template.</param>
    /// <param name="Count">How often it logged in the window.</param>
    /// <param name="LastAt">When it logged last.</param>
    /// <param name="LastMessage">What it logged last, rendered.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public LoggedStatement(string Statement, int Count, DateTimeOffset LastAt, string LastMessage)
    {
        this.Statement = Statement;
        this.Count = Count;
        this.LastAt = LastAt;
        this.LastMessage = LastMessage;
    }

    /// <summary>Its source and message template.</summary>
    public required string Statement { get; init; }

    /// <summary>How often it logged in the window.</summary>
    public required int Count { get; init; }

    /// <summary>When it logged last.</summary>
    public required DateTimeOffset LastAt { get; init; }

    /// <summary>What it logged last, rendered.</summary>
    public required string LastMessage { get; init; }
}
