using FluentAssertions;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipEventHandlerConventionTests
{
    [Fact]
    public void ShipHandlers_ShouldHandleDockedOrInOrbitEvents_ByDefault()
    {
        var allowedEventTypes = new HashSet<Type>
        {
            typeof(ShipDockedEvent),
            typeof(ShipInOrbitEvent),
        };

        var allowedExceptionHandlers = new HashSet<Type>
        {
            typeof(ShipInTransitEventHandler),
            // Phase 7a: ShipStateMismatchEventHandler deleted.
        };

        var handlerTypes = typeof(ShipInTransitEventHandler).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.Namespace == "SpaceTraders.Application.Events.Handlers.Ships")
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IChainOfCommandEventHandler<>)))
            .ToList();

        var violations = handlerTypes
            .Where(handler => !allowedExceptionHandlers.Contains(handler))
            .Select(handler => new
            {
                Handler = handler,
                EventTypes = handler
                    .GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IChainOfCommandEventHandler<>))
                    .Select(i => i.GetGenericArguments()[0])
                    .Distinct()
                    .ToList(),
            })
            .Where(x => x.EventTypes.Any(eventType => !allowedEventTypes.Contains(eventType)))
            .Select(x => $"{x.Handler.Name} -> {string.Join(", ", x.EventTypes.Select(e => e.Name))}")
            .ToList();

        violations.Should().BeEmpty(
            "ship chain handlers should default to ShipDockedEvent or ShipInOrbitEvent; add temporary exceptions only when needed. Violations: {0}",
            string.Join("; ", violations));
    }
}
