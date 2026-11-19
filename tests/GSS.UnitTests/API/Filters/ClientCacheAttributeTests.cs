using FluentAssertions;
using GSS.Api.Filters;
using GSS.UnitTests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSS.UnitTests.API.Filters;

[Trait("Category", TestCategories.Unit)]
public class ClientCacheAttributeTests
{
    [Fact]
    public async Task GetWithOkObjectResult_SetsPrivateNoCacheAndWeakETag()
    {
        (ResultExecutingContext context, NextProbe next) = CreateContext(new OkObjectResult(new { Value = "current" }));

        await new ClientCacheAttribute().OnResultExecutionAsync(context, next.Delegate);

        context.HttpContext.Response.Headers.CacheControl.ToString().Should().Be(ClientCacheAttribute.CacheControlValue);
        context.HttpContext.Response.Headers.ETag.ToString().Should().StartWith("W/\"");
        next.Called.Should().BeTrue();
    }

    [Fact]
    public async Task GetWithOkObjectResult_WritesSerializedJsonWithoutMvcFormatter()
    {
        (ResultExecutingContext context, NextProbe next) = CreateContext(
            new OkObjectResult(new { Value = "current" }),
            services => services.AddSingleton<IActionResultExecutor<ObjectResult>, MarkerObjectResultExecutor>());
        context.HttpContext.Response.Body = new MemoryStream();

        await new ClientCacheAttribute().OnResultExecutionAsync(context, next.Delegate);
        await context.Result.ExecuteResultAsync(context);

        context.HttpContext.Response.ContentType.Should().Be("application/json; charset=utf-8");
        context.HttpContext.Response.ContentLength.Should().Be(19);
        context.HttpContext.Response.Body.Position = 0;
        using StreamReader reader = new(context.HttpContext.Response.Body);
        (await reader.ReadToEndAsync()).Should().Be("{\"value\":\"current\"}");
    }

    [Fact]
    public async Task HeadWithOkObjectResult_WritesHeadersWithoutBody()
    {
        (ResultExecutingContext context, NextProbe next) = CreateContext(new OkObjectResult(new { Value = "current" }));
        context.HttpContext.Request.Method = HttpMethods.Head;
        context.HttpContext.Response.Body = new MemoryStream();

        await new ClientCacheAttribute().OnResultExecutionAsync(context, next.Delegate);
        await context.Result.ExecuteResultAsync(context);

        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.HttpContext.Response.ContentType.Should().Be("application/json; charset=utf-8");
        context.HttpContext.Response.ContentLength.Should().Be(19);
        context.HttpContext.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task GetWithMatchingIfNoneMatch_Returns304WithoutAnObjectResult()
    {
        object value = new { Value = "unchanged" };
        (ResultExecutingContext first, NextProbe firstNext) = CreateContext(new OkObjectResult(value));
        ClientCacheAttribute attribute = new();
        await attribute.OnResultExecutionAsync(first, firstNext.Delegate);

        (ResultExecutingContext revalidation, NextProbe revalidationNext) = CreateContext(new OkObjectResult(value));
        revalidation.HttpContext.Request.Headers.IfNoneMatch = first.HttpContext.Response.Headers.ETag;

        await attribute.OnResultExecutionAsync(revalidation, revalidationNext.Delegate);

        revalidation.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status304NotModified);
        revalidation.HttpContext.Response.Headers.ETag.Should().Equal(first.HttpContext.Response.Headers.ETag);
    }

    [Fact]
    public async Task GetWithNonOkResult_DoesNotOverridePipelineCachingHeaders()
    {
        (ResultExecutingContext context, NextProbe next) = CreateContext(new NotFoundObjectResult(new { Error = "missing" }));

        await new ClientCacheAttribute().OnResultExecutionAsync(context, next.Delegate);

        context.HttpContext.Response.Headers.CacheControl.Should().BeEmpty();
        context.HttpContext.Response.Headers.ETag.Should().BeEmpty();
        context.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetWithNonOkResponseStatus_DoesNotOverridePipelineCachingHeaders()
    {
        ObjectResult result = new(new { Error = "missing" });
        (ResultExecutingContext context, NextProbe next) = CreateContext(result);
        context.HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;

        await new ClientCacheAttribute().OnResultExecutionAsync(context, next.Delegate);

        context.HttpContext.Response.Headers.CacheControl.Should().BeEmpty();
        context.HttpContext.Response.Headers.ETag.Should().BeEmpty();
        context.Result.Should().BeSameAs(result);
    }

    [Fact]
    public async Task GetWithNonOkProblemDetails_DoesNotOverridePipelineCachingHeaders()
    {
        ObjectResult result = new(new ProblemDetails { Status = StatusCodes.Status422UnprocessableEntity });
        (ResultExecutingContext context, NextProbe next) = CreateContext(result);

        await new ClientCacheAttribute().OnResultExecutionAsync(context, next.Delegate);

        context.HttpContext.Response.Headers.CacheControl.Should().BeEmpty();
        context.HttpContext.Response.Headers.ETag.Should().BeEmpty();
        context.Result.Should().BeSameAs(result);
    }

    private static (ResultExecutingContext Context, NextProbe Next) CreateContext(
        IActionResult result,
        Action<IServiceCollection>? configureServices = null)
    {
        ServiceCollection services = new();
        services.AddOptions();
        services.Configure<JsonOptions>(_ => { });
        configureServices?.Invoke(services);

        DefaultHttpContext httpContext = new();
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.RequestServices = services.BuildServiceProvider();
        ActionContext actionContext = new(httpContext, new(), new());
        ResultExecutingContext context = new(actionContext, [], result, new object());
        return (context, new NextProbe(context));
    }

    private sealed class MarkerObjectResultExecutor : IActionResultExecutor<ObjectResult>
    {
        public Task ExecuteAsync(ActionContext context, ObjectResult result)
        {
            return context.HttpContext.Response.WriteAsync("formatter-ran");
        }
    }

    private sealed class NextProbe(ResultExecutingContext context)
    {
        public bool Called { get; private set; }

        public ResultExecutionDelegate Delegate => () =>
        {
            Called = true;
            return Task.FromResult(new ResultExecutedContext(
                context,
                context.Filters,
                context.Result,
                context.Controller));
        };
    }
}
