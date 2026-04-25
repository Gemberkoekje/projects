namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

public interface IAgentTokenProvider
{
    string? Token { get; }

    void Set(string token);
}
