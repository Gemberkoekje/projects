namespace FrontPage.WebApp;

/// <summary>
/// The headers every response gets. The page loads nothing from elsewhere and has no inline script
/// or style, so the policy is <c>'self'</c> throughout. HSTS comes from Traefik.
/// </summary>
internal static class SecurityHeaders
{
    /// <summary>The Content-Security-Policy.</summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>Adds the headers, then runs the rest of the pipeline.</summary>
    /// <param name="context">The request.</param>
    /// <param name="next">The rest of the pipeline.</param>
    /// <returns>The pipeline's task.</returns>
    public static Task Add(HttpContext context, RequestDelegate next)
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        return next(context);
    }
}
