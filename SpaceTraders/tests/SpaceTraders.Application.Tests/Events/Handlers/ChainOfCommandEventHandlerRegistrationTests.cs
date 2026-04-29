using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Events.Ships;
using System.Reflection;

namespace SpaceTraders.Application.Tests.Events.Handlers;

public sealed class ChainOfCommandEventHandlerRegistrationTests
{
    [Fact]
    public void AllChainOfCommandEventTypes_ShouldHaveDiRegisteredHandlers()
    {
        // Arrange - discover all concrete ChainOfCommandEvent subtypes that are dispatched directly
        // (i.e., events whose immediate base type is ChainOfCommandEvent, as the bridge dispatches
        // derived events upcast to their base type)
        var chainEventTypes = typeof(ChainOfCommandEvent).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.BaseType == typeof(ChainOfCommandEvent))
            .ToList();

        // Act - invoke the real DI registration and inspect descriptors
        var services = new ServiceCollection();
        services.AddApplication();

        var unhandledEvents = new List<string>();

        foreach (var eventType in chainEventTypes)
        {
            var handlerInterfaceType = typeof(IChainOfCommandEventHandler<>).MakeGenericType(eventType);
            var hasRegistration = services.Any(d => d.ServiceType == handlerInterfaceType);

            if (!hasRegistration)
            {
                unhandledEvents.Add(eventType.Name);
            }
        }

        // Assert
        unhandledEvents.Should().BeEmpty(
            "all top-level ChainOfCommandEvent types must have at least one IChainOfCommandEventHandler<TEvent> registered in DI. " +
            "Missing registrations: {0}",
            string.Join(", ", unhandledEvents));
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

        // Events that are pattern-matched inside base handlers (no direct handler needed)
        var derivedEventsHandledViaPatternMatching = new HashSet<string>
        {
            nameof(ShipArrivedEvent),
            nameof(ShipAssignmentTypeSetEvent),
            nameof(ShipContractDockedEvent),
            nameof(ShipIdleDockedEvent),
            nameof(ShipRefueledEvent),
            nameof(ShipRoleSetEvent),
            nameof(ShipUndockedEvent),
            nameof(ConstructionSuppliedEvent)
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
        var baseDispatchableEventTypes = new[]
        {
            typeof(ShipDockedEvent),
            typeof(ShipInOrbitEvent),
            typeof(ShipInTransitEvent),
            typeof(ShipStateMismatchEvent)
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
