using System.Diagnostics.CodeAnalysis;
using Curator.Core.Content;
using Curator.Core.Game;

namespace Curator.Core.Rules;

/// <summary>Trust: an integer 0 (Guarded) to 3 (Confiding), per patron (BUILD_BRIEF §5.5).</summary>
public static class TrustRules
{
    public const int Lowest = 0;
    public const int Highest = 3;

    /// <summary>Clamps a trust value to 0-3.</summary>
    /// <param name="trust">The value.</param>
    /// <returns>The clamped value.</returns>
    public static int Clamp(int trust) => Math.Clamp(trust, Lowest, Highest);

    /// <summary>A patron's trust before their first visit.</summary>
    /// <param name="content">The content.</param>
    /// <param name="state">The state (reputation warms strangers).</param>
    /// <param name="patron">The patron.</param>
    /// <returns>Their starting trust.</returns>
    public static int Initial(ContentSet content, GameState state, Patron patron)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(patron);
        var start = patron.StartTrust ?? content.Balance.Trust.StrangerStart;
        var reputation = content.Balance.Reputation;
        if (patron.Role == PatronRole.Filler && state.Reputation >= reputation.WarmThreshold)
        {
            start = Math.Max(start, reputation.WarmStrangerTrust);
        }

        return Clamp(start);
    }

    /// <summary>A patron's trust now: what the state says once met, else their starting trust.</summary>
    /// <param name="content">The content.</param>
    /// <param name="state">The state.</param>
    /// <param name="patronId">The patron.</param>
    /// <returns>Their trust.</returns>
    public static int Current(ContentSet content, GameState state, string patronId)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        return state.HasMet(patronId) ? state.Patron(patronId).Trust : Initial(content, state, content.Patron(patronId));
    }

    /// <summary>
    /// The entry for a trust level from a map keyed by trust: the entry at that level, else the
    /// nearest lower one (BUILD_BRIEF §5.8, §5.9).
    /// </summary>
    /// <param name="byTrust">The map.</param>
    /// <param name="trust">The current trust.</param>
    /// <param name="value">The entry found.</param>
    /// <typeparam name="T">The entry type.</typeparam>
    /// <returns>True when an entry at or below the level exists.</returns>
    public static bool TryAtOrBelow<T>(IReadOnlyDictionary<int, T> byTrust, int trust, [MaybeNullWhen(false)] out T value)
    {
        ArgumentNullException.ThrowIfNull(byTrust);
        for (var level = trust; level >= Lowest; level--)
        {
            if (byTrust.TryGetValue(level, out var found))
            {
                value = found;
                return true;
            }
        }

        value = default;
        return false;
    }
}
