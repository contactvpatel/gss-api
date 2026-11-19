using FluentAssertions;
using GSS.Api.Controllers;
using GSS.Api.Models;
using GSS.Business.Models;
using GSS.UnitTests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GSS.UnitTests.API.Controllers;

/// <summary>One Result → HTTP mapping: NotFound 404, Failure 400, Conflict 409, default throws.</summary>
[Trait("Category", TestCategories.Unit)]
public class ApiControllerBaseResultMappingTests
{
    private readonly CapturingLogger _logger = new();
    private readonly TestController _controller;

    public ApiControllerBaseResultMappingTests()
    {
        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(new CapturingLoggerFactory(_logger))
            .BuildServiceProvider();
        _controller = new TestController
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } }
        };
    }

    [Fact]
    public async Task GetById_NotFound_Returns404AndLogsOnceAtDebug()
    {
        var result = await _controller.GetById(Result<int>.NotFound());

        result.Result.Should().BeOfType<NotFoundObjectResult>();
        var entry = _logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Debug);
        entry.EventId.Should().Be(2000);
        entry.Category.Should().EndWith(".TestController", because: "the category is the concrete controller");
        entry.Message.Should().Be("Thing not found: ID 1");
    }

    [Fact]
    public async Task Delete_NotFound_Returns404AndLogsOnceAtDebug()
    {
        var result = await _controller.Delete(Result<int>.NotFound());

        result.Should().BeOfType<NotFoundObjectResult>();
        _logger.Entries.Should().ContainSingle().Which.EventId.Should().Be(2000);
    }

    [Fact]
    public async Task Delete_Success_Returns204()
    {
        var result = await _controller.Delete(Result<int>.Success(1));

        result.Should().BeOfType<NoContentResult>();
    }

    /// <summary>A ValidateDeleteAsync violation: 409/400 with a Response&lt;object&gt; (no "data": 0).</summary>
    [Fact]
    public async Task Delete_RuleViolation_Returns409Or400WithAnObjectEnvelope()
    {
        AssertError<ConflictObjectResult, object>(await _controller.Delete(Result<int>.Conflict("Has orders")), 409, "Has orders");
        AssertError<BadRequestObjectResult, object>(await _controller.Delete(Result<int>.Failure("Rule broken")), 400, "Rule broken");
    }

    [Fact]
    public async Task GetById_Success_LogsNothing()
    {
        await _controller.GetById(Result<int>.Success(5));

        _logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task NotFound_WithoutHttpContext_StillReturns404()
    {
        var result = await new TestController().GetById(Result<int>.NotFound());

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetById_Failure_Returns400NotNotFound()
    {
        var result = await _controller.GetById(Result<int>.Failure("Rule broken"));

        AssertError<BadRequestObjectResult>(result.Result, 400, "Rule broken");
        _logger.Entries.Should().BeEmpty(because: "only a 404 is logged here");
    }

    [Fact]
    public async Task GetById_Conflict_Returns409()
    {
        var result = await _controller.GetById(Result<int>.Conflict("Already exists"));

        AssertError<ConflictObjectResult>(result.Result, 409, "Already exists");
    }

    [Fact]
    public async Task Update_Failure_Returns400NotNotFound()
    {
        var result = await _controller.Update(Result<int>.Failure("Rule broken"));

        AssertError<BadRequestObjectResult>(result, 400, "Rule broken");
    }

    [Fact]
    public async Task Update_Conflict_Returns409()
    {
        var result = await _controller.Update(Result<int>.Conflict("Already exists"));

        AssertError<ConflictObjectResult>(result, 409, "Already exists");
    }

    [Fact]
    public async Task Create_NotFound_Returns404NotServerError()
    {
        var result = await _controller.Create(Result<int>.NotFound());

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Create_Conflict_Returns409()
    {
        var result = await _controller.Create(Result<int>.Conflict("Already exists"));

        AssertError<ConflictObjectResult>(result.Result, 409, "Already exists");
    }

    [Fact]
    public async Task Create_Failure_Returns400()
    {
        var result = await _controller.Create(Result<int>.Failure("Rule broken"));

        AssertError<BadRequestObjectResult>(result.Result, 400, "Rule broken");
    }

    [Fact]
    public async Task UpdateBatch_Success_Returns200()
    {
        var result = await _controller.UpdateBatch(Result<IReadOnlyList<int>>.Success([1, 2]));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<Response<IReadOnlyList<int>>>().Which.Data.Should().Equal(1, 2);
    }

    [Fact]
    public async Task UpdateBatch_NotFound_Returns404WithServiceMessage()
    {
        var result = await _controller.UpdateBatch(
            Result<IReadOnlyList<int>>.NotFound("Things not found. IDs: 7, 9."));

        AssertError<NotFoundObjectResult, IReadOnlyList<int>>(result.Result, 404, "Things not found. IDs: 7, 9.");
    }

    [Fact]
    public async Task UpdateBatch_Failure_Returns400()
    {
        var result = await _controller.UpdateBatch(
            Result<IReadOnlyList<int>>.Failure("Duplicate Thing IDs in batch: 1."));

        AssertError<BadRequestObjectResult, IReadOnlyList<int>>(result.Result, 400, "Duplicate Thing IDs in batch: 1.");
    }

    [Fact]
    public async Task DefaultResult_Throws()
    {
        Func<Task> get = () => _controller.GetById(default);
        Func<Task> update = () => _controller.Update(default);
        Func<Task> create = () => _controller.Create(default);
        Func<Task> updateBatch = () => _controller.UpdateBatch(default);
        Func<Task> delete = () => _controller.Delete(default);

        await get.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not initialized*");
        await update.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not initialized*");
        await create.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not initialized*");
        await updateBatch.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not initialized*");
        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not initialized*");
    }

    private static void AssertError<TResult>(object? actionResult, short statusCode, string message)
        where TResult : ObjectResult
    {
        AssertError<TResult, int>(actionResult, statusCode, message);
    }

    private static void AssertError<TResult, TData>(object? actionResult, short statusCode, string message)
        where TResult : ObjectResult
    {
        var typed = actionResult.Should().BeOfType<TResult>().Subject;
        var body = typed.Value.Should().BeOfType<Response<TData>>().Subject;
        body.Succeeded.Should().BeFalse();
        body.Message.Should().Be(message);
        body.Errors.Should().ContainSingle().Which.StatusCode.Should().Be(statusCode);
    }

    private sealed class TestController : ApiControllerBase
    {
        public Task<ActionResult<Response<int>>> GetById(Result<int> outcome)
        {
            return GetByIdOrNotFoundAsync(1, (_, _) => Task.FromResult(outcome), "Thing", "ok", CancellationToken.None);
        }

        public Task<IActionResult> Delete(Result<int> outcome)
        {
            return DeleteOrNotFoundAsync(1, (_, _) => Task.FromResult(outcome), "Thing", CancellationToken.None);
        }

        public Task<IActionResult> Update(Result<int> outcome)
        {
            return UpdateOrNotFoundAsync(1, (_, _) => Task.FromResult(outcome), "Thing", CancellationToken.None);
        }

        public Task<ActionResult<Response<int>>> Create(Result<int> outcome)
        {
            return CreateResourceOrErrorAsync(_ => Task.FromResult(outcome), "GetById", id => new { id }, "created", CancellationToken.None);
        }

        public Task<ActionResult<Response<IReadOnlyList<int>>>> UpdateBatch(Result<IReadOnlyList<int>> outcome)
        {
            return BatchOrErrorAsync(_ => Task.FromResult(outcome), "updated", CancellationToken.None);
        }
    }

    private sealed record CapturedEntry(string Category, LogLevel Level, int EventId, string Message);

    private sealed class CapturingLogger
    {
        public List<CapturedEntry> Entries { get; } = [];
    }

    private sealed class CapturingLoggerFactory(CapturingLogger sink) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => new CategoryLogger(categoryName, sink);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class CategoryLogger(string category, CapturingLogger sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            sink.Entries.Add(new CapturedEntry(category, logLevel, eventId.Id, formatter(state, exception)));
        }
    }
}
