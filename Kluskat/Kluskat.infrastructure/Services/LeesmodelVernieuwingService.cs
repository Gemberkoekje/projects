using Kluskat.infrastructure.MartenDb;
using Marten;

namespace Kluskat.infrastructure.Services;

/// <summary>
/// Re-projects every <see cref="KlusOverzicht"/> from its stream at start-up and stores the ones
/// that differ. A field added to the read model (such as <see cref="KlusOverzicht.KeerGedaan"/>) is
/// then filled in for klussen that were not saved since, instead of reading as its default. Cheap:
/// one short stream per klus, and nothing is written when nothing changed.
/// </summary>
public sealed class LeesmodelVernieuwingService(
    KlusRepository repository,
    IDocumentStore documentStore,
    ILogger<LeesmodelVernieuwingService> logger)
{
    private readonly KlusRepository repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IDocumentStore documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
    private readonly ILogger<LeesmodelVernieuwingService> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Brings every stored <see cref="KlusOverzicht"/> in line with its stream.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes once every changed document is stored.</returns>
    public async Task VernieuwenAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<KlusOverzicht> opgeslagen;
        using (var query = documentStore.QuerySession())
        {
            opgeslagen = await query.Query<KlusOverzicht>().ToListAsync(cancellationToken);
        }

        using var session = documentStore.LightweightSession();
        var bijgewerkt = 0;

        foreach (var bestaand in opgeslagen)
        {
            var klus = await repository.LadenAsync(bestaand.KlusId, cancellationToken);
            if (!klus.IsValid)
            {
                logger.LogWarning("Leesmodel van klus {KlusId} niet vernieuwd: {Reden}", bestaand.Id, string.Join("; ", klus.Messages.Select(m => m.Message)));
                continue;
            }

            var vers = KlusOverzicht.Van(klus.Value);
            if (vers != bestaand)
            {
                session.Store(vers);
                bijgewerkt++;
            }
        }

        if (bijgewerkt > 0)
        {
            await session.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Leesmodel vernieuwd: {Bijgewerkt} van {Totaal} klussen bijgewerkt", bijgewerkt, opgeslagen.Count);
        }
    }
}
