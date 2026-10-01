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

    /// <summary>The <see cref="Scoping.AgentIdentity"/> the token was stored under, or <c>null</c> for a new token.</summary>
    public static async Task<string?> FindAgentIdAsync(
        SpaceTradersDbContext db,
        string token,
        CancellationToken cancellationToken = default)
    {
        return await db.Credentials
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Value == token)
            .OrderByDescending(c => c.StoredAt)
            .Select(c => c.AgentId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Whether credentials are stored for this <see cref="Scoping.AgentIdentity"/>.</summary>
    public static Task<bool> IsKnownAgentAsync(
        SpaceTradersDbContext db,
        string agentId,
        CancellationToken cancellationToken = default)
        => db.Credentials
            .IgnoreQueryFilters()
            .AnyAsync(c => c.AgentId == agentId, cancellationToken);

    /// <summary>Marks the token as the active one, for the agent of <paramref name="db"/>.</summary>
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
            AgentId = db.AgentId,
            Key = ActiveAgentTokenKey,
            Value = token,
            StoredAt = TimeProvider.System.GetUtcNow(),
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
