using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record SurveyCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SurveyCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class SurveyHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    ISurveyRepository surveys,
    IMessageBus bus,
    ILogger<SurveyHandler> logger)
{
    public async Task Handle(SurveyCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (!string.Equals(ship?.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(SurveyCommand),
                "IN_ORBIT",
                ship?.Status ?? "UNKNOWN",
                "Ship must be in orbit to survey.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping survey for ship {Symbol}: expected IN_ORBIT but was {Status}.",
                command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return;
        }

        var result = await port.SurveyAsync(command.ShipSymbol, cancellationToken);
        if (!string.IsNullOrWhiteSpace(ship.WaypointSymbol) && result.Surveys.Count > 0)
        {
            await surveys.UpsertAsync(command.ShipSymbol, result.Surveys, cancellationToken);
        }

        logger.LogInformation("Ship {Symbol} completed survey. {Count} surveys found, cooldown {Seconds}s.",
            command.ShipSymbol, result.Surveys.Count, result.CooldownSeconds);
    }
}
