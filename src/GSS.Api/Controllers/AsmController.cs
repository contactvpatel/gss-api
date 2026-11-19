using Asp.Versioning;
using GSS.Api.Models;
using GSS.Business.DTOs;
using GSS.Business.Services.Interfaces;
using GSS.Core.Interfaces.Base;
using Microsoft.AspNetCore.Mvc;

namespace GSS.Api.Controllers;

/// <summary>
/// Application Security Management (ASM) endpoints.
/// </summary>
[ApiController]
[ApiVersion("1")]
[Route("api/v{version:apiVersion}/asm")]
[Produces("application/json")]
public class AsmController(IAsmService asmService, IUserContext userContext, ILogger<AsmController> logger) : ApiControllerBase
{
    /// <summary>
    /// Gets the current user ASM access entries (empty if none).
    /// </summary>
    /// <returns>Current user access entries.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<IReadOnlyList<AsmResponseDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Response<IReadOnlyList<AsmResponseDto>>>> Get(CancellationToken cancellationToken)
    {
        string? personId = userContext.GetUserId();
        string? token = userContext.GetToken();

        if (string.IsNullOrWhiteSpace(personId) || string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Person ID or token not available in context");
            return UnauthorizedResponse<IReadOnlyList<AsmResponseDto>>("Person ID or token not available", "Person ID or token not available");
        }

        var accessPermissions = await asmService.GetApplicationSecurityAsync(token, userContext.GetTokenExpiresAtUtc(), cancellationToken);

        if (accessPermissions.Count == 0)
        {
            logger.LogDebug("No application security entries. PersonId: {PersonId}", personId);
            return OkResponse(accessPermissions, $"No application security found for Person ID: {personId}");
        }

        return OkResponse(accessPermissions, "Application security retrieved successfully");
    }
}
