using Kluskat.infrastructure.MartenDb;
using Marten;

namespace Kluskat.infrastructure.Adapters.Application;

/// <summary>Saves a klus stream, then refreshes its <see cref="KlusOverzicht"/> document.</summary>
public sealed class MartenKlusRepository(
    KlusRepository repository,
    IDocumentStore documentStore,
    ILogger<MartenKlusRepository> logger) : IKlusRepository
{
    private readonly KlusRepository repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IDocumentStore documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
    private readonly ILogger<MartenKlusRepository> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public Task<Result<Klus>> LadenAsync(KlusId id, CancellationToken cancellationToken = default)
        => repository.LadenAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<Result> OpslaanAsync(Klus klus, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(klus);

        return AppendAsync(klus, cancellationToken);
    }

    private async Task<Result> AppendAsync(Klus klus, CancellationToken cancellationToken)
    {
        var appended = await repository.OpslaanAsync(klus, cancellationToken);
        if (!appended.IsValid)
        {
            return appended;
        }

        try
        {
            using var session = documentStore.LightweightSession();
            session.Store(KlusOverzicht.Van(klus));
            await session.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The events landed; only the document is behind, until the next save of this klus.
            logger.LogError(exception, "Leesmodel van klus {KlusId} niet bijgewerkt.", klus.Id);
        }

        return appended;
    }
}
