using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence;

namespace SpaceTraders.API.Services;

/// <summary>
/// Logs the current settings snapshot for the active agent token.
/// </summary>
public sealed class SettingsSnapshotLogger(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<SettingsSnapshotLogger> logger)
{
    public async Task LogAsync(string reason, CancellationToken cancellationToken = default)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var settingsRepository = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

        var settings = await settingsRepository.GetAllAsync(cancellationToken);
        var token = MaskToken(dbContext.AgentToken);

        logger.LogInformation(
            "Settings snapshot ({Reason}) for agent token {AgentToken}: {Count} setting(s).",
            reason,
            token,
            settings.Count);

        foreach (var setting in settings)
        {
            logger.LogInformation(
                "Setting {Key} = {Value} ({Type}) - {Description}",
                setting.Key,
                setting.Value,
                setting.Type,
                setting.Description);
        }
    }

    private static string MaskToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "(empty)";
        }

        if (token.Length <= 8)
        {
            return "[redacted]";
        }

        return $"{token[..4]}[...]{token[^4..]}";
    }
}
