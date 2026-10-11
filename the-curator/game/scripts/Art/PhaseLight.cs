using Godot;

namespace Curator.Presentation.Art;

/// <summary>The light for one time of day.</summary>
/// <param name="Ambient">The ambient tint.</param>
/// <param name="Window">The window light's colour.</param>
/// <param name="WindowEnergy">The window light's strength.</param>
/// <param name="CandleEnergy">The candle's strength.</param>
/// <param name="ArchwayEnergy">The archway light's strength.</param>
public sealed record PhaseLight(Color Ambient, Color Window, float WindowEnergy, float CandleEnergy, float ArchwayEnergy);
