using System.Text;
using FluentAssertions;
using GSS.Api.Filters;
using GSS.Api.Models;
using GSS.Business.Models;
using GSS.Business.Services.Interfaces;
using GSS.Core.Helpers;
using GSS.UnitTests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace GSS.UnitTests.API.Filters;

/// <summary>
/// Unit tests for IdempotentAttribute.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class IdempotentAttributeTests
{
    private const string Body = "{\"email\":\"a@b.c\"}";

    private readonly Mock<IIdempotencyService> _service = new();
    private readonly DefaultHttpContext _http = new();
    private readonly MemoryStream _sent = new();

    public IdempotentAttributeTests()
    {
        _http.RequestServices = new ServiceCollection().AddSingleton(_service.Object).BuildServiceProvider();
        _http.Request.Method = HttpMethods.Post;
        _http.Request.Path = "/api/v1/customers";
        _http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(Body));
        _http.Response.Body = _sent;
    }

    [Fact]
    public async Task WithoutTheHeader_RunsTheActionUntouched()
    {
        var (context, next) = Context(() => { });

        await new IdempotentAttribute().OnResourceExecutionAsync(context, next.Delegate);

        next.Calls.Should().Be(1);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WithoutTheHeader_WhenRequired_Is400()
    {
        var (context, next) = Context(() => { });

        await new IdempotentAttribute { Required = true }.OnResourceExecutionAsync(context, next.Delegate);

        Status(context).Should().Be(400);
        next.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("tab\there")]
    [InlineData("café")]
    public async Task InvalidKey_Is400(string key)
    {
        _http.Request.Headers[IdempotencyLimits.HeaderName] = key;
        var (context, next) = Context(() => { });

        await new IdempotentAttribute().OnResourceExecutionAsync(context, next.Delegate);

        Status(context).Should().Be(400);
        next.Calls.Should().Be(0);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task KeyLongerThanTheLimit_Is400()
    {
        _http.Request.Headers[IdempotencyLimits.HeaderName] = new string('k', IdempotencyLimits.MaxKeyLength + 1);
        var (context, next) = Context(() => { });

        await new IdempotentAttribute().OnResourceExecutionAsync(context, next.Delegate);

        Status(context).Should().Be(400);
    }

    [Fact]
    public async Task TwoKeyHeaders_Is400()
    {
        _http.Request.Headers[IdempotencyLimits.HeaderName] = new[] { "a", "b" };
        var (context, next) = Context(() => { });

        await new IdempotentAttribute().OnResourceExecutionAsync(context, next.Delegate);

        Status(context).Should().Be(400);
    }

    [Fact]
    public async Task Mismatch_Is422_WithTheErrorEnvelope()
    {
        _http.Request.Headers[IdempotencyLimits.HeaderName] = "k";
        _service.Setup(s => s.BeginAsync("k", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyStart(IdempotencyOutcome.Mismatch));
        var (context, next) = Context(() => { });

        await new IdempotentAttribute().OnResourceExecutionAsync(context, next.Delegate);

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(422);
        result.Value.Should().BeOfType<Response<object?>>().Which.Succeeded.Should().BeFalse();
        next.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Replay_WritesTheStoredResponse_MarkedAsAReplay()
    {
        _http.Request.Headers[IdempotencyLimits.HeaderName] = "k";
        _service.Setup(s => s.BeginAsync("k", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyStart(IdempotencyOutcome.Replay, new StoredResponse(201, "{\"id\":5}", "/api/v1/customers/5")));
        var (context, next) = Context(() => { });

        await new IdempotentAttribute().OnResourceExecutionAsync(context, next.Delegate);
        await context.Result!.ExecuteResultAsync(new ActionContext(_http, new RouteData(), new ActionDescriptor()));

        next.Calls.Should().Be(0, "the action never runs again");
        _http.Response.StatusCode.Should().Be(201);
        _http.Response.Headers[IdempotencyLimits.ReplayedHeaderName].ToString().Should().Be("true");
        _http.Response.Headers.Location.ToString().Should().Be("/api/v1/customers/5");
        ResponseText().Should().Be("{\"id\":5}");
    }

    [Fact]
    public async Task Proceed_WithA2xx_StoresTheResponseAndSendsItOnceCommitted()
    {
        _http.Request.Headers[IdempotencyLimits.HeaderName] = "k";
        StoredResponse? stored = null;
        string? hash = null;
        bool sentBeforeCommit = true;
        Proceed();
        _service.Setup(s => s.CompleteAsync("k", It.IsAny<string>(), It.IsAny<StoredResponse>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, StoredResponse, CancellationToken>((_, h, r, _) =>
            {
                hash = h;
                stored = r;
                sentBeforeCommit = ResponseText().Length > 0;
            })
            .Returns(Task.CompletedTask);
        var (context, next) = Context(() =>
        {
            _http.Response.StatusCode = 201;
            _http.Response.Headers.Location = "/api/v1/customers/9";
            _http.Response.Body.Write(Encoding.UTF8.GetBytes("{\"id\":9}"));
        });

        await new IdempotentAttribute().OnResourceExecutionAsync(context, next.Delegate);

        stored.Should().Be(new StoredResponse(201, "{\"id\":9}", "/api/v1/customers/9"));
        hash.Should().HaveLength(64);
        sentBeforeCommit.Should().BeFalse("the response is held back until the commit succeeded");
        ResponseText().Should().Be("{\"id\":9}");
        _service.Verify(s => s.AbortAsync(), Times.Never);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(500)]
    public async Task Proceed_WithAnErrorStatus_RollsBackAndStoresNothing(int status)
    {
        _http.Request.Headers[IdempotencyLimits.HeaderName] = "k";
        Proceed();
        var (context, next) = Context(() =>
        {
            _http.Response.StatusCode = status;
            _http.Response.Body.Write(Encoding.UTF8.GetBytes("err"));
        });

        await new IdempotentAttribute().OnResourceExecutionAsync(context, next.Delegate);

        _service.Verify(s => s.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StoredResponse>(), It.IsAny<CancellationToken>()), Times.Never);
        _service.Verify(s => s.AbortAsync(), Times.Once, "the key is released so the client can fix the request and resend it");
        ResponseText().Should().Be("err", "the error is still sent");
    }

    [Fact]
    public async Task Proceed_WhenTheActionThrows_RollsBackAndRestoresTheBody()
    {
        _http.Request.Headers[IdempotencyLimits.HeaderName] = "k";
        var original = _http.Response.Body;
        Proceed();
        var (context, next) = Context(() => throw new InvalidOperationException("boom"));

        var act = () => new IdempotentAttribute().OnResourceExecutionAsync(context, next.Delegate);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _service.Verify(s => s.AbortAsync(), Times.Once);
        _http.Response.Body.Should().BeSameAs(original, "the exception handler writes to the real response");
    }

    [Fact]
    public async Task Proceed_WhenTheCommitFails_RollsBackAndSendsNothing()
    {
        _http.Request.Headers[IdempotencyLimits.HeaderName] = "k";
        Proceed();
        _service.Setup(s => s.CompleteAsync("k", It.IsAny<string>(), It.IsAny<StoredResponse>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException());
        var (context, next) = Context(() =>
        {
            _http.Response.StatusCode = 201;
            _http.Response.Body.Write(Encoding.UTF8.GetBytes("{}"));
        });

        var act = () => new IdempotentAttribute().OnResourceExecutionAsync(context, next.Delegate);

        await act.Should().ThrowAsync<TimeoutException>();
        ResponseText().Should().BeEmpty("a created resource that was not committed must never be reported as created");
    }

    [Fact]
    public async Task TheSameRequest_HashesTheSame_ADifferentBodyDoesNot()
    {
        _http.Request.Headers[IdempotencyLimits.HeaderName] = "k";
        List<string> hashes = [];
        _service.Setup(s => s.BeginAsync("k", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, h, _) => hashes.Add(h))
            .ReturnsAsync(new IdempotencyStart(IdempotencyOutcome.Mismatch));

        foreach (string body in new[] { Body, Body, "{\"email\":\"other\"}" })
        {
            _http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            var (context, next) = Context(() => { });
            await new IdempotentAttribute().OnResourceExecutionAsync(context, next.Delegate);
        }

        hashes[0].Should().Be(hashes[1]);
        hashes[2].Should().NotBe(hashes[0]);
    }

    [Fact]
    public async Task TheBody_StaysReadableForModelBindingAfterHashing()
    {
        _http.Request.Headers[IdempotencyLimits.HeaderName] = "k";
        Proceed();
        string? seen = null;
        var (context, next) = Context(() => seen = new StreamReader(_http.Request.Body).ReadToEnd());
        _service.Setup(s => s.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StoredResponse>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await new IdempotentAttribute().OnResourceExecutionAsync(context, next.Delegate);

        seen.Should().Be(Body);
    }

    private void Proceed()
    {
        _service.Setup(s => s.BeginAsync("k", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyStart(IdempotencyOutcome.Proceed));
    }

    private string ResponseText()
    {
        // What reached the real response stream, not the buffer the filter swaps in.
        return Encoding.UTF8.GetString(_sent.ToArray());
    }

    private static int Status(ResourceExecutingContext context)
    {
        return context.Result.Should().BeOfType<ObjectResult>().Subject.StatusCode!.Value;
    }

    /// <summary>A resource context whose action runs <paramref name="action"/>.</summary>
    private (ResourceExecutingContext Context, Next Next) Context(Action action)
    {
        var actionContext = new ActionContext(_http, new RouteData(), new ActionDescriptor());
        ResourceExecutingContext context = new(actionContext, [], []);
        Next next = new(() =>
        {
            action();
            return new ResourceExecutedContext(actionContext, []);
        });
        return (context, next);
    }

    private sealed class Next(Func<ResourceExecutedContext> run)
    {
        public int Calls { get; private set; }

        public ResourceExecutionDelegate Delegate => () =>
        {
            Calls++;
            return Task.FromResult(run());
        };
    }
}
