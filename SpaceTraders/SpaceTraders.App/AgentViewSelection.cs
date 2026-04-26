using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence;

namespace SpaceTraders.App;

public sealed record AgentViewOption(string Token, string AgentSymbol, DateTimeOffset LastSeenAt, bool IsActive);

public static class AgentViewSelection
{
    private const string ViewAgentTokenCookie = "st.view.agent-token";

    public static async Task<string> ResolveSelectedTokenAsync(
        SpaceTradersDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var queryToken = httpContext.Request.Query["agentToken"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(queryToken))
        {
            SetCookie(httpContext, queryToken);
            return queryToken;
        }

        if (httpContext.Request.Cookies.TryGetValue(ViewAgentTokenCookie, out var cookieToken) &&
            !string.IsNullOrWhiteSpace(cookieToken))
        {
            return cookieToken;
        }

        var activeToken = await AgentTokenSelection.GetActiveTokenAsync(db, cancellationToken);
        if (!string.IsNullOrWhiteSpace(activeToken))
        {
            SetCookie(httpContext, activeToken);
            return activeToken;
        }

        var latestToken = await AgentTokenSelection.GetLatestAgentTokenAsync(db, cancellationToken);
        if (!string.IsNullOrWhiteSpace(latestToken))
        {
            SetCookie(httpContext, latestToken);
            return latestToken;
        }

        return db.AgentToken;
    }

    public static void SetCookie(HttpContext httpContext, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        httpContext.Response.Cookies.Append(
            ViewAgentTokenCookie,
            token,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = httpContext.Request.IsHttps,
                Expires = DateTimeOffset.UtcNow.AddDays(30),
            });
    }

    public static async Task<IReadOnlyList<AgentViewOption>> GetAgentOptionsAsync(
        SpaceTradersDbContext db,
        string search,
        CancellationToken cancellationToken = default)
    {
        var activeToken = await AgentTokenSelection.GetActiveTokenAsync(db, cancellationToken);

        var storedTokens = await db.Credentials
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Key == AgentTokenSelection.AgentTokenCredentialKey)
            .Select(c => c.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var tokens = storedTokens
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!string.IsNullOrWhiteSpace(activeToken) && !tokens.Contains(activeToken, StringComparer.Ordinal))
        {
            tokens.Add(activeToken);
        }

        var agents = await db.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => tokens.Contains(a.AgentToken))
            .ToListAsync(cancellationToken);

        var logs = await db.ActivityLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(l => tokens.Contains(l.AgentToken))
            .ToListAsync(cancellationToken);

        var logByToken = logs
            .GroupBy(l => l.AgentToken)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Timestamp), StringComparer.Ordinal);

        var options = tokens
            .Select(token =>
            {
                var agent = agents
                    .Where(a => a.AgentToken == token)
                    .OrderByDescending(a => a.LastSyncedAt)
                    .FirstOrDefault();

                var agentLastSeen = agent?.LastSyncedAt ?? DateTimeOffset.MinValue;
                var logLastSeen = logByToken.TryGetValue(token, out var seenAt) ? seenAt : DateTimeOffset.MinValue;
                var lastSeen = agentLastSeen >= logLastSeen ? agentLastSeen : logLastSeen;
                var symbol = agent?.Symbol ?? "UNKNOWN";
                var isActive = !string.IsNullOrWhiteSpace(activeToken) && token == activeToken;

                return new AgentViewOption(token, symbol, lastSeen, isActive);
            })
            .Where(o => string.IsNullOrWhiteSpace(search) ||
                        o.AgentSymbol.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        o.Token.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.IsActive)
            .ThenByDescending(o => o.LastSeenAt)
            .ThenBy(o => o.AgentSymbol)
            .ToList();

        return options;
    }
}
