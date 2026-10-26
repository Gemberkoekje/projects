using System.Resources;

// The templates in Text/Templates.resx are English; this stops the resource manager probing for an
// English satellite assembly (in the browser, a request for a file that doesn't exist).
[assembly: NeutralResourcesLanguage("en")]
