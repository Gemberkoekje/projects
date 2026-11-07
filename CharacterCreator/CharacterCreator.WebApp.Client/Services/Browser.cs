using CharacterCreator.WebApp.Client.Words;
using Microsoft.JSInterop;

namespace CharacterCreator.WebApp.Client.Services;

/// <summary>
/// What the build screen needs from the browser, through <c>wwwroot/js/charactercreator.js</c> (in the
/// WebApp): the page's language, local storage, and saving a file.
/// </summary>
/// <remarks>
/// One per browser tab (WebAssembly has one scope). Local storage may be unavailable (a private
/// window, blocked site data): reads then come back empty and writes report failure, never throw.
/// </remarks>
/// <param name="js">The JavaScript runtime.</param>
public sealed class Browser(IJSRuntime js) : IUiLanguage, IAsyncDisposable
{
    private readonly IJSRuntime js = js ?? throw new ArgumentNullException(nameof(js));
    private readonly SemaphoreSlim gate = new (1, 1);
    private IJSObjectReference module = default!;
    private bool imported;

    /// <inheritdoc />
    public async ValueTask<Ui> CurrentAsync() => Ui.For(await CallAsync<string>("language"));

    /// <summary>Reads a value from local storage.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value; empty when there is none or storage is unavailable.</returns>
    public async ValueTask<string> LoadAsync(string key) => await CallAsync<string>("load", key) ?? string.Empty;

    /// <summary>Writes a value to local storage.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>Whether it was stored.</returns>
    public ValueTask<bool> StoreAsync(string key, string value) => CallAsync<bool>("store", key, value);

    /// <summary>Moves focus to the first element that matches one of the selectors, without scrolling.</summary>
    /// <param name="selectors">CSS selectors, the preferred one first.</param>
    /// <returns>Whether an element was focused.</returns>
    public ValueTask<bool> FocusAsync(params string[] selectors) => CallAsync<bool>("focus", selectors, false);

    /// <summary>
    /// <see cref="FocusAsync"/>, but only when focus was lost: the focused element was removed by a
    /// re-render and nothing else has taken focus since.
    /// </summary>
    /// <param name="selectors">CSS selectors, the preferred one first.</param>
    /// <returns>Whether an element was focused.</returns>
    public ValueTask<bool> RestoreFocusAsync(params string[] selectors) => CallAsync<bool>("focus", selectors, true);

    /// <summary>Hands the visitor a file to save.</summary>
    /// <param name="fileName">The file's name.</param>
    /// <param name="contentType">Its media type.</param>
    /// <param name="text">Its contents.</param>
    /// <returns>When done.</returns>
    public async ValueTask DownloadAsync(string fileName, string contentType, string text)
    {
        var loaded = await ModuleAsync();
        await loaded.InvokeVoidAsync("download", fileName, contentType, text);
    }

    /// <summary>Hands the visitor a binary file to save, such as a PDF.</summary>
    /// <param name="fileName">The file's name.</param>
    /// <param name="contentType">Its media type.</param>
    /// <param name="bytes">Its contents.</param>
    /// <returns>When done.</returns>
    public async ValueTask DownloadBytesAsync(string fileName, string contentType, byte[] bytes)
    {
        var loaded = await ModuleAsync();
        await loaded.InvokeVoidAsync("download", fileName, contentType, bytes);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (imported)
        {
            try
            {
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The tab is closing; nothing left to release.
            }
        }

        gate.Dispose();
    }

    private async ValueTask<T> CallAsync<T>(string function, params object[] args)
    {
        var loaded = await ModuleAsync();
        return await loaded.InvokeAsync<T>(function, args);
    }

    private async ValueTask<IJSObjectReference> ModuleAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (!imported)
            {
                module = await js.InvokeAsync<IJSObjectReference>("import", "./js/charactercreator.js");
                imported = true;
            }

            return module;
        }
        finally
        {
            gate.Release();
        }
    }
}
