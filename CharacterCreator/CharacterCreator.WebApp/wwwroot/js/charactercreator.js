// What the build screen (CharacterCreator.WebApp.Client, Services/Browser.cs) needs from the browser.
// Storage may be unavailable (a private window, blocked site data): reads then return "" and writes
// false, so the screen carries on without it.

export function language() {
    return document.documentElement.lang || "nl";
}

export function load(key) {
    try {
        return window.localStorage.getItem(key) ?? "";
    } catch {
        return "";
    }
}

export function store(key, value) {
    try {
        window.localStorage.setItem(key, value);
        return true;
    } catch {
        return false;
    }
}

export function download(fileName, contentType, contents) {
    // Text from DownloadAsync, a Uint8Array from DownloadBytesAsync (Blazor sends byte[] as one).
    const url = URL.createObjectURL(new Blob([contents], { type: contentType }));
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
