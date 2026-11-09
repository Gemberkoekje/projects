namespace DatumPrikker.core.Models;

/// <summary>Who is acting: a signed-in account, or <see cref="Anonymous"/>.</summary>
public sealed record Account
{
    /// <summary>Nobody signed in.</summary>
    public static readonly Account Anonymous = new Account();

    /// <summary>The provider-qualified id, such as <c>google:1234</c>; empty when anonymous.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The display name the provider gave.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The email address the provider gave, normalised; empty when it gave none.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>True when nobody is signed in.</summary>
    public bool IsAnonymous => Id.Length == 0;
}
