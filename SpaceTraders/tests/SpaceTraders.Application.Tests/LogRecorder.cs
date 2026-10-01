using Microsoft.Extensions.Logging;

namespace SpaceTraders.Application.Tests;

/// <summary>Collects what one or more loggers write, to count the lines that reach production.</summary>
internal sealed class LogRecorder
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];
    private readonly List<JournalEntry> _journal = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>What production keeps: Information and above.</summary>
    public IReadOnlyList<string> Kept => Entries.Where(entry => entry.Level >= LogLevel.Information).Select(entry => entry.Message).ToList();

    /// <summary>The journal lines: those with an <c>EventKind</c> property (see <see cref="JournalEvents"/>).</summary>
    public IReadOnlyList<JournalEntry> Journal
    {
        get
        {
            lock (_lock)
            {
                return [.. _journal];
            }
        }
    }

    public ILogger<T> For<T>() => new Logger<T>(this);

    private void Add(LogLevel level, string message, IReadOnlyDictionary<string, object?> properties)
    {
        lock (_lock)
        {
            _entries.Add((level, message));
            if (properties.TryGetValue("EventKind", out var kind) && kind is string eventKind)
            {
                _journal.Add(new JournalEntry(level, eventKind, properties, message));
            }
        }
    }

    /// <summary>One journal line: its level, kind, structured properties and rendered message.</summary>
    internal sealed record JournalEntry(LogLevel Level, string EventKind, IReadOnlyDictionary<string, object?> Properties, string Message);

    private sealed class Logger<T>(LogRecorder recorder) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.GroupBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);
            recorder.Add(logLevel, formatter(state, exception), properties);
        }
    }
}
