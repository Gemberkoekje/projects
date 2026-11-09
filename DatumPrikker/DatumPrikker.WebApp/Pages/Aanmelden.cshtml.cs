using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DatumPrikker.WebApp.Pages;

/// <summary>Offers the configured sign-in providers (D5), and the dev login in Development.</summary>
[AllowAnonymous]
public sealed class AanmeldenModel(IConfiguration configuration, IWebHostEnvironment environment) : PageModel
{
    private readonly IConfiguration configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly IWebHostEnvironment environment = environment ?? throw new ArgumentNullException(nameof(environment));

    /// <summary>Where to go after signing in; only local URLs are honoured.</summary>
    [BindProperty(SupportsGet = true)]
    public string ReturnUrl { get; set; } = "/";

    /// <summary>The providers to show a button for.</summary>
    public IReadOnlyList<LoginProvider> Providers { get; private set; } = [];

    /// <summary>True in Development, where the dev login stands in for the providers.</summary>
    public bool DevLogin => environment.IsDevelopment();

    /// <summary>Shows the buttons.</summary>
    public void OnGet()
    {
        ReturnUrl = SafeReturnUrl();
        Providers = LoginProviders.Configured(configuration);
    }

    /// <summary>Hands over to the chosen provider.</summary>
    /// <param name="provider">The provider's scheme, from the button.</param>
    /// <returns>The provider's challenge, or this page again for an unknown provider.</returns>
    public IActionResult OnPost(string provider)
    {
        var chosen = LoginProviders.Configured(configuration).FirstOrDefault(candidate => candidate.Scheme == provider);
        if (chosen is null)
        {
            return RedirectToPage(new { ReturnUrl = SafeReturnUrl() });
        }

        return Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl() }, chosen.Scheme);
    }

    private string SafeReturnUrl() => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";
}
