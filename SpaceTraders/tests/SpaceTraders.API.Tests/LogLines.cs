using Microsoft.Extensions.Logging;

namespace SpaceTraders.API.Tests;

/// <summary>Collects what one logger writes: each line's level, rendered message and exception.</summary>
/// <typeparam name="T">The logger's category.</typeparam>
internal sealed class LogLines<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message, bool HasException)> _lines = [];
    private readonly Lock _lock = new();

    /// <summary>The messages logged at <paramref name="level"/>.</summary>
    public IReadOnlyList<string> At(LogLevel level)
    {
        lock (_lock)
        {
            return _lines.Where(line => line.Level == level).Select(line => line.Message).ToList();
        }
    }

    /// <summary>Whether any line carried an exception.</summary>
    public bool AnyException
    {
        get
        {
            lock (_lock)
            {
                return _lines.Exists(line => line.HasException);
            }
        }
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        lock (_lock)
        {
            _lines.Add((logLevel, formatter(state, exception), exception is not null));
        }
    }
}
