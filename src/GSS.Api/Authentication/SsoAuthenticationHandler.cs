using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using GSS.Api.Helpers;
using GSS.Api.Models;
using GSS.Core.Exceptions;
using GSS.Util.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using ISsoService = GSS.Business.Services.Interfaces.ISsoService;

namespace GSS.Api.Authentication;

/// <summary>
/// Authenticates SSO v1 bearer tokens. Tokens are unsigned, so each is validated by SSO through
/// <see cref="ISsoService"/>, which caches valid results by token hash.
/// </summary>
public sealed class SsoAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ISsoService ssoService) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    private const string MissingTokenReason = "Token is missing or invalid";
    private const string SsoUnavailableMessage = "The authentication service is temporarily unavailable. Please try again later.";
    private const string InternalErrorMessage = "An unexpected error occurred. Please try again later.";
    private const string UserIdTag = "user.id";

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = JwtTokenHelper.ExtractBearerToken(Request.Headers.Authorization.FirstOrDefault());
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.NoResult();
        }

        var jwtToken = HasJwsShape(token) ? JwtTokenHelper.ParseToken(token) : null;
        if (jwtToken == null)
        {
            return Fail("Invalid token format");
        }

        // Local checks first, so tokens SSO would reject never cost an SSO call.
        var expiresAt = JwtTokenHelper.GetTokenExpiration(jwtToken);
        if (expiresAt == null)
        {
            return Fail("Token has no expiry");
        }

        var remaining = expiresAt.Value - TimeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            return Fail("Token has expired");
        }

        // Writes and permission checks need a numeric user id, so a token without one is rejected here.
        string? userId = JwtTokenHelper.GetUserId(jwtToken);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Fail("Token has no user id");
        }

        if (!int.TryParse(userId, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return Fail("Invalid token format");
        }

        var ssoFailure = await ValidateWithSsoAsync(token, expiresAt.Value);
        if (ssoFailure != null)
        {
            return ssoFailure;
        }

        // Claims are trusted only after SSO validation. Token is never a claim.
        Context.Items[HttpContextItemKeys.UserToken] = token;

        // OpenTelemetry.NET reads the user at request start, before authentication, so tag the request span here.
        if (Context.Features.Get<IHttpActivityFeature>()?.Activity is { IsAllDataRequested: true } requestActivity)
        {
            requestActivity.SetTag(UserIdTag, userId);
        }

        AuthenticationTicket ticket = new(CreatePrincipal(jwtToken, userId, Scheme.Name), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    /// <inheritdoc />
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var result = await HandleAuthenticateOnceSafeAsync();
        if (result.Failure is ExternalServiceException outage)
        {
            // SSO could not answer: the token may be fine, so not 401.
            if (outage.RetryAfter is { } retryAfter)
            {
                Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            await WriteErrorAsync(StatusCodes.Status503ServiceUnavailable, "Authentication unavailable", SsoUnavailableMessage);
            return;
        }

        if (result.Failure is not null and not AuthenticationFailureException)
        {
            // Our error, not a verdict on the token: no 401 or WWW-Authenticate, so clients do not force a re-login.
            await WriteErrorAsync(StatusCodes.Status500InternalServerError, "Internal server error", InternalErrorMessage);
            return;
        }

        Response.Headers.WWWAuthenticate = "Bearer";
        await WriteErrorAsync(StatusCodes.Status401Unauthorized, "Authentication failed", result.Failure?.Message ?? MissingTokenReason);
    }

    /// <inheritdoc />
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        return WriteErrorAsync(StatusCodes.Status403Forbidden, "Access denied", "Insufficient permissions");
    }

    /// <summary>Builds the principal from token claims; pid becomes NameIdentifier.</summary>
    internal static ClaimsPrincipal CreatePrincipal(JsonWebToken jwtToken, string userId, string authenticationType)
    {
        List<Claim> claims = [.. jwtToken.Claims, new Claim(ClaimTypes.NameIdentifier, userId)];

        ClaimsIdentity identity = new(claims, authenticationType, ClaimTypes.NameIdentifier, ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>header.payload.signature with no empty part: SSO issues signed tokens even though we cannot verify them.</summary>
    private static bool HasJwsShape(string token)
    {
        // Span-based: no array or substrings on every request.
        ReadOnlySpan<char> rest = token;
        int first = rest.IndexOf('.');
        if (first <= 0)
        {
            return false;
        }

        rest = rest[(first + 1)..];
        int second = rest.IndexOf('.');
        if (second <= 0)
        {
            return false;
        }

        var signature = rest[(second + 1)..];
        return signature.Length > 0 && !signature.Contains('.');
    }

    /// <summary>A local rejection: Information, not Warning, since any client can send garbage and this runs before rate limiting.</summary>
    private AuthenticateResult Fail(string reason)
    {
        Logger.TokenRejectedLocally(reason, Request.Path.Value);
        return AuthenticateResult.Fail(reason);
    }

    /// <summary>
    /// SSO validation (cached by the service): null when valid. An outage or an internal error fails with the exception
    /// itself, which the challenge turns into 503 or 500 (never 401); <c>[AllowAnonymous]</c> endpoints are unaffected.
    /// </summary>
    private async Task<AuthenticateResult?> ValidateWithSsoAsync(string token, DateTimeOffset expiresAt)
    {
        try
        {
            if (await ssoService.ValidateTokenAsync(token, expiresAt, Context.RequestAborted))
            {
                return null;
            }

            Logger.TokenRejectedBySso(Request.Path.Value);
            return AuthenticateResult.Fail("Token validation failed");
        }
        catch (ExternalServiceException ex)
        {
            Logger.LogError(ex, "SSO unavailable; token not validated. Service: {Service}, Failure: {Failure}, Path: {Path}", ex.ServiceName, ex.Failure, Request.Path);
            return AuthenticateResult.Fail(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Token validation error (not a rejection). Path: {Path}", Request.Path);
            return AuthenticateResult.Fail(ex);
        }
    }

    private Task WriteErrorAsync(int statusCode, string message, string detail)
    {
        Response.StatusCode = statusCode;
        Response<object?> body = Response<object?>.Failure(message,
        [
            new ApiError { ErrorId = ApiErrorId.For(Context), StatusCode = (short)statusCode, Message = detail }
        ]);

        return Response.WriteAsJsonAsync(body, Context.RequestAborted);
    }
}
