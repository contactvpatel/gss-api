using System.Globalization;
using System.Net;
using GSS.Api.Helpers;
using GSS.Api.Models;
using GSS.Business.Models;
using GSS.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace GSS.Api.Controllers;

/// <summary>
/// Base controller: <see cref="Response{T}"/> helpers and Result-to-HTTP mapping.
/// </summary>
public abstract class ApiControllerBase : ControllerBase
{
    private ILogger? _logger;

    /// <summary>Logger for the concrete controller's category, from the request's services (none outside a request).</summary>
    protected ILogger Logger => _logger ??=
        HttpContext?.RequestServices?.GetService<ILoggerFactory>()?.CreateLogger(GetType()) ?? NullLogger.Instance;

    /// <summary>
    /// One <see cref="ApiError"/> per detail, or one for the message when there are none.
    /// ErrorId is the request's correlation id (<see cref="ApiErrorId"/>).
    /// </summary>
    private List<ApiError> BuildErrors(HttpStatusCode statusCode, string message, string[] errorDetails)
    {
        short code = (short)statusCode;
        string errorId = ApiErrorId.For(HttpContext);
        if (errorDetails.Length > 0)
        {
            return errorDetails.Select(detail => new ApiError
            {
                ErrorId = errorId,
                StatusCode = code,
                Message = detail
            }).ToList();
        }

        return
        [
            new ApiError
            {
                ErrorId = errorId,
                StatusCode = code,
                Message = message
            }
        ];
    }

    /// <summary>
    /// 200 OK.
    /// </summary>
    protected OkObjectResult OkResponse<T>(T? data, string message = "")
    {
        return Ok(Response<T>.Success(data, message));
    }

    /// <summary>
    /// 201 Created with a Location header.
    /// </summary>
    protected CreatedAtActionResult CreatedAtActionResponse<T>(string actionName, object routeValues, T data, string message)
    {
        return CreatedAtAction(actionName, routeValues, Response<T>.Success(data, message));
    }

    /// <summary>
    /// 201 Created without a Location header (batch creates).
    /// </summary>
    protected ObjectResult CreatedResponse<T>(T data, string message)
    {
        return StatusCode(StatusCodes.Status201Created, Response<T>.Success(data, message));
    }

    /// <summary>
    /// 400 Bad Request.
    /// </summary>
    protected BadRequestObjectResult BadRequestResponse<T>(string message, params string[] errorDetails)
    {
        return BadRequest(Response<T>.Failure(message, BuildErrors(HttpStatusCode.BadRequest, message, errorDetails)));
    }

    /// <summary>
    /// 404 Not Found.
    /// </summary>
    protected NotFoundObjectResult NotFoundResponse<T>(string message, params string[] errorDetails)
    {
        return NotFound(Response<T>.Failure(message, BuildErrors(HttpStatusCode.NotFound, message, errorDetails)));
    }

    /// <summary>
    /// 409 Conflict.
    /// </summary>
    protected ConflictObjectResult ConflictResponse<T>(string message, params string[] errorDetails)
    {
        return Conflict(Response<T>.Failure(message, BuildErrors(HttpStatusCode.Conflict, message, errorDetails)));
    }

    /// <summary>
    /// 401 Unauthorized.
    /// </summary>
    protected UnauthorizedObjectResult UnauthorizedResponse<T>(string message, params string[] errorDetails)
    {
        return Unauthorized(Response<T>.Failure(message, BuildErrors(HttpStatusCode.Unauthorized, message, errorDetails)));
    }

    /// <summary>
    /// 403 Forbidden.
    /// </summary>
    protected ObjectResult ForbiddenResponse<T>(string message, params string[] errorDetails)
    {
        return StatusCode(
            StatusCodes.Status403Forbidden,
            Response<T>.Failure(message, BuildErrors(HttpStatusCode.Forbidden, message, errorDetails)));
    }

    /// <summary>
    /// A <see cref="CursorPagedResult{T}"/> (keyset) for cursor paging, a <see cref="PagedResult{T}"/> (OFFSET) for a
    /// page number, otherwise all items. COUNT runs only for includeTotal.
    /// </summary>
    protected async Task<IActionResult> GetPagedOrAllAsync<T>(
        PaginationQuery pagination,
        Func<CancellationToken, Task<IReadOnlyList<T>>> getAllAsync,
        Func<int, int, bool, CancellationToken, Task<PageSlice<T>>> getPagedAsync,
        Func<int, int, bool, CancellationToken, Task<PageSlice<T>>> getPagedAfterAsync,
        string entityNamePlural,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pagination);
        string plural = entityNamePlural.ToLowerInvariant();
        int pageSize = pagination.EffectivePageSize;

        if (pagination.IsPageNumbered)
        {
            var page = await getPagedAsync(pagination.Page, pageSize, pagination.IncludeTotal, cancellationToken);
            PagedResult<T> pagedResult = new(page.Items, pagination.Page, pageSize, page.HasMore, page.TotalCount);
            string message = pagedResult.TotalPages is { } totalPages
                ? $"Retrieved page {pagination.Page} of {totalPages} ({page.Items.Count} of {page.TotalCount} total {plural})"
                : $"Retrieved page {pagination.Page} ({page.Items.Count} {plural})";
            return OkResponse(pagedResult, message);
        }

        if (pagination.IsCursorPaged)
        {
            int afterId = 0;
            // ValidationFilter rejects a bad cursor first; never restart from page one on one.
            if (pagination.Cursor is not null && !PageCursor.TryDecode(pagination.Cursor, out afterId))
            {
                return BadRequestResponse<CursorPagedResult<T>>("Validation failed", "Cursor is invalid; send the nextCursor of the previous page.");
            }

            var page = await getPagedAfterAsync(afterId, pageSize, pagination.IncludeTotal, cancellationToken);
            string? nextCursor = page.NextAfterId is { } next ? PageCursor.Encode(next) : null;
            return OkResponse(
                new CursorPagedResult<T>(page.Items, pageSize, nextCursor, page.TotalCount),
                $"Retrieved {page.Items.Count} {plural}");
        }

        var all = await getAllAsync(cancellationToken);
        return OkResponse(all, $"{entityNamePlural} retrieved successfully");
    }

    /// <summary>
    /// 404 Not Found for "{entity} with {identifier} {value}", logged once here at Debug (<see cref="ControllerLog.NotFound"/>).
    /// </summary>
    protected NotFoundObjectResult HandleNotFound<T>(string entityName, string identifier, object identifierValue)
    {
        Logger.NotFound(entityName, identifier, Convert.ToString(identifierValue, CultureInfo.InvariantCulture));
        return NotFoundResponse<T>($"{entityName} not found", $"{entityName} with {identifier} {identifierValue} not found.");
    }

    /// <summary>
    /// GET by ID: 200, or the mapped error.
    /// </summary>
    protected async Task<ActionResult<Response<T>>> GetByIdOrNotFoundAsync<T>(
        int id,
        Func<int, CancellationToken, Task<Result<T>>> getByIdAsync,
        string entityName,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var result = await getByIdAsync(id, cancellationToken);
        if (!result.IsSuccess)
        {
            return ErrorResponse(result, () => HandleNotFound<T>(entityName, "ID", id));
        }

        return OkResponse(result.Value, successMessage);
    }

    /// <summary>
    /// GET by another key (email, name): 200, or the mapped error.
    /// </summary>
    protected async Task<ActionResult<Response<T>>> GetByPropertyOrNotFoundAsync<T>(
        Func<CancellationToken, Task<Result<T>>> getAsync,
        string entityName,
        string identifier,
        object identifierValue,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var result = await getAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            return ErrorResponse(result, () => HandleNotFound<T>(entityName, identifier, identifierValue));
        }

        return OkResponse(result.Value, successMessage);
    }

    /// <summary>
    /// POST returning Result: 201, or the mapped error.
    /// </summary>
    protected async Task<ActionResult<Response<T>>> CreateResourceOrErrorAsync<T>(
        Func<CancellationToken, Task<Result<T>>> createAsync,
        string actionName,
        Func<T, object> getRouteValues,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var result = await createAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            return ErrorResponse(result, () => NotFoundResponse<T>("The requested resource was not found."));
        }

        return CreatedAtActionResponse(actionName, getRouteValues(result.Value), result.Value, successMessage);
    }

    /// <summary>
    /// Batch POST returning Result: 201 without a Location header, or the mapped error (409 for a conflict).
    /// </summary>
    protected async Task<ActionResult<Response<IReadOnlyList<T>>>> CreateBatchOrErrorAsync<T>(
        Func<CancellationToken, Task<Result<IReadOnlyList<T>>>> createAsync,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var result = await createAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            return ErrorResponse(result, () => NotFoundResponse<IReadOnlyList<T>>("The requested resource was not found."));
        }

        return CreatedResponse(result.Value, successMessage);
    }

    /// <summary>
    /// Batch PUT or DELETE returning Result: 200 with the affected items or ids, or the mapped error.
    /// </summary>
    protected async Task<ActionResult<Response<IReadOnlyList<T>>>> BatchOrErrorAsync<T>(
        Func<CancellationToken, Task<Result<IReadOnlyList<T>>>> batchAsync,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var result = await batchAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            return ErrorResponse(result, () => NotFoundResponse<IReadOnlyList<T>>("The requested resource was not found."));
        }

        return OkResponse(result.Value, successMessage);
    }

    /// <summary>
    /// PUT/PATCH: 204, or the mapped error.
    /// </summary>
    protected async Task<IActionResult> UpdateOrNotFoundAsync<T>(
        int id,
        Func<int, CancellationToken, Task<Result<T>>> updateAsync,
        string entityName,
        CancellationToken cancellationToken)
    {
        var result = await updateAsync(id, cancellationToken);
        if (!result.IsSuccess)
        {
            return ErrorResponse(result, () => HandleNotFound<T>(entityName, "ID", id));
        }

        return NoContent();
    }

    /// <summary>
    /// DELETE: 204 if deleted (or already deleted), 404 if not found, or the mapped rule violation (400/409).
    /// </summary>
    protected async Task<IActionResult> DeleteOrNotFoundAsync(
        int id,
        Func<int, CancellationToken, Task<Result<int>>> deleteAsync,
        string entityName,
        CancellationToken cancellationToken)
    {
        var result = await deleteAsync(id, cancellationToken);
        if (!result.IsSuccess)
        {
            return ErrorResponse<int, object>(result, () => HandleNotFound<object>(entityName, "ID", id));
        }

        return NoContent();
    }

    /// <summary>
    /// Maps a failed Result: NotFound 404 (with its message when it has one), Failure 400, Conflict 409.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown for an uninitialized (default) result.</exception>
    private ObjectResult ErrorResponse<T>(Result<T> result, Func<NotFoundObjectResult> notFound)
    {
        return ErrorResponse<T, T>(result, notFound);
    }

    /// <summary>As above, with a <see cref="Response{T}"/> of <typeparamref name="TData"/> (e.g. object for a DELETE).</summary>
    private ObjectResult ErrorResponse<TValue, TData>(Result<TValue> result, Func<NotFoundObjectResult> notFound)
    {
        if (result.IsConflict)
        {
            return ConflictResponse<TData>(result.ErrorMessage!);
        }

        if (result.IsFailure)
        {
            return BadRequestResponse<TData>(result.ErrorMessage!);
        }

        if (result.IsNotFound)
        {
            return result.ErrorMessage is null ? notFound() : NotFoundResponse<TData>(result.ErrorMessage);
        }

        throw new InvalidOperationException("Result is not initialized (default); the service returned no outcome.");
    }
}
