using Microsoft.Extensions.DependencyInjection;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Services;
using Wolverine;
using Wolverine.ErrorHandling;

namespace SpaceTraders.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<ITradeAnalyser, TradeAnalyser>();
        services.AddSingleton<ICreditHistoryService, CreditHistoryService>();

        services.AddWolverine(opts =>
        {
            opts.Discovery.IncludeAssembly(typeof(DependencyInjection).Assembly);

            opts.OnException<Exception>()
                .RetryWithCooldown(
                    TimeSpan.FromMilliseconds(250),
                    TimeSpan.FromMilliseconds(500),
                    TimeSpan.FromSeconds(1))
                .Then.Discard();
        });

        return services;
    }
}
