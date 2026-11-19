using GSS.Core.Interfaces.Services;
using GSS.Core.Models;
using GSS.Infrastructure.Helpers;
using GSS.Infrastructure.Interfaces;
using GSS.Util.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GSS.Infrastructure.Services.External;

/// <summary>
/// MIS HTTP client. App credentials are client defaults (set at registration); an upstream failure throws
/// <see cref="Core.Exceptions.ExternalServiceException"/>, never an empty result.
/// </summary>
public class MisService(
    HttpClient httpClient,
    IDapperTransactionManager transactionManager,
    IOptions<MisServiceOptions> options,
    ILogger<MisService> logger) : HttpServiceBase(httpClient, transactionManager, logger, nameof(MisService)), IMisService
{
    private MisServiceEndpoints Endpoint => options.Value.Endpoint;

    /// <inheritdoc />
    public Task<IReadOnlyList<DepartmentModel>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        return GetListAsync<DepartmentModel>(Endpoint.Department, null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RoleTypeModel>> GetAllRoleTypesAsync(int divisionId, CancellationToken cancellationToken = default)
    {
        return GetListAsync<RoleTypeModel>($"{Endpoint.RoleType}?divId={divisionId}", null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RoleModel>> GetAllRolesAsync(int divisionId, CancellationToken cancellationToken = default)
    {
        return GetListAsync<RoleModel>($"{Endpoint.Role}?divId={divisionId}", null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<RoleModel?> GetRoleByIdAsync(int roleId, CancellationToken cancellationToken = default)
    {
        return GetSingleOrNullAsync<RoleModel>($"{Endpoint.Role}?roleId={roleId}", r => r.Id == roleId, null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RoleModel>> GetRolesByDepartmentIdAsync(int departmentId, CancellationToken cancellationToken = default)
    {
        return GetListAsync<RoleModel>($"{Endpoint.Role}?deptId={departmentId}", null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PositionModel>> GetPositionsByRoleIdAsync(int roleId, CancellationToken cancellationToken = default)
    {
        return GetListAsync<PositionModel>($"{Endpoint.Position}?roleId={roleId}", null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PersonPositionModel>> GetPersonPositionsAsync(string personId, CancellationToken cancellationToken = default)
    {
        return GetListAsync<PersonPositionModel>($"{Endpoint.PersonPosition}?personId={Uri.EscapeDataString(personId)}", null, cancellationToken);
    }
}
