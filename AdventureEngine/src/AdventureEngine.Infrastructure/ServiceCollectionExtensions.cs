namespace AdventureEngine.Infrastructure;

using AdventureEngine.Infrastructure.Agents;
using AdventureEngine.Infrastructure.Anthropic;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAdventureEngineInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.SectionName));

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=adventure;Username=postgres;******";

        services.AddMarten(MartenConfiguration.Configure(connectionString))
            .UseLightweightSessions();

        services.AddScoped<IDirectorAgent, DirectorAgent>();
        services.AddScoped<INarratorAgent, NarratorAgent>();
        services.AddScoped<INpcAgent, NpcAgent>();
        services.AddScoped<ModerationAgent>();
        services.AddScoped<IGameSessionService, GameSessionService>();

        return services;
    }
}
