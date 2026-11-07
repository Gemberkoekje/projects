using System.Text.Json;
using CharacterCreator.application.Ports;
using CharacterCreator.application.Rulesets;
using CharacterCreator.core.Plans;
using CharacterCreator.core.Rulesets;
using CharacterCreator.WebApp.Client.Services;
using CharacterCreator.WebApp.Client.Words;
using CharacterCreator.WebApp.Localization;
using CharacterCreator.WebApp.Printing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Qowaiv;

namespace CharacterCreator.WebApp.Endpoints;

/// <summary>
/// Printing a plan (DESIGN.md §5, Print): the build screen sends the plan it keeps in the browser,
/// and gets the character sheet back as a PDF. Nothing is stored and the plan isn't logged:
/// anonymous plans never stay on the server (DESIGN.md §6).
/// </summary>
internal static class SheetEndpoints
{
    /// <summary>The rate-limiting policy: a PDF costs real CPU, and the endpoint is open to anyone.</summary>
    public const string RateLimit = "sheet";

    /// <summary>The most skills a plan may name; a real plan has a few dozen.</summary>
    public const int MaxSkills = 300;

    /// <summary>The largest request body accepted; a real plan is a few kilobytes.</summary>
    public const long MaxBody = 64 * 1024;

    /// <summary>Maps <c>POST /api/plans/sheet</c>.</summary>
    /// <param name="endpoints">The app's endpoints.</param>
    /// <returns>The same endpoints, for chaining.</returns>
    public static IEndpointRouteBuilder MapSheetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/plans/sheet", SheetAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimit)
            .WithMetadata(new BodyLimit(MaxBody));
        return endpoints;
    }

    /// <summary>
    /// The sheet for the plan in the body, checked against the current ruleset (as the build screen
    /// checks it), in the visitor's language.
    /// </summary>
    private static async Task<IResult> SheetAsync(HttpContext context, ReadCurrentRulesetHandler rulesets, ISheetWriter writer, CookieUiLanguage languages)
    {
        if (!context.Request.HasJsonContentType())
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        PlanFile file;
        try
        {
            file = await context.Request.ReadFromJsonAsync(RulesetJsonContext.Default.PlanFile, context.RequestAborted) ?? new PlanFile();
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { message = "The body is not a plan." });
        }

        if (file.Skills.Count > MaxSkills)
        {
            return Results.BadRequest(new { message = $"A plan names at most {MaxSkills} skills." });
        }

        var current = await rulesets.HandleAsync(context.RequestAborted);
        if (!current.IsValid)
        {
            return Results.NotFound(new { message = "No ruleset has been published yet." });
        }

        var loaded = RulesetClient.Prepare(current.Value);
        if (!loaded.IsValid)
        {
            return Results.Problem("The published ruleset cannot be read.");
        }

        var ui = languages.Current;
        var sheet = SheetComposer.Compose(loaded.Value, file, ui, new DateTimeOffset(Clock.UtcNow(), TimeSpan.Zero));
        var pdf = writer.Write(sheet);

        context.Response.Headers.CacheControl = "no-store";
        return Results.File(pdf.ToArray(), "application/pdf", FileName(sheet.Title, ui));
    }

    /// <summary>The character's name as a file name: letters, digits, dashes and underscores.</summary>
    /// <param name="title">The sheet's title.</param>
    /// <param name="ui">The language, for an unnamed character.</param>
    /// <returns>The file name, such as <c>Ilse-van-de-Vijfhoek.pdf</c>.</returns>
    internal static string FileName(string title, Ui ui)
    {
        var safe = new string([.. (title ?? string.Empty).Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')]);
        while (safe.Contains("--", StringComparison.Ordinal))
        {
            safe = safe.Replace("--", "-", StringComparison.Ordinal);
        }

        safe = safe.Trim('-');
        return $"{(safe.Length > 0 ? safe : ui.T("personage", "character"))}.pdf";
    }

    /// <summary>The request body limit as endpoint metadata, which Kestrel applies per endpoint.</summary>
    /// <param name="max">The most bytes.</param>
    private sealed class BodyLimit(long max) : IRequestSizeLimitMetadata
    {
        /// <inheritdoc />
        public long? MaxRequestBodySize => max;
    }
}
