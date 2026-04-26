namespace SpaceTraders.API.Configuration;

[Mutable]
public sealed class SpaceTradersBootstrapOptions
{
    public string? AgentToken { get; set; }

    public string? AccountToken { get; set; }

    public string? AgentName { get; set; }

    public string AgentFaction { get; set; } = "COSMIC";
}
