using CharacterCreator.WebApp.Client.Services;
using CharacterCreator.WebApp.Client.Words;

namespace CharacterCreator.WebApp.Localization;

/// <summary>
/// The visitor's language on the server: the language cookie, Dutch without it (DESIGN.md decision
/// 11). The server pages read it, and so does the build screen while the server prerenders it.
/// </summary>
/// <remarks>Scoped: one per request.</remarks>
/// <param name="accessor">The current request.</param>
internal sealed class CookieUiLanguage(IHttpContextAccessor accessor) : IUiLanguage
{
    /// <summary>The cookie's name; its value is <c>nl</c> or <c>en</c>.</summary>
    public const string CookieName = "charactercreator.taal";

    private readonly IHttpContextAccessor accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));

    /// <summary>The language of the current request.</summary>
    public Ui Current => accessor.HttpContext is { } context ? From(context) : Ui.Dutch;

    /// <summary>The language a request asks for.</summary>
    /// <param name="context">The request.</param>
    /// <returns>Its language.</returns>
    public static Ui From(HttpContext context)
        => context is null ? Ui.Dutch : Ui.For(context.Request.Cookies[CookieName] ?? string.Empty);

    /// <inheritdoc />
    public ValueTask<Ui> CurrentAsync() => ValueTask.FromResult(Current);
}
