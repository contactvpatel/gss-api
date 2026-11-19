using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace GSS.Business.DTOs;

/// <summary>
/// Request DTO for renewing an SSO authentication token. Validated by <c>SsoRenewTokenRequestValidator</c>.
/// </summary>
public sealed record SsoRenewTokenRequest
{
    /// <summary>
    /// Refresh token used to obtain a new access token.
    /// </summary>
    public string RefreshToken { get; init; } = string.Empty;

    /// <summary>Prints no members: the only one is a secret.</summary>
    [SuppressMessage("CodeQuality", "IDE0051", Justification = "Called by the compiler-generated ToString.")]
    [SuppressMessage("CodeQuality", "IDE0060", Justification = "Signature fixed by the record ToString contract.")]
    private bool PrintMembers(StringBuilder builder)
    {
        return false;
    }
}

