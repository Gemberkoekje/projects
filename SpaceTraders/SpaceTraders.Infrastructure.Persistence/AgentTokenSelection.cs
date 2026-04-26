using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence;

public static class AgentTokenSelection
{
    public const string ActiveAgentTokenKey = "ActiveAgentToken";
    public const string AgentTokenCredentialKey = "AgentToken";

    public static async Task<string> GetActiveTokenAsync(
        SpaceTradersDbContext db,
        CancellationToken cancellationToken = default)
    {
        var token = await db.Credentials
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Key == ActiveAgentTokenKey)
            .OrderByDescending(c => c.StoredAt)
            .Select(c => c.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return token ?? string.Empty;
    }

    public static async Task<string> GetLatestAgentTokenAsync(
        SpaceTradersDbContext db,
        CancellationToken cancellationToken = default)
    {
        var token = await db.Credentials
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Key == AgentTokenCredentialKey)
            .OrderByDescending(c => c.StoredAt)
            .Select(c => c.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return token ?? string.Empty;
    }

    public static async Task SetActiveTokenAsync(
        SpaceTradersDbContext db,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var existingRows = await db.Credentials
            .IgnoreQueryFilters()
            .Where(c => c.Key == ActiveAgentTokenKey)
            .ToListAsync(cancellationToken);

        if (existingRows.Count > 0)
        {
            db.Credentials.RemoveRange(existingRows);
        }

        db.Credentials.Add(new StoredCredential
        {
            AgentToken = token,
            Key = ActiveAgentTokenKey,
            Value = token,
            StoredAt = TimeProvider.System.GetUtcNow(),
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
