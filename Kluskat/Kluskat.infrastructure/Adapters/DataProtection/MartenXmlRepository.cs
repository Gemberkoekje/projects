using Marten;
using Microsoft.AspNetCore.DataProtection.Repositories;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace Kluskat.infrastructure.Adapters.DataProtection;

/// <summary>One data-protection key, as XML.</summary>
public sealed record DataProtectionSleutel
{
    /// <summary>The key's friendly name, e.g. <c>key-1b2c…</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The key element as ASP.NET Core wrote it.</summary>
    public string Xml { get; init; } = string.Empty;
}

/// <summary>
/// Keeps the data-protection key ring in PostgreSQL. Without a persisted ring every deploy hands out
/// fresh keys and the year-long sign-in cookie stops validating: you would be logged out by every
/// image bump. The pod has no volume of its own, and the database is already backed up.
/// </summary>
/// <remarks>
/// The keys are stored unencrypted: whoever can read the database can forge a sign-in cookie. For a
/// one-user chore app whose database sits behind the cluster network that is an accepted trade-off.
/// </remarks>
public sealed class MartenXmlRepository(IDocumentStore store) : IXmlRepository
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    [SuppressMessage("Major Code Smell", "S4462:Calls to \"async\" methods should not be blocking", Justification = "IXmlRepository is synchronous by contract; ASP.NET Core reads the ring once and caches it.")]
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var session = store.QuerySession();
        var sleutels = session.Query<DataProtectionSleutel>().ToListAsync().GetAwaiter().GetResult();
        return [.. sleutels.Select(s => XElement.Parse(s.Xml))];
    }

    /// <inheritdoc />
    [SuppressMessage("Major Code Smell", "S4462:Calls to \"async\" methods should not be blocking", Justification = "IXmlRepository is synchronous by contract; a key is written once every 90 days.")]
    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        using var session = store.LightweightSession();
        session.Store(new DataProtectionSleutel
        {
            Id = string.IsNullOrWhiteSpace(friendlyName) ? Guid.NewGuid().ToString("N") : friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
        });
        session.SaveChangesAsync().GetAwaiter().GetResult();
    }
}
