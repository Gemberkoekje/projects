using Microsoft.Extensions.Logging;

namespace SpaceTraders.Application.Tests;

/// <summary>Collects what one or more loggers write, to count the lines that reach production.</summary>
internal sealed class LogRecorder
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];
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

    public ILogger<T> For<T>() => new Logger<T>(this);

    private void Add(LogLevel level, string message)
    {
        lock (_lock)
        {
            _entries.Add((level, message));
        }
    }

    private sealed class Logger<T>(LogRecorder recorder) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => recorder.Add(logLevel, formatter(state, exception));
    }
}
