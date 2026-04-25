namespace SpaceTraders.API.Configuration;

public sealed class SpaceTradersBootstrapOptions
{
    public string? AccountToken { get; set; }

    public string? AgentName { get; set; }

    public string AgentFaction { get; set; } = "COSMIC";
}
