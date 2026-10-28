using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Kluskat.WebApp.Pages.Dev;

/// <summary>
/// Every form, pose and accessory of the cat side by side, for drawing the pixel art. Exists only in
/// Development; anywhere else it is a 404.
/// </summary>
[AllowAnonymous]
public sealed class KattenModel(IWebHostEnvironment omgeving) : PageModel
{
    /// <summary>Shows the gallery.</summary>
    /// <returns>The page, or 404 outside Development.</returns>
    public IActionResult OnGet() => omgeving.IsDevelopment() ? Page() : NotFound();
}
