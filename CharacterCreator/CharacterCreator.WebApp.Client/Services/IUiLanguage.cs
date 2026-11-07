using CharacterCreator.WebApp.Client.Words;

namespace CharacterCreator.WebApp.Client.Services;

/// <summary>
/// The language the visitor chose. The server reads it from the language cookie while it prerenders
/// the page; the browser reads it from the page that the server rendered (<c>html lang</c>).
/// </summary>
/// <remarks>Public, because the server implements it too.</remarks>
public interface IUiLanguage
{
    /// <summary>The visitor's language.</summary>
    /// <returns>It.</returns>
    ValueTask<Ui> CurrentAsync();
}
