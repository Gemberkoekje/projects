namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Configuration;

public sealed class SpaceTradersApiOptions
{
    public const string DefaultBaseUrl = "https://api.spacetraders.io/v2/";

    public string BaseUrl { get; set; } = DefaultBaseUrl;

    public string? AgentToken { get; set; }

    public string? AccountToken { get; set; }
}
