namespace Klaverjas.WebApp.Pages;

/// <summary>Something went wrong; shown instead of a stack trace outside Development.</summary>
[AllowAnonymous]
public sealed class FoutModel : PageModel
{
    /// <summary>Shows the page.</summary>
    public void OnGet()
    {
        // Nothing to load.
    }
}
