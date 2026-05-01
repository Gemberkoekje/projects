using FluentAssertions;
using SpaceTraders.Application.Events.Handlers.Ships;

namespace SpaceTraders.Application.Tests.Events.Handlers;

/// <summary>
/// Phase 7f regression guard: chain-of-command infrastructure has been deleted.
/// These tests assert that no remnants remain in the assembly.
/// </summary>
public sealed class ChainOfCommandEventHandlerRegistrationTests
{
    /// <summary>
    /// Phase 7f: IChainOfCommandEventHandler has been deleted. Ensure no class in the application
    /// assembly accidentally re-introduces it.
    /// </summary>
    [Fact]
    public void Phase7f_NoIChainOfCommandEventHandlerImplementations_ShouldExist()
    {
        var applicationAssembly = typeof(ShipInTransitEventHandler).Assembly;

        var chainHandlerTypes = applicationAssembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition().Name.StartsWith("IChainOfCommandEventHandler", StringComparison.Ordinal)))
            .ToList();

        chainHandlerTypes.Should().BeEmpty(
            "Phase 7f deleted the IChainOfCommandEventHandler<T> interface and all chain-of-command " +
            "infrastructure; no implementations should remain. Found: {0}",
            string.Join(", ", chainHandlerTypes.Select(t => t.Name)));
    }
}
