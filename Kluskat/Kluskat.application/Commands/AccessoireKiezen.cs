namespace Kluskat.application.Commands;

/// <summary>Dresses the cat, from the wardrobe on the profile page.</summary>
public sealed record AccessoireKiezen : ICommand
{
    /// <summary>What to wear; <see cref="Accessoire.None"/> takes it off.</summary>
    public Accessoire Accessoire { get; init; }
}
