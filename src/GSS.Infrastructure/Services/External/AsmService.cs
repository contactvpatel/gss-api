using GSS.Core.Interfaces.Services;
using GSS.Core.Models;
using GSS.Infrastructure.Helpers;
using GSS.Infrastructure.Interfaces;
using GSS.Util.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GSS.Infrastructure.Services.External;

/// <summary>
/// ASM HTTP client: a user's application security. App credentials are client defaults (set at registration);
/// each call adds the user's bearer token.
/// </summary>
public class AsmService(
    HttpClient httpClient,
    IDapperTransactionManager transactionManager,
    IOptions<AsmServiceOptions> options,
    ILogger<AsmService> logger) : HttpServiceBase(httpClient, transactionManager, logger, nameof(AsmService)), IAsmService
{
    /// <inheritdoc />
    /// <remarks>An empty list means "no permissions" and is cached until the token expires, so a failure always throws.</remarks>
    public Task<IReadOnlyList<AsmResponseModel>> GetApplicationSecurityAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        return GetListAsync<AsmResponseModel>(
            options.Value.Endpoint.ApplicationSecurity.EnsureTrailingSlash(),
            request => request.SetBearerToken(token),
            cancellationToken);
    }
}
