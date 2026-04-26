using Microsoft.Extensions.DependencyInjection;
using SpaceTraders.Application.Events.Dispatching;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;
using Wolverine.ErrorHandling;

namespace SpaceTraders.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<ITradeAnalyser, TradeAnalyser>();
        services.AddSingleton<ICreditHistoryService, CreditHistoryService>();
        services.AddScoped<IShipAssignmentPlanner, ShipAssignmentPlanner>();
        services.AddScoped<IChainOfCommandDispatcher, ChainOfCommandDispatcher>();

        services.AddScoped<IDockedCommandAcceptor, DockedCommandAcceptor>();
        services.AddScoped<IInOrbitCommandAcceptor, InOrbitCommandAcceptor>();
        services.AddScoped<IInTransitCommandAcceptor, InTransitCommandAcceptor>();

        services.AddScoped<IWaypointVisitService, WaypointVisitService>();
        services.AddScoped<IMarketRefreshService, MarketRefreshService>();
        services.AddScoped<IShipyardRefreshService, ShipyardRefreshService>();

        services.AddScoped<IChainOfCommandEventHandler<ShipUndockedEvent>, ShipUndockedScoutEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipUndockedEvent>, ShipUndockedMineEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipUndockedEvent>, ShipUndockedEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipMovingEvent>, ShipMovingEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipArrivedEvent>, ShipArrivedScoutEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipArrivedEvent>, ShipArrivedMineEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipArrivedEvent>, ShipArrivedEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipRoleSetEvent>, ShipRoleSetEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipDockedEvent>, ShipDockedEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipRefueledEvent>, ShipRefueledEventHandler>();

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
