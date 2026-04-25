using Microsoft.Extensions.DependencyInjection;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Repositories;
using Wolverine;
using Wolverine.ErrorHandling;

namespace SpaceTraders.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IActivityLogRepository, ActivityLogRepository>();

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
