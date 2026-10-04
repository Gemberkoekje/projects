using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Adapters;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Availability;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Configuration;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Metrics;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Notifications;
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
        services.AddSingleton<IRateLimitStatus>(sp => sp.GetRequiredService<RateLimitStatus>());
        services.AddSingleton<ApiAvailabilityState>();
        services.AddSingleton<IApiAvailabilityState>(sp => sp.GetRequiredService<ApiAvailabilityState>());
        // B59: the server still counts what the process before this one sent in the last minute.
        services.AddSingleton(_ => RequestBudget.ForANewProcess(TimeProvider.System.GetUtcNow()));
        services.AddTransient<ApiRequestInitiatedHandler>();
        services.AddTransient<RateLimitingHandler>();
        services.AddTransient<RateLimitResponseHandler>();
        services.AddTransient<OutagePauseHandler>();
        services.AddTransient<ApiRequestMetricsHandler>();

        services.AddHttpClient<ISpaceTradersApiClient, SpaceTradersApiClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<SpaceTradersApiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        })
        .AddHttpMessageHandler<ApiRequestInitiatedHandler>()
        .AddHttpMessageHandler<OutagePauseHandler>()
        .AddHttpMessageHandler<RateLimitResponseHandler>()
        .AddHttpMessageHandler<RateLimitingHandler>()
        .AddHttpMessageHandler<ApiRequestMetricsHandler>();

        services.AddScoped<ISpaceTradersPort, SpaceTradersPortAdapter>();

        services.AddHttpClient("webhook");
        services.AddScoped<IAlertNotifier, WebhookAlertNotifier>();

        return services;
    }
}
