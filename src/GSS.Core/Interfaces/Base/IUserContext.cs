namespace GSS.Core.Interfaces.Base;

/// <summary>
/// Current caller's identity and token.
/// </summary>
public interface IUserContext
{
    /// <summary>User ID (<c>pid</c> claim), or null.</summary>
    string? GetUserId();

    /// <summary>Caller's SSO token, or null.</summary>
    string? GetToken();

    /// <summary>Expiry (UTC) of the authenticated caller's token, or null when unknown.</summary>
    DateTimeOffset? GetTokenExpiresAtUtc();
}
