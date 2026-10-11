namespace Curator.Core.Content;

/// <summary>How an outcome reaches the curator. <see cref="None"/> is silence: it never surfaces.</summary>
public enum OutcomeChannel
{
    None = 0,
    Newspaper,
    Letter,
    Gossip,
    Return,
}
