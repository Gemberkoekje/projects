namespace Klaverjas.application.Ports;

/// <summary>Keeps a planned import between reading the workbook and pressing "Inlezen".</summary>
public interface IImportVoorstelStore
{
    /// <summary>One plan.</summary>
    /// <param name="id">The plan's identity.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The plan, or an error when it is gone (applied, cancelled or never there).</returns>
    Task<Result<ImportVoorstel>> LadenAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Adds or replaces a plan.</summary>
    /// <param name="voorstel">The plan.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes once stored.</returns>
    Task OpslaanAsync(ImportVoorstel voorstel, CancellationToken cancellationToken = default);

    /// <summary>Removes a plan; removing one that is gone is not an error.</summary>
    /// <param name="id">The plan's identity.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>A task that completes once removed.</returns>
    Task VerwijderenAsync(Guid id, CancellationToken cancellationToken = default);
}
