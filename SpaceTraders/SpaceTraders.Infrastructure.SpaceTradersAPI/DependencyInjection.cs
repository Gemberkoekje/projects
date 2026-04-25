using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Configuration;
using SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI;

public static class DependencyInjection
{
    public static IServiceCollection AddSpaceTradersApiInfrastructure(this IServiceCollection services)
        => services.AddSpaceTradersApi(_ => { });

    public static IServiceCollection AddSpaceTradersApi(
        this IServiceCollection services,
        Action<SpaceTradersApiOptions> configureOptions)
    {
        services.Configure(configureOptions);
        return services.AddSpaceTradersApiClient();
    }

    public static IServiceCollection AddSpaceTradersApi(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "SpaceTradersApi")
    {
        services.Configure<SpaceTradersApiOptions>(configuration.GetSection(sectionName));
        return services.AddSpaceTradersApiClient();
    }

    private static IServiceCollection AddSpaceTradersApiClient(this IServiceCollection services)
    {
        services.AddSingleton<IAgentTokenProvider, AgentTokenProvider>();
        services.AddSingleton<RateLimitStatus>();
        services.AddTransient<RateLimitingHandler>();
        services.AddTransient<RateLimitResponseHandler>();
        services.AddTransient<RetryHandler>();

        services.AddHttpClient<ISpaceTradersApiClient, SpaceTradersApiClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<SpaceTradersApiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        })
        .AddHttpMessageHandler<RetryHandler>()
        .AddHttpMessageHandler<RateLimitResponseHandler>()
        .AddHttpMessageHandler<RateLimitingHandler>();

        return services;
    }
}
