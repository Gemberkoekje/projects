using Curator.Core.Content;
using Curator.Core.Game;

namespace Curator.Core.Rules;

/// <summary>The library's hidden reputation (BUILD_BRIEF §5.6).</summary>
public static class ReputationRules
{
    /// <summary>The band a reputation falls in.</summary>
    /// <param name="balance">The balance.</param>
    /// <param name="reputation">The reputation.</param>
    /// <returns>Wary, neutral or warm.</returns>
    public static ReputationBand Band(Balance balance, int reputation)
    {
        ArgumentNullException.ThrowIfNull(balance);
        if (reputation <= balance.Reputation.WaryThreshold)
        {
            return ReputationBand.Wary;
        }

        return reputation >= balance.Reputation.WarmThreshold ? ReputationBand.Warm : ReputationBand.Neutral;
    }

    /// <summary>
    /// The reputation change when an outcome surfaces: its own value if it sets one, else on a
    /// public channel (newspaper, gossip) the default for its category, else nothing.
    /// </summary>
    /// <param name="balance">The balance.</param>
    /// <param name="resolution">The outcome.</param>
    /// <param name="channel">The channel it surfaced on.</param>
    /// <returns>The change.</returns>
    public static int OnSurface(Balance balance, Resolution resolution, OutcomeChannel channel)
    {
        ArgumentNullException.ThrowIfNull(balance);
        ArgumentNullException.ThrowIfNull(resolution);
        if (resolution.HasReputation)
        {
            return resolution.Reputation;
        }

        if (channel is not (OutcomeChannel.Newspaper or OutcomeChannel.Gossip))
        {
            return 0;
        }

        return resolution.Category switch
        {
            OutcomeCategory.Good => balance.Reputation.GoodPublic,
            OutcomeCategory.Harm or OutcomeCategory.Mixed => balance.Reputation.HarmPublic,
            _ => 0,
        };
    }
}
