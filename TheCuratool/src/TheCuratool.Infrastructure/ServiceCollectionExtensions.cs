namespace TheCuratool.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TheCuratool.Application.Abstractions.Repositories;
using TheCuratool.Infrastructure.Data;
using TheCuratool.Infrastructure.Repositories;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCuratoolInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Add DbContext
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=curatool;Username=postgres;Password=postgres";

        services.AddDbContext<CuratoolDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Add repositories
        services.AddScoped<IScriptRepository, ScriptRepository>();
        services.AddScoped<IGameSessionRepository, GameSessionRepository>();

        return services;
    }
}
