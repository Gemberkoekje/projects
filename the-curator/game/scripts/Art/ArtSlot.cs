using Godot;

namespace Curator.Presentation.Art;

/// <summary>One art slot from art/layout.json: where a layer of the painted scene sits.</summary>
/// <param name="Id">The slot id, also the painting's file name.</param>
/// <param name="Rect">Where it sits on the 1920×1800 canvas.</param>
/// <param name="Z">Draw order.</param>
/// <param name="LightMask">Which lights touch it.</param>
/// <param name="Placeholder">The flat placeholder style.</param>
/// <param name="Color">The placeholder's main colour.</param>
/// <param name="Arch">For the doorway wall: where the arch is, in canvas coordinates.</param>
public sealed record ArtSlot(string Id, Rect2 Rect, int Z, int LightMask, string Placeholder, Color Color, Rect2 Arch);
