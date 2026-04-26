namespace SpaceTraders.Infrastructure.Persistence.Scoping;

public interface IAgentDataScope
{
    string Token { get; }

    void Set(string token);
}
