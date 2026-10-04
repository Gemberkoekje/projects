using Serilog.Core;
using Serilog.Events;
using SpaceTraders.Application.Naming;

namespace SpaceTraders.API.Services;

/// <summary>
/// Puts the name the bot gives a ship (slice 2.14, D72) on every log line about it, as <see cref="LogProperty"/>, beside its
/// symbol: a line with a <c>ShipSymbol</c>, from its message or from the scope of the ship's goal step in the tick. The name
/// comes from the name book (<see cref="IShipNameBook"/>), which knows a ship once something has read the fleet since it
/// joined: startup recovery, its purchase, and the metrics every 10 seconds.
/// </summary>
/// <param name="names">The fleet's names, read on every line.</param>
public sealed class ShipNameEnricher(IShipNameBook names) : ILogEventEnricher
{
    /// <summary>The property's name on the log lines.</summary>
    public const string LogProperty = "ShipName";

    /// <summary>The property that says which ship a line is about.</summary>
    private const string ShipSymbol = "ShipSymbol";

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        if (logEvent.Properties.TryGetValue(ShipSymbol, out var value)
            && value is ScalarValue { Value: string symbol }
            && names.NameOf(symbol) is { Length: > 0 } name)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(LogProperty, name));
        }
    }
}
