using Marten;

namespace Kluskat.infrastructure.Adapters.Application;

/// <summary>Reads <see cref="KlusOverzicht"/> documents.</summary>
public sealed class MartenKlusOverzichtStore(IDocumentStore store) : IKlusOverzichtStore
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<IReadOnlyList<KlusOverzicht>> ActieveAsync(CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        return await session.Query<KlusOverzicht>().Where(k => !k.Geschrapt).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<KlusOverzicht>> LadenAsync(KlusId id, CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        var document = await session.LoadAsync<KlusOverzicht>((Guid)(Uuid)id, cancellationToken);

        return document is null
            ? Result.WithMessages<KlusOverzicht>(ValidationMessage.Error("Deze klus bestaat niet (meer)."))
            : Result.For(document);
    }

    /// <inheritdoc />
    public async Task<bool> HeeftKlussenAsync(CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        return await session.Query<KlusOverzicht>().AnyAsync(cancellationToken);
    }
}
