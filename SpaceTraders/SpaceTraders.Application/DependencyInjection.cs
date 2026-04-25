using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace SpaceTraders.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddWolverine(opts =>
        {
            opts.Discovery.IncludeAssembly(typeof(DependencyInjection).Assembly);
        });

        return services;
    }
}
