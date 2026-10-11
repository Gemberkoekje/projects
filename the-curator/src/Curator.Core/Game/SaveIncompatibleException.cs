namespace Curator.Core.Game;

/// <summary>Thrown when a save can't be restored: another schema, or content it no longer matches.</summary>
public sealed class SaveIncompatibleException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SaveIncompatibleException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What's wrong.</param>
    public SaveIncompatibleException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and cause.</summary>
    /// <param name="message">What's wrong.</param>
    /// <param name="innerException">The cause.</param>
    public SaveIncompatibleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
