namespace GSS.Api.Authentication;

/// <summary>
/// Marks an <c>[AllowAnonymous]</c> action that reads the bearer token from the request itself (e.g. SSO renew, which
/// accepts an expired access token), so the OpenAPI document still lists the bearer requirement.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ReadsBearerTokenAttribute : Attribute;
