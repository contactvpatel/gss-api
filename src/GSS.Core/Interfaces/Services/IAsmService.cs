using GSS.Core.Models;

namespace GSS.Core.Interfaces.Services;

/// <summary>
/// ASM HTTP client: a user's application security.
/// </summary>
public interface IAsmService
{
    /// <summary>Gets the user's roles, positions and per-module access.</summary>
    /// <remarks>ASM identifies the user by the bearer token.</remarks>
    Task<IReadOnlyList<AsmResponseModel>> GetApplicationSecurityAsync(
        string token,
        CancellationToken cancellationToken = default);
}
