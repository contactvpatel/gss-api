using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GSS.Api.Extensions.Core;
using GSS.Api.Extensions.Middleware;
using GSS.Api.Middleware;
using GSS.Api.Models;
using GSS.UnitTests.Common;
using GSS.Util;
using GSS.Util.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GSS.UnitTests.API.Extensions;

/// <summary>
/// Drives the real <see cref="MiddlewareExtensions.ConfigureMiddleware"/> in a strict (non-Development) environment,
/// so ApiPipeline.NET's startup guards run, and pins behaviour that breaks silently when the order changes.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class MiddlewarePipelineOrderTests
{
    private static readonly IPAddress TrustedProxy = IPAddress.Parse("10.0.0.5");

    [Fact]
    public async Task FaultInEndpoint_ReturnsErrorEnvelope_WithCorrelationAndSecurityHeaders()
    {
        // Arrange
        await using var app = CreateApp();
        app.ConfigureMiddleware();
        app.MapGet("/boom", (Func<IResult>)(() => throw new InvalidOperationException("boom")));
        var pipeline = BuildPipeline(app);

        // Act
        var context = await SendAsync(pipeline, app, "/boom");

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        string body = await ReadBodyAsync(context);
        body.Should().Contain("errorId").And.Contain("succeeded");
        context.Response.Headers.Should().ContainKey("X-Correlation-Id",
            because: "exception handling runs inside the correlation phase");
        context.Response.Headers.Should().ContainKey("X-Content-Type-Options",
            because: "security headers are written on error responses too");
        var envelope = JsonSerializer.Deserialize<Response<ApiError>>(body, JsonSerializerOptions.Web);
        envelope!.Errors.Should().ContainSingle().Which.ErrorId.Should().Be(context.Response.Headers["X-Correlation-Id"].ToString(),
            because: "the ErrorId is the correlation id, so support can find the logs from the error response");
    }

    [Fact]
    public async Task KeyNotFoundInEndpoint_Returns500Envelope()
    {
        // Arrange - a dictionary-indexer bug is a server error, not a client 404
        await using var app = CreateApp();
        app.ConfigureMiddleware();
        app.MapGet("/missing", (Func<IResult>)(() => throw new KeyNotFoundException("order 42")));
        var pipeline = BuildPipeline(app);

        // Act
        var context = await SendAsync(pipeline, app, "/missing");

        // Assert
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        (await ReadBodyAsync(context)).Should().Contain("Please use the ErrorId").And.NotContain("order 42");
    }

    [Fact]
    public async Task FaultInEndpoint_IsLoggedOnce_ByApiExceptionHandler()
    {
        // Arrange
        CapturingLoggerProvider logs = new();
        await using var app = CreateApp(loggerProvider: logs);
        app.ConfigureMiddleware();
        app.MapGet("/boom", (Func<IResult>)(() => throw new InvalidOperationException("boom")));
        var pipeline = BuildPipeline(app);

        // Act
        await SendAsync(pipeline, app, "/boom");

        // Assert
        logs.Entries.Where(e => e.Level >= LogLevel.Error).Should().ContainSingle()
            .Which.Category.Should().Be(typeof(ApiExceptionHandler).FullName,
                because: "the framework middleware does not log an exception its IExceptionHandler handled");
    }

    [Fact]
    public async Task ProxiedHttpsRequest_PassesHttpsEnforcement()
    {
        // Arrange
        await using var app = CreateApp();
        app.ConfigureMiddleware();
        app.MapGet("/anything", () => Results.Ok());
        var pipeline = BuildPipeline(app);

        // Act - plain HTTP from the trusted proxy carrying X-Forwarded-Proto: https
        var context = await SendAsync(pipeline, app, "/anything", scheme: "http", forwardedProto: "https");

        // Assert
        context.Response.StatusCode.Should().Be(200,
            because: "forwarded headers run before HTTPS enforcement, so Request.IsHttps reflects the client scheme");
    }

    [Fact]
    public async Task PlainHttpWithoutForwardedProto_IsRejectedWithErrorEnvelope()
    {
        // Arrange
        await using var app = CreateApp();
        app.ConfigureMiddleware();
        app.MapGet("/anything", () => Results.Ok());
        var pipeline = BuildPipeline(app);

        // Act
        var context = await SendAsync(pipeline, app, "/anything", scheme: "http");

        // Assert
        context.Response.StatusCode.Should().Be(400);
        context.Response.ContentType.Should().StartWith("application/json");
        var envelope = JsonSerializer.Deserialize<Response<ApiError>>(await ReadBodyAsync(context), JsonSerializerOptions.Web);
        envelope!.Succeeded.Should().BeFalse();
        envelope.Message.Should().Be("HTTPS is required.");
        var error = envelope.Errors.Should().ContainSingle().Subject;
        error.StatusCode.Should().Be(400);
        error.ErrorId.Should().Be(context.Response.Headers["X-Correlation-Id"].ToString());
    }

    [Fact]
    public async Task RateLimitRejection_UsesResponseEnvelope_WithRetryAfter()
    {
        // Arrange
        await using var app = CreateApp(new Dictionary<string, string?>
        {
            ["ApiPipeline:RateLimiting:Policies:0:PermitLimit"] = "1"
        });
        app.ConfigureMiddleware();
        app.MapGet("/limited", () => Results.Ok());
        var pipeline = BuildPipeline(app);

        // Act
        await SendAsync(pipeline, app, "/limited");
        var rejected = await SendAsync(pipeline, app, "/limited");

        // Assert
        rejected.Response.StatusCode.Should().Be((int)HttpStatusCode.TooManyRequests);
        rejected.Response.Headers.Should().ContainKey("Retry-After");
        string body = await ReadBodyAsync(rejected);
        body.Should().Contain("\"succeeded\":false").And.Contain("\"statusCode\":429",
            because: "429s use the Response<ApiError> envelope, not problem+json");
    }

    [Fact]
    public async Task ForwardedHeadersWithoutTrustedProxies_FailStartupOutsideDevelopment()
    {
        // Arrange
        await using var app = CreateApp(trustProxy: false);

        // Act
        Action act = app.ConfigureMiddleware;

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*KnownNetworks*");
    }

    private static WebApplication CreateApp(
        Dictionary<string, string?>? extraConfiguration = null,
        bool trustProxy = true,
        ILoggerProvider? loggerProvider = null)
    {
        Dictionary<string, string?> settings = new()
        {
            [ConfigurationKeys.AppSettingsEnvironment] = AppSettingModel.QA,
            ["ApiPipeline:Cors:Enabled"] = "true",
            ["ApiPipeline:Cors:AllowedOrigins:0"] = "https://a.example",
            ["ApiPipeline:RateLimiting:Enabled"] = "true",
            ["ApiPipeline:RateLimiting:DefaultPolicy"] = "global",
            ["ApiPipeline:RateLimiting:Policies:0:Name"] = "global",
            ["ApiPipeline:RateLimiting:Policies:0:PermitLimit"] = "100",
            ["ApiPipeline:RateLimiting:Policies:0:Window"] = "00:01:00"
        };

        if (trustProxy)
        {
            settings["ApiPipeline:ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8";
            settings["ApiPipeline:ForwardedHeaders:ClearDefaultProxies"] = "true";
        }

        if (extraConfiguration != null)
        {
            foreach (var entry in extraConfiguration)
            {
                settings[entry.Key] = entry.Value;
            }
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        if (loggerProvider != null)
        {
            builder.Logging.ClearProviders().AddProvider(loggerProvider);
        }

        var services = builder.Services;
        services.AddApiPipeline(builder.Configuration);
        services.AddSingleton<IProblemDetailsService, ApiErrorProblemDetailsService>();
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddAuthentication();
        services.AddAuthorization();
        services.AddControllers();

        var app = builder.Build();
        app.UseConfiguredEnvironment();
        return app;
    }

    /// <summary>Builds the request delegate, adding UseEndpoints where the host would auto-insert it.</summary>
    private static RequestDelegate BuildPipeline(WebApplication app)
    {
        app.UseEndpoints(_ => { });
        return ((IApplicationBuilder)app).Build();
    }

    private static async Task<HttpContext> SendAsync(
        RequestDelegate pipeline,
        WebApplication app,
        string path,
        string scheme = "https",
        string? forwardedProto = null)
    {
        DefaultHttpContext context = new();
        TestHttpResponseFeature responseFeature = new();
        context.Features.Set<IHttpResponseFeature>(responseFeature);
        context.RequestServices = app.Services;
        context.Request.Method = "GET";
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString("localhost");
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = TrustedProxy;

        if (forwardedProto != null)
        {
            context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
        }

        await pipeline(context);
        await responseFeature.FireOnStartingAsync();

        // Mirror the header dictionary the pipeline wrote into onto the context for assertions.
        foreach (var header in responseFeature.Headers)
        {
            context.Response.Headers[header.Key] = header.Value;
        }

        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, LogLevel Level)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(categoryName, Entries);
        }

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<(string Category, LogLevel Level)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                entries.Enqueue((category, logLevel));
            }
        }
    }
}
