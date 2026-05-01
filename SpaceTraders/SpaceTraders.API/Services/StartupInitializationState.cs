using System.Threading;

namespace SpaceTraders.API.Services;

/// <summary>
/// Tracks completion and failure state for deferred startup initialization.
/// </summary>
public sealed class StartupInitializationState
{
    private Exception _failure = null!;
    private int _isCompleted;
    private int _isRunning;

    /// <summary>Gets whether deferred startup initialization is currently running.</summary>
    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    /// <summary>Gets whether deferred startup initialization has completed successfully.</summary>
    public bool IsCompleted => Volatile.Read(ref _isCompleted) == 1;

    /// <summary>Gets whether deferred startup initialization has failed.</summary>
    public bool HasFailed => _failure is not null;

    /// <summary>Gets the initialization failure, when available.</summary>
    public Exception Failure => _failure;

    /// <summary>Marks deferred startup initialization as running.</summary>
    public void MarkRunning()
    {
        Interlocked.Exchange(ref _isRunning, 1);
        Interlocked.Exchange(ref _isCompleted, 0);
        _failure = null!;
    }

    /// <summary>Marks deferred startup initialization as completed.</summary>
    public void MarkCompleted()
    {
        Interlocked.Exchange(ref _isRunning, 0);
        Interlocked.Exchange(ref _isCompleted, 1);
        _failure = null!;
    }

    /// <summary>Marks deferred startup initialization as failed.</summary>
    /// <param name="exception">The initialization failure.</param>
    public void MarkFailed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _failure = exception;
        Interlocked.Exchange(ref _isRunning, 0);
        Interlocked.Exchange(ref _isCompleted, 0);
    }
}
