using GSS.Api.Authentication;
using GSS.Api.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace GSS.Api.Extensions.Features;

/// <summary>Authentication and authorization registration.</summary>
public static class AuthenticationExtensions
{
    /// <summary>Registers the SSO v1 scheme and a fallback policy requiring an authenticated user.</summary>
    public static void ConfigureAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(SsoAuthenticationDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, SsoAuthenticationHandler>(SsoAuthenticationDefaults.AuthenticationScheme, null);

        // Secure by default: endpoints opt out with [AllowAnonymous] / AllowAnonymous().
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(SsoAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build());

        // [AsmAuthorize] requirements: checked against the caller's selected position. The handler is a singleton
        // (the provider resolves every handler on each authorized request) and resolves its scoped services lazily.
        services.AddScoped<ICallerValidator, CallerValidator>();
        services.AddSingleton<IAuthorizationHandler, AsmPermissionHandler>();
    }
}
