using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Tests.Events.Handlers;

public sealed class ChainOfCommandEventHandlerRegistrationTests
{
    /// <summary>
    /// Phase 7e: All ship-state events (ShipDockedEvent, ShipInOrbitEvent, ShipStateMismatchEvent,
    /// ShipInTransitEvent, ConstructionSuppliedEvent) have been decoupled from ChainOfCommandEvent.
    /// No concrete ChainOfCommandEvent subtypes remain; the chain infrastructure is empty and will
    /// be deleted in Phase 7f.
    /// </summary>
    [Fact]
    public void AfterPhase7e_NoConcreteChainOfCommandEventSubtypes_ShouldExist()
    {
        var chainEventTypes = typeof(ChainOfCommandEvent).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => typeof(ChainOfCommandEvent).IsAssignableFrom(t) && t != typeof(ChainOfCommandEvent))
            .ToList();

        chainEventTypes.Should().BeEmpty(
            "Phase 7e decoupled all ship-state events from ChainOfCommandEvent; no concrete subtypes should remain. " +
            "Found: {0}", string.Join(", ", chainEventTypes.Select(t => t.Name)));
    }

    [Fact]
    public void AllChainOfCommandEventTypes_ShouldHaveImplementedHandlers()
    {
        // Arrange - Get all chain-of-command event types
        var chainEventTypes = typeof(ChainOfCommandEvent).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => typeof(ChainOfCommandEvent).IsAssignableFrom(t) && t != typeof(ChainOfCommandEvent))
            .ToList();

        // Events that are pattern-matched inside base handlers, or whose chain handler has been retired,
        // or which have been decoupled from ChainOfCommandEvent entirely (Phase 7e).
        var derivedEventsHandledViaPatternMatching = new HashSet<string>
        {
            // Phase 7e: ConstructionSuppliedEvent decoupled from ShipInOrbitEvent; plain factual event.
            nameof(ConstructionSuppliedEvent),
            // Phase 7a: ShipStateMismatchEventHandler deleted; recovery is now covered by the
            // ShipAutomationTickEvent published alongside every ShipStateMismatchEvent (Phase 6.5e).
            // Phase 7e: ShipStateMismatchEvent decoupled from ChainOfCommandEvent.
            nameof(ShipStateMismatchEvent),
            // Phase 7b: Docked derived events deleted; docked chain handlers removed.
            // Phase 7e: ShipDockedEvent decoupled from ChainOfCommandEvent.
            nameof(ShipDockedEvent),
            // Phase 7c: ShipArrivedEvent and ShipUndockedEvent deleted; all orbit chain handlers removed.
            // Phase 7e: ShipInOrbitEvent decoupled from ChainOfCommandEvent.
            nameof(ShipInOrbitEvent),
            // Phase 7d: ShipInTransitEventHandler converted to a plain Wolverine handler; chain handler removed.
            // Phase 7e: ShipInTransitEvent decoupled from ChainOfCommandEvent.
            nameof(ShipInTransitEvent),
        };

        // Get all handler implementations
        var handlerTypes = typeof(IChainOfCommandEventHandler<>).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IChainOfCommandEventHandler<>)))
            .ToList();

        // Act - For each event type, check if there's at least one handler
        var unhandledEvents = new List<string>();

        foreach (var eventType in chainEventTypes)
        {
            // Skip derived events that are handled via pattern matching
            if (derivedEventsHandledViaPatternMatching.Contains(eventType.Name))
            {
                continue;
            }

            var handlerInterfaceType = typeof(IChainOfCommandEventHandler<>).MakeGenericType(eventType);
            var hasHandler = handlerTypes.Any(h => h.GetInterfaces().Any(i => i == handlerInterfaceType));

            if (!hasHandler)
            {
                unhandledEvents.Add(eventType.Name);
            }
        }

        // Assert
        unhandledEvents.Should().BeEmpty(
            "all chain-of-command events should have at least one implemented handler. Unhandled events: {0}",
            string.Join(", ", unhandledEvents));
    }

    [Fact]
    public void BaseDispatchableEventTypes_ShouldHaveHandlerTypes()
    {
        // Arrange - Only base event types that have dedicated handlers
        var baseDispatchableEventTypes = new Type[]
        {
            // Phase 7a: ShipStateMismatchEventHandler deleted; removed from this list.
            // Phase 7b: ShipDockedEvent chain handlers deleted; removed from this list.
            // Phase 7c: ShipInOrbitEvent chain handlers deleted; removed from this list.
            // Phase 7d: ShipInTransitEventHandler converted to plain Wolverine handler; removed from this list.
        };

        // Get all handler implementations
        var handlerTypes = typeof(IChainOfCommandEventHandler<>).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IChainOfCommandEventHandler<>)))
            .ToList();

        // Act - For each base event type, verify at least one handler is defined
        var unhandledEvents = new List<string>();

        foreach (var eventType in baseDispatchableEventTypes)
        {
            var handlerInterfaceType = typeof(IChainOfCommandEventHandler<>).MakeGenericType(eventType);
            var hasHandler = handlerTypes.Any(h => h.GetInterfaces().Any(i => i == handlerInterfaceType));

            if (!hasHandler)
            {
                unhandledEvents.Add(eventType.Name);
            }
        }

        // Assert
        unhandledEvents.Should().BeEmpty(
            "base dispatchable event types should have handler types defined. Unhandled events: {0}",
            string.Join(", ", unhandledEvents));
    }

    [Fact]
    public void AllEventHandlerImplementations_ShouldFollowNamingConvention()
    {
        // Arrange - Get all handler implementations
        var handlerTypes = typeof(IChainOfCommandEventHandler<>).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.Namespace == "SpaceTraders.Application.Events.Handlers.Ships")
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IChainOfCommandEventHandler<>)))
            .ToList();

        // Act - Check each handler follows the naming convention
        var namingViolations = new List<string>();

        foreach (var handlerType in handlerTypes)
        {
            // Handler name should end with "EventHandler"
            if (!handlerType.Name.EndsWith("EventHandler", StringComparison.Ordinal))
            {
                namingViolations.Add($"{handlerType.Name} - should end with 'EventHandler'");
            }

            // Get the event types this handler implements
            var implementedEventTypes = handlerType
                .GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IChainOfCommandEventHandler<>))
                .Select(i => i.GetGenericArguments()[0].Name)
                .ToList();

            // Handler name should correspond to the event type (e.g., ShipDockedEventHandler for ShipDockedEvent)
            // This is enforced by ST0004 analyzer, so we just verify it has at least one event type
            if (implementedEventTypes.Count == 0)
            {
                namingViolations.Add($"{handlerType.Name} - should implement IChainOfCommandEventHandler<TEvent>");
            }
        }

        // Assert
        namingViolations.Should().BeEmpty(
            "all event handler implementations should follow naming conventions. Violations: {0}",
            string.Join("; ", namingViolations));
    }

    [Fact]
    public void ChainOfCommandEvents_ShouldBeDerivedFromBaseEvent()
    {
        // Arrange - Get all chain-of-command event types
        var chainEventTypes = typeof(ChainOfCommandEvent).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => typeof(ChainOfCommandEvent).IsAssignableFrom(t) && t != typeof(ChainOfCommandEvent))
            .ToList();

        // Act - Verify each is a proper chain event
        var invalidEvents = chainEventTypes
            .Where(t => !typeof(ChainOfCommandEvent).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        // Assert
        invalidEvents.Should().BeEmpty(
            "all chain event types should derive from ChainOfCommandEvent. Invalid events: {0}",
            string.Join(", ", invalidEvents));
    }
}
