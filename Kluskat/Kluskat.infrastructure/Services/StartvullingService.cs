using Kluskat.application.Commands;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kluskat.infrastructure.Services;

/// <summary>
/// Seeds an empty database from <c>startvulling.json</c>: the chore list agreed in DESIGN.md, which
/// started life as the "Huishoudelijke taken" sheet. Runs at every start and is a no-op once the
/// list holds anything.
/// </summary>
public sealed class StartvullingService(StartvullingToepassenHandler handler, ILogger<StartvullingService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly StartvullingToepassenHandler handler = handler ?? throw new ArgumentNullException(nameof(handler));
    private readonly ILogger<StartvullingService> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Applies the seed when the list is empty.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes once seeded (or skipped).</returns>
    public async Task ToepassenAsync(CancellationToken cancellationToken = default)
    {
        var result = await handler.HandleAsync(new StartvullingToepassen { Klussen = Lezen() }, cancellationToken);

        if (!result.IsValid)
        {
            logger.LogWarning("Startvulling deels mislukt: {Reden}", string.Join("; ", result.Messages.Select(m => m.Message)));
        }
    }

    /// <summary>Reads the embedded seed list.</summary>
    /// <returns>The klussen, in file order.</returns>
    internal static IReadOnlyList<Klus.Data> Lezen()
    {
        using var stream = typeof(StartvullingService).Assembly.GetManifestResourceStream("Kluskat.infrastructure.Services.startvulling.json")
            ?? throw new InvalidOperationException("Embedded resource startvulling.json is missing.");

        var regels = JsonSerializer.Deserialize<List<Regel>>(stream, JsonOptions)
            ?? throw new InvalidDataException("startvulling.json is empty.");

        return [.. regels.Select(r => new Klus.Data { Ruimte = r.Ruimte, Naam = r.Naam, IntervalDagen = r.Dagen, Grootte = r.Grootte })];
    }

    private sealed record Regel(string Ruimte, string Naam, int Dagen, Grootte Grootte);
}
