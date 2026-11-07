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

// Focus the first element matching one of the selectors, in order. With onlyIfLost, only when focus
// was lost: a re-render that removed the focused button (an add turns "+" into a stepper) drops it to
// the page, and nothing else has claimed it since.
export function focus(selectors, onlyIfLost) {
    const active = document.activeElement;
    if (onlyIfLost && active && active !== document.body && active.isConnected) {
        return false;
    }
    for (const selector of selectors) {
        const element = document.querySelector(selector);
        if (element) {
            element.focus({ preventScroll: true });
            return true;
        }
    }
    return false;
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
