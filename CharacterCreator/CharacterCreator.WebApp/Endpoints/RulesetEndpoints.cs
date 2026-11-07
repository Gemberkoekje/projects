using CharacterCreator.application.Rulesets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Net.Http.Headers;

namespace CharacterCreator.WebApp.Endpoints;

/// <summary>The published ruleset for the build screen (DESIGN.md §6, data flow).</summary>
internal static class RulesetEndpoints
{
    /// <summary>Maps <c>GET /api/rulesets/current</c>.</summary>
    /// <param name="endpoints">The app's endpoints.</param>
    /// <returns>The same endpoints, for chaining.</returns>
    public static IEndpointRouteBuilder MapRulesetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous: the build screen is for anyone. The ruleset names no SL-only skill.
        endpoints.MapGet("/api/rulesets/current", CurrentAsync).AllowAnonymous();
        return endpoints;
    }

    /// <summary>
    /// The newest published ruleset, whole: rules and content in both languages. A ruleset never changes,
    /// so its id is the ETag, and a browser that has it gets a 304.
    /// </summary>
    private static async Task<IResult> CurrentAsync(HttpContext context, ReadCurrentRulesetHandler handler)
    {
        var current = await handler.HandleAsync(context.RequestAborted);
        if (!current.IsValid)
        {
            return Results.NotFound(new { message = string.Join(" ", current.Messages.Select(message => message.Message)) });
        }

        var etag = new EntityTagHeaderValue($"\"{current.Value.Id}\"");
        context.Response.Headers.ETag = etag.ToString();
        context.Response.Headers.CacheControl = "no-cache";

        if (context.Request.Headers.IfNoneMatch.Any(value => string.Equals(value, etag.ToString(), StringComparison.Ordinal)))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Ok(current.Value);
    }
}
