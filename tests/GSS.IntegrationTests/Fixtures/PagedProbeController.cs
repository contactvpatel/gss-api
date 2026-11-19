using Asp.Versioning;
using GSS.Api.Controllers;
using GSS.Api.Models;
using GSS.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GSS.IntegrationTests.Fixtures;

/// <summary>
/// Test-only endpoint for list, numbered page, and cursor page responses, so the OpenAPI
/// document's list-or-page shape (<c>MultipleOkResponsesOperationTransformer</c>) is tested without a feature
/// controller. Added as an application part by <see cref="WebAppFactory"/>.
/// </summary>
[ApiController]
[ApiVersion("1")]
[Route("api/v{version:apiVersion}/test-probe-items")]
public sealed class PagedProbeController : ApiControllerBase
{
    public const string Path = "/api/v1/test-probe-items";
    private static readonly ProbeItemDto[] Items =
    [
        new() { Id = 1, Name = "First" },
        new() { Id = 3, Name = "Second" }
    ];

    /// <summary>Gets the probe items with optional numbered or cursor pagination.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<IReadOnlyList<ProbeItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<PagedResult<ProbeItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<CursorPagedResult<ProbeItemDto>>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetAll([FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        return GetPagedOrAllAsync<ProbeItemDto>(
            pagination,
            _ => Task.FromResult<IReadOnlyList<ProbeItemDto>>(Items),
            (page, size, total, _) => Task.FromResult(Slice(Items.Skip((page - 1) * size), size, total)),
            (afterId, size, total, _) => Task.FromResult(Slice(Items.Where(item => item.Id > afterId), size, total)),
            "Probe items",
            cancellationToken);
    }

    private static PageSlice<ProbeItemDto> Slice(IEnumerable<ProbeItemDto> source, int size, bool total)
    {
        var candidates = source.Take(size + 1).ToArray();
        return new(candidates.Take(size).ToArray(), candidates.Length > size ? candidates[size - 1].Id : null,
            total ? Items.Length : null);
    }
}

/// <summary>Item returned by <see cref="PagedProbeController"/>.</summary>
public sealed record ProbeItemDto
{
    public int Id { get; init; }

    public required string Name { get; init; }
}
