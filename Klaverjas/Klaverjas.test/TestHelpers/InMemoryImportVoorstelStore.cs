namespace Klaverjas.test.TestHelpers;

internal sealed class InMemoryImportVoorstelStore : IImportVoorstelStore
{
    private readonly Dictionary<Guid, ImportVoorstel> voorstellen = [];

    public IReadOnlyCollection<ImportVoorstel> Alle => voorstellen.Values;

    public Task<Result<ImportVoorstel>> LadenAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(voorstellen.TryGetValue(id, out var voorstel)
            ? Result.For(voorstel)
            : Result.WithMessages<ImportVoorstel>(ValidationMessage.Error("Al ingelezen of geannuleerd.")));

    public Task OpslaanAsync(ImportVoorstel voorstel, CancellationToken cancellationToken = default)
    {
        voorstellen[voorstel.Id] = voorstel;
        return Task.CompletedTask;
    }

    public Task VerwijderenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        voorstellen.Remove(id);
        return Task.CompletedTask;
    }
}
