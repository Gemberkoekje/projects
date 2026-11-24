namespace Klaverjas.application.ReadModels;

/// <summary>Dad's Stand screen: the standings now, the settings to draw them with, and what is online.</summary>
public sealed record StandOverzicht
{
    /// <summary>The standings now, as the players would see them.</summary>
    public StandWeergave Weergave { get; init; } = new StandWeergave();

    /// <summary>The settings.</summary>
    public Instellingen Instellingen { get; init; } = new Instellingen();

    /// <summary>What the players' page shows.</summary>
    public OnlineStand Online { get; init; } = new OnlineStand();

    /// <summary>Whether the standings changed after they were put online ("Verbeterde stand online zetten").</summary>
    public bool IsGewijzigd { get; init; }

    /// <summary>Whether exactly these standings are online.</summary>
    public bool StaatOnline => !Online.IsLeeg && !IsGewijzigd;
}
