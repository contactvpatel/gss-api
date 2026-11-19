using GSS.Business.DTOs;

namespace GSS.Business.Services.Interfaces;

/// <summary>
/// A person's application security from ASM.
/// </summary>
public interface IAsmService
{
    /// <summary>
    /// Applications and actions the token's user can use, per role and position, cached until
    /// <paramref name="tokenExpiresAtUtc"/> (null = not cached). ASM identifies the user by the token.
    /// </summary>
    Task<IReadOnlyList<AsmResponseDto>> GetApplicationSecurityAsync(
        string token,
        DateTimeOffset? tokenExpiresAtUtc,
        CancellationToken cancellationToken = default);
}
