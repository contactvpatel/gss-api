using Asp.Versioning;
using Dapper;
using GSS.Api.Controllers;
using GSS.Api.Filters;
using GSS.Api.Models;
using GSS.Infrastructure.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GSS.IntegrationTests.Fixtures;

/// <summary>
/// Test-only create endpoints for <see cref="IdempotentAttribute"/>: they insert into <c>gss.idempotency_probe</c> on the
/// Unit of Work transaction's connection, so a test sees the write commit or roll back with the stored response.
/// Added as an application part by <see cref="WebAppFactory"/>.
/// </summary>
[ApiController]
[ApiVersion("1")]
[Route("api/v{version:apiVersion}/test-probe-creates")]
public sealed class IdempotencyProbeController(IDapperTransactionManager transactionManager) : ApiControllerBase
{
    public const string Path = "/api/v1/test-probe-creates";

    public const string TableDdl = "CREATE TABLE IF NOT EXISTS gss.idempotency_probe (name text PRIMARY KEY)";

    private const string InsertSql = "INSERT INTO gss.idempotency_probe (name) VALUES (@Name) ON CONFLICT DO NOTHING";

    /// <summary>Creates one probe row; 409 when the name exists, 400 when it is blank.</summary>
    [HttpPost]
    [Idempotent]
    [ProducesResponseType(typeof(Response<ProbeCreateDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<ProbeCreateDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<ProbeCreateDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Response<ProbeCreateDto>>> Create([FromBody] ProbeCreateDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequestResponse<ProbeCreateDto>("Name is required.");
        }

        if (await InsertAsync(dto.Name, cancellationToken) == 0)
        {
            return ConflictResponse<ProbeCreateDto>("Name is in use.");
        }

        return CreatedAtAction(nameof(Create), new { }, Response<ProbeCreateDto>.Success(dto, "Created"));
    }

    /// <summary>Creates several probe rows.</summary>
    [HttpPost("batch")]
    [Idempotent]
    [ProducesResponseType(typeof(Response<IReadOnlyList<ProbeCreateDto>>), StatusCodes.Status201Created)]
    public async Task<ActionResult<Response<IReadOnlyList<ProbeCreateDto>>>> CreateBatch(
        [FromBody] ProbeCreateDto[] dtos, CancellationToken cancellationToken)
    {
        foreach (var dto in dtos)
        {
            await InsertAsync(dto.Name, cancellationToken);
        }

        return CreatedResponse<IReadOnlyList<ProbeCreateDto>>(dtos, "Created");
    }

    private async Task<int> InsertAsync(string name, CancellationToken cancellationToken)
    {
        // Always inside the filter's transaction: its connection carries the write, so it commits with the stored response.
        var transaction = transactionManager.GetCurrentTransaction()
            ?? throw new InvalidOperationException("The probe needs the idempotency transaction.");
        using (transactionManager.EnterCommand())
        {
            return await transaction.Connection!.ExecuteAsync(
                new CommandDefinition(InsertSql, new { Name = name }, transaction, cancellationToken: cancellationToken));
        }
    }
}

/// <summary>Body of <see cref="IdempotencyProbeController"/>.</summary>
public sealed record ProbeCreateDto
{
    public string Name { get; init; } = "";
}
