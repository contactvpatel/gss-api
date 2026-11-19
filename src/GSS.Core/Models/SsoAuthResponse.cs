using System.Text.Json.Serialization;

namespace GSS.Core.Models;

/// <summary>
/// Response model containing SSO authentication information after token renewal.
/// </summary>
public class SsoAuthResponse
{
    /// <summary>
    /// New access token.
    /// </summary>
    [JsonPropertyName("token")]
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// New refresh token.
    /// </summary>
    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// Additional authentication information from SSO (JSON string).
    /// </summary>
    [JsonPropertyName("authenticationInfo")]
    public string? AuthenticationInfo { get; set; }
}
