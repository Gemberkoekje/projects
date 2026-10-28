namespace Kluskat.WebApp.Pages;

/// <summary>The friendly face of an unhandled exception.</summary>
[AllowAnonymous]
public sealed class FoutModel : PageModel
{
    /// <summary>Shows the page.</summary>
    public void OnGet()
    {
        // The exception was already logged by the exception handler middleware.
    }
}
