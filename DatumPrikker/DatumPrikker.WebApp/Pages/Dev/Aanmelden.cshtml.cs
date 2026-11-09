using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System.ComponentModel.DataAnnotations;

namespace DatumPrikker.WebApp.Pages.Dev;

/// <summary>Signs in as anyone, without a provider. Development only: anywhere else it is a 404.</summary>
[AllowAnonymous]
public sealed class AanmeldenModel(IWebHostEnvironment environment) : PageModel
{
    private readonly IWebHostEnvironment environment = environment ?? throw new ArgumentNullException(nameof(environment));

    /// <summary>Where to go afterwards; only local URLs are honoured.</summary>
    [BindProperty(SupportsGet = true)]
    public string ReturnUrl { get; set; } = "/";

    /// <summary>Any name.</summary>
    [BindProperty]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Name { get; set; } = "Dev";

    /// <summary>Any email address; it doubles as the account's subject.</summary>
    [BindProperty]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Email { get; set; } = "dev@example.com";

    /// <summary>Shows the form.</summary>
    /// <returns>The page, or 404 outside Development.</returns>
    public IActionResult OnGet() => environment.IsDevelopment() ? Page() : NotFound();

    /// <summary>Signs in.</summary>
    /// <returns>A redirect to <see cref="ReturnUrl"/>, or 404 outside Development.</returns>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(Name) || !core.Models.Email.TryNormalise(Email, out var email))
        {
            ModelState.AddModelError(string.Empty, "Vul een naam en een geldig e-mailadres in.");
            return Page();
        }

        var principal = AccountClaims.ForDev(Name.Trim(), email, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/");
    }
}
