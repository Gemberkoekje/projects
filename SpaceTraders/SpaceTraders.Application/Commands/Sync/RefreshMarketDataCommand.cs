using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using System.Text.Json;
using Wolverine;

namespace SpaceTraders.Application.Commands.Sync;

public record RefreshMarketDataCommand(string SystemSymbol, string WaypointSymbol);

public sealed class RefreshMarketDataHandler(
    ISpaceTradersApiClient apiClient,
    SpaceTradersDbContext db,
    IMessageBus bus,
    ILogger<RefreshMarketDataHandler> logger)
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    public async Task Handle(RefreshMarketDataCommand command, CancellationToken cancellationToken)
    {
        // Only refresh if a ship is present at this waypoint
        var shipPresent = await db.Ships
            .AsNoTracking()
            .AnyAsync(s => s.WaypointSymbol == command.WaypointSymbol && s.Status != "IN_TRANSIT", cancellationToken);

        if (!shipPresent)
        {
            logger.LogDebug("Skipping market refresh for {Waypoint}: no ship present.", command.WaypointSymbol);
            return;
        }

        // Check TTL
        var existing = await db.Markets.FindAsync([command.WaypointSymbol], cancellationToken);
        if (existing is not null && existing.LastObservedAt + DefaultTtl > DateTimeOffset.UtcNow)
        {
            logger.LogDebug("Skipping market refresh for {Waypoint}: data is fresh.", command.WaypointSymbol);
            return;
        }

        var market = await apiClient.GetMarketAsync(command.SystemSymbol, command.WaypointSymbol, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var tradeGoodsJson = market.TradeGoods is not null ? JsonSerializer.Serialize(market.TradeGoods) : null;
        var importsJson = market.Imports is not null ? JsonSerializer.Serialize(market.Imports) : null;
        var exportsJson = market.Exports is not null ? JsonSerializer.Serialize(market.Exports) : null;
        var exchangeJson = market.Exchange is not null ? JsonSerializer.Serialize(market.Exchange) : null;

        if (existing is null)
        {
            db.Markets.Add(new CachedMarket
            {
                WaypointSymbol = command.WaypointSymbol,
                SystemSymbol = command.SystemSymbol,
                TradeGoodsJson = tradeGoodsJson,
                ImportsJson = importsJson,
                ExportsJson = exportsJson,
                ExchangeJson = exchangeJson,
                LastObservedAt = now
            });
        }
        else
        {
            existing.TradeGoodsJson = tradeGoodsJson;
            existing.ImportsJson = importsJson;
            existing.ExportsJson = exportsJson;
            existing.ExchangeJson = exchangeJson;
            existing.LastObservedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        await bus.PublishAsync(new MarketDataRefreshedEvent(new WaypointSymbol(command.WaypointSymbol)));
        logger.LogInformation("Market data refreshed for {Waypoint}.", command.WaypointSymbol);
    }
}
