using Marten;
using Microsoft.AspNetCore.DataProtection.Repositories;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace CharacterCreator.infrastructure.Adapters.DataProtection;

/// <summary>One data-protection key, as XML.</summary>
public sealed record DataProtectionKeyDocument
{
    /// <summary>The key's friendly name, e.g. <c>key-1b2c…</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The key element as ASP.NET Core wrote it.</summary>
    public string Xml { get; init; } = string.Empty;
}

/// <summary>
/// Keeps the data-protection key ring in PostgreSQL. The pod has no volume of its own, so without a
/// persisted ring every deploy would hand out fresh keys: sign-in cookies and the antiforgery
/// tokens of forms already on screen would stop validating at every image bump.
/// </summary>
/// <remarks>
/// The keys are stored unencrypted, as in Kluskat: whoever can read the database can forge a
/// sign-in cookie. The database sits behind the cluster network, and a sign-in grants no more than
/// a player's own read-only view of VOID (v1).
/// </remarks>
/// <param name="store">The Marten document store.</param>
public sealed class MartenXmlRepository(IDocumentStore store) : IXmlRepository
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    [SuppressMessage("Major Code Smell", "S4462:Calls to \"async\" methods should not be blocking", Justification = "IXmlRepository is synchronous by contract; ASP.NET Core reads the ring once and caches it.")]
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var session = store.QuerySession();
        var keys = session.Query<DataProtectionKeyDocument>().ToListAsync().GetAwaiter().GetResult();
        return [.. keys.Select(key => XElement.Parse(key.Xml))];
    }

    /// <inheritdoc />
    [SuppressMessage("Major Code Smell", "S4462:Calls to \"async\" methods should not be blocking", Justification = "IXmlRepository is synchronous by contract; a key is written once every 90 days.")]
    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        using var session = store.LightweightSession();
        session.Store(new DataProtectionKeyDocument
        {
            Id = string.IsNullOrWhiteSpace(friendlyName) ? Guid.NewGuid().ToString("N") : friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
        });
        session.SaveChangesAsync().GetAwaiter().GetResult();
    }
}
