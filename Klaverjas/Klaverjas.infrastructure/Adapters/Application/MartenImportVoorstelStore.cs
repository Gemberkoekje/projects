using Marten;

namespace Klaverjas.infrastructure.Adapters.Application;

/// <summary>Keeps planned imports as <see cref="ImportVoorstel"/> documents until they are applied or cancelled.</summary>
public sealed class MartenImportVoorstelStore(IDocumentStore store) : IImportVoorstelStore
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<Result<ImportVoorstel>> LadenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        var voorstel = await session.LoadAsync<ImportVoorstel>(id, cancellationToken);
        return voorstel is null
            ? Result.WithMessages<ImportVoorstel>(ValidationMessage.Error("Dit Excel-bestand is al ingelezen of geannuleerd. Kies het bestand opnieuw."))
            : Result.For(voorstel);
    }

    /// <inheritdoc />
    public Task OpslaanAsync(ImportVoorstel voorstel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(voorstel);

        return StoreAsync(voorstel, cancellationToken);
    }

    /// <inheritdoc />
    public async Task VerwijderenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var session = store.LightweightSession();
        session.Delete<ImportVoorstel>(id);
        await session.SaveChangesAsync(cancellationToken);
    }

    private async Task StoreAsync(ImportVoorstel voorstel, CancellationToken cancellationToken)
    {
        using var session = store.LightweightSession();
        session.Store(voorstel);
        await session.SaveChangesAsync(cancellationToken);
    }
}
