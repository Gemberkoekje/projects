using Curator.Core.Game;

namespace Curator.Core.Events;

/// <summary>The morning paper arrived.</summary>
/// <param name="Masthead">The paper's name.</param>
/// <param name="HeadlineResolutionIds">Outcomes in today's paper.</param>
/// <param name="Flavour">Ordinary news.</param>
/// <param name="ToneLine">A line about the library, toned by reputation.</param>
/// <param name="Band">The reputation band that chose the tone line.</param>
public sealed record NewspaperDelivered(string Masthead, IReadOnlyList<string> HeadlineResolutionIds, IReadOnlyList<string> Flavour, string ToneLine, ReputationBand Band) : GameEvent;
