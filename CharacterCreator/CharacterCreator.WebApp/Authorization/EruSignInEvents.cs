using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace CharacterCreator.WebApp.Authorization;

/// <summary>What happens around a Vortex ERU sign-in: the permissions in, a failure explained.</summary>
/// <remarks>
/// Resolved from DI per sign-in (<c>options.EventsType</c>), so it gets its collaborators injected
/// like any other scoped service.
/// </remarks>
/// <param name="logger">Logs failed sign-ins.</param>
internal sealed class EruSignInEvents(ILogger<EruSignInEvents> logger) : OpenIdConnectEvents
{
    private readonly ILogger<EruSignInEvents> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Adds one <see cref="EruClaims.Permission"/> claim per ERU permission in the access token.</summary>
    /// <param name="context">The validated sign-in.</param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    /// The permissions ride only in the access token, not in the id token, so they are read here,
    /// where the token endpoint's response is still at hand.
    /// </remarks>
    public override Task TokenValidated(TokenValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Principal?.Identity is ClaimsIdentity identity)
        {
            foreach (var permission in EruPermissions.Read(context.TokenEndpointResponse?.AccessToken ?? string.Empty))
            {
                identity.AddClaim(new Claim(EruClaims.Permission, permission));
            }
        }

        return base.TokenValidated(context);
    }

    /// <summary>Logs a failed sign-in and sends the visitor to a page that says so, instead of an error page.</summary>
    /// <param name="context">The failure.</param>
    /// <returns>A completed task.</returns>
    public override Task RemoteFailure(RemoteFailureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        logger.LogError(context.Failure, "Vortex ERU sign-in failed");

        context.Response.Redirect($"{context.Request.PathBase}/aanmelden-mislukt");
        context.HandleResponse();

        return Task.CompletedTask;
    }
}
