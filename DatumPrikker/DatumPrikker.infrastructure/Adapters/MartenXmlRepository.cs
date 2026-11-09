using Marten;
using Microsoft.AspNetCore.DataProtection.Repositories;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace DatumPrikker.infrastructure.Adapters;

/// <summary>One data-protection key, as XML.</summary>
public sealed record DataProtectionKey
{
    /// <summary>The key's friendly name, such as <c>key-1b2c…</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The key element as ASP.NET Core wrote it.</summary>
    public string Xml { get; init; } = string.Empty;
}

/// <summary>
/// Keeps the data-protection key ring in PostgreSQL (copied from Kluskat). The pod has no volume, so
/// without a persisted ring every deploy would hand out fresh keys and sign everybody out, and a
/// half-filled form would fail its antiforgery check.
/// </summary>
/// <remarks>
/// The keys are stored unencrypted: whoever can read the database can forge a sign-in cookie. The
/// database sits behind the cluster network, which is the accepted trade-off here too.
/// </remarks>
public sealed class MartenXmlRepository(IDocumentStore store) : IXmlRepository
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    [SuppressMessage("Major Code Smell", "S4462:Calls to \"async\" methods should not be blocking", Justification = "IXmlRepository is synchronous by contract; ASP.NET Core reads the ring once and caches it.")]
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var session = store.QuerySession();
        var keys = session.Query<DataProtectionKey>().ToListAsync().GetAwaiter().GetResult();
        return [.. keys.Select(key => XElement.Parse(key.Xml))];
    }

    /// <inheritdoc />
    [SuppressMessage("Major Code Smell", "S4462:Calls to \"async\" methods should not be blocking", Justification = "IXmlRepository is synchronous by contract; a key is written once every 90 days.")]
    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        using var session = store.LightweightSession();
        session.Store(new DataProtectionKey
        {
            Id = string.IsNullOrWhiteSpace(friendlyName) ? Guid.NewGuid().ToString("N") : friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
        });
        session.SaveChangesAsync().GetAwaiter().GetResult();
    }
}
