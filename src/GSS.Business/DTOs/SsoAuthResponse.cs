using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace GSS.Business.DTOs;

/// <summary>
/// Response DTO containing SSO authentication information after token renewal.
/// </summary>
public sealed record SsoAuthResponse
{
    /// <summary>
    /// New access token.
    /// </summary>
    public required string AccessToken { get; init; }

    /// <summary>
    /// New refresh token.
    /// </summary>
    public required string RefreshToken { get; init; }

    /// <summary>
    /// Additional authentication information from SSO (JSON string).
    /// </summary>
    public string? AuthenticationInfo { get; init; }

    /// <summary>Prints only non-secret members, so a logged or asserted response never shows its tokens.</summary>
    [SuppressMessage("CodeQuality", "IDE0051", Justification = "Called by the compiler-generated ToString.")]
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("AuthenticationInfo = ").Append(AuthenticationInfo ?? "null");
        return true;
    }
}

