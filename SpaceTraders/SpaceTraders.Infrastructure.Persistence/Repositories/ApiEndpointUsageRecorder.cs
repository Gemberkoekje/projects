using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class ApiEndpointUsageRecorder(SpaceTradersDbContext db) : IApiEndpointUsageRecorder
{
    public async Task RecordAsync(string httpMethod, string endpoint, string agentToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(httpMethod) || string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(agentToken))
        {
            return;
        }

        var now = TimeProvider.System.GetUtcNow();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO api_endpoint_usages ("AgentToken", "HttpMethod", "Endpoint", "Calls", "LastCalledAt")
            VALUES ({agentToken}, {httpMethod}, {endpoint}, 1, {now})
            ON CONFLICT ("AgentToken", "HttpMethod", "Endpoint")
            DO UPDATE
            SET "Calls" = api_endpoint_usages."Calls" + 1,
                "LastCalledAt" = EXCLUDED."LastCalledAt";
            """, cancellationToken);
    }

    public async Task<long> GetTotalCallsAsync(CancellationToken cancellationToken = default)
    {
        return await db.ApiEndpointUsages
            .AsNoTracking()
            .SumAsync(u => (long)u.Calls, cancellationToken);
    }
}
