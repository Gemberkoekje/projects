namespace TheCuratool.Application;

/// <summary>
/// Pre-draft options that affect the setup calculation and draft session initialisation.
/// </summary>
/// <param name="UseMarionette">When <see langword="true"/>, the Marionette adjustment is applied (+1 Townsfolk / −1 Minion).</param>
public sealed record SessionSetupOptions(bool UseMarionette);
